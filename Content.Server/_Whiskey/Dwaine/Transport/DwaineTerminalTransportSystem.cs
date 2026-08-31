// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Transport;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Transport;

/// <summary>
/// Owns terminal-to-mainframe sessions and bounded text transport. It contains no OS behavior.
/// </summary>
public sealed partial class DwaineTerminalTransportSystem : EntitySystem
{
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromSeconds(1);
    private const int PendingInputLines = 64;
    private const int PendingInputCharacters = 8192;

    [Dependency] private DwaineHardwareSystem _hardware = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _mainframes = new();
    private readonly Dictionary<string, HashSet<EntityUid>> _mainframesByNetwork =
        new(StringComparer.Ordinal);
    private ulong _nextSessionId = 1;
    private TimeSpan _nextValidation;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineMainframeComponent, MapInitEvent>(OnMainframeMapInit);
        SubscribeLocalEvent<DwaineMainframeComponent, ComponentShutdown>(OnMainframeShutdown);
        SubscribeLocalEvent<DwaineMainframeRuntimeComponent, ComponentShutdown>(OnMainframeRuntimeShutdown);
        SubscribeLocalEvent<DwaineTerminalLinkComponent, ComponentShutdown>(OnTerminalLinkShutdown);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalConnectMessage>(OnConnectMessage);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalDisconnectMessage>(OnDisconnectMessage);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalInputReceivedEvent>(OnTerminalInput);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalPresentationEvent>(OnPresentation);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineHardwarePowerChangedEvent>(OnTerminalPowerChanged);
        SubscribeLocalEvent<DwaineMainframeComponent, DwaineHardwarePowerChangedEvent>(OnMainframePowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextValidation)
            return;

        _nextValidation = _timing.CurTime + ValidationInterval;
        ValidateAllSessions();
    }

    private void OnMainframeMapInit(Entity<DwaineMainframeComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<DwaineMainframeRuntimeComponent>(ent, out var runtime))
            return;

        _mainframes.Add(ent.Owner);
        ReindexMainframe(ent.Owner, runtime);
    }

    private void OnMainframeShutdown(Entity<DwaineMainframeComponent> ent, ref ComponentShutdown args)
    {
        RemoveMainframeIndex(ent.Owner);
        DisconnectAll(ent.Owner, DwaineDisconnectReason.EntityRemoved);
    }

    private void OnMainframeRuntimeShutdown(Entity<DwaineMainframeRuntimeComponent> ent, ref ComponentShutdown args)
    {
        RemoveMainframeIndex(ent.Owner);
        DisconnectAll(ent.Owner, ent.Comp, DwaineDisconnectReason.EntityRemoved);
    }

    private void OnTerminalLinkShutdown(Entity<DwaineTerminalLinkComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp is { Mainframe: { } mainframe, Session: { } session })
            RemoveSessionFromMainframe(mainframe, session, ent.Owner, DwaineDisconnectReason.EntityRemoved);

        ClearLink(ent.Comp, DwaineDisconnectReason.EntityRemoved);
    }

    private void OnConnectMessage(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalConnectMessage args)
    {
        if (!_hardware.IsAuthorizedUiActor(ent.Owner, args.Actor)
            || !TryGetEntity(args.Target, out var mainframe)
            || mainframe is not { } mainframeUid)
        {
            return;
        }

        TryConnect(ent.Owner, mainframeUid, args.Actor, out _);
    }

    private void OnDisconnectMessage(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalDisconnectMessage args)
    {
        if (_hardware.IsAuthorizedUiActor(ent.Owner, args.Actor))
            TryDisconnect(ent.Owner, args.Actor);
    }

    private void OnTerminalInput(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalInputReceivedEvent args)
    {
        if (!TryGetValidSession(ent.Owner, out var mainframe, out var session)
            || session.Owner != args.Actor)
        {
            return;
        }

        if (!IsSessionStillValid(mainframe, session))
        {
            ForceDisconnect(ent.Owner, DwaineDisconnectReason.TopologyChanged);
            return;
        }

        session.PendingInput.Add(args.Text);
        var forwarded = new DwaineMainframeInputReceivedEvent(
            session.Id,
            session.Terminal,
            session.Owner,
            args.Text);
        RaiseLocalEvent(mainframe, ref forwarded);
    }

    private void OnPresentation(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalPresentationEvent args)
    {
        if (TryGetValidSession(ent.Owner, out var mainframe, out var session))
        {
            args.Status = DwaineTerminalConnectionStatus.Connected;
            args.ConnectedMainframe = Name(mainframe);
            args.OutputOverride = session.Output.Snapshot();
        }
        else if (TryComp<DwaineTerminalLinkComponent>(ent, out var link))
        {
            if (link.Session is not null)
                ClearLink(link, DwaineDisconnectReason.MainframeUnavailable);

            args.Status = link.PresentationStatus;
        }

        args.AvailableMainframes = GetAvailableMainframes(ent.Owner);
    }

    private void OnTerminalPowerChanged(Entity<DwaineTerminalComponent> ent, ref DwaineHardwarePowerChangedEvent args)
    {
        if (!args.Powered)
            ForceDisconnect(ent.Owner, DwaineDisconnectReason.TerminalUnavailable);
    }

    private void OnMainframePowerChanged(Entity<DwaineMainframeComponent> ent, ref DwaineHardwarePowerChangedEvent args)
    {
        if (!args.Powered)
            DisconnectAll(ent.Owner, DwaineDisconnectReason.MainframeUnavailable);
    }

    public DwaineConnectResult TryConnect(
        EntityUid terminal,
        EntityUid mainframe,
        EntityUid actor,
        out DwaineSessionId sessionId)
    {
        sessionId = default;

        if (TerminatingOrDeleted(terminal)
            || !TryComp<DwaineTerminalComponent>(terminal, out _)
            || !TryComp<DwaineTerminalLinkComponent>(terminal, out var link))
        {
            return DwaineConnectResult.InvalidTerminal;
        }

        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineMainframeComponent>(mainframe, out var mainframeConfig)
            || !TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var mainframeRuntime))
        {
            link.PresentationStatus = DwaineTerminalConnectionStatus.MainframeUnavailable;
            _hardware.UpdateUi(terminal);
            return DwaineConnectResult.InvalidMainframe;
        }

        if (!_hardware.IsAuthorizedUiActor(terminal, actor))
            return DwaineConnectResult.UnauthorizedActor;

        if (_hardware.GetStatus(terminal) != DwaineHardwareStatus.HardwareReady)
            return DwaineConnectResult.TerminalUnavailable;

        if (_hardware.GetStatus(mainframe) != DwaineHardwareStatus.HardwareReady)
            return DwaineConnectResult.MainframeUnavailable;

        if (link.Session is not null && !TryGetValidSession(terminal, out _, out _))
            ClearLink(link, DwaineDisconnectReason.MainframeUnavailable);

        if (link is { Session: { } existingId, Mainframe: { } existingMainframe })
        {
            if (existingMainframe == mainframe
                && link.SessionOwner == actor
                && mainframeRuntime.Sessions.ContainsKey(existingId))
            {
                sessionId = existingId;
                return DwaineConnectResult.AlreadyConnected;
            }

            return DwaineConnectResult.TerminalAlreadyConnected;
        }

        if (!TryValidateTopology(terminal, mainframe, out var topologyResult))
        {
            link.PresentationStatus = DwaineTerminalConnectionStatus.MainframeUnavailable;
            _hardware.UpdateUi(terminal);
            return topologyResult;
        }

        var capacity = Math.Clamp(mainframeConfig.MaxSessions, 1, DwaineMainframeComponent.HardMaxSessions);
        if (mainframeRuntime.Sessions.Count >= capacity)
            return DwaineConnectResult.CapacityReached;

        if (mainframeRuntime.TerminalSessions.TryGetValue(terminal, out var indexedId)
            && mainframeRuntime.Sessions.TryGetValue(indexedId, out var indexedSession))
        {
            if (indexedSession.Owner != actor)
                return DwaineConnectResult.TerminalAlreadyConnected;

            AssignLink(link, mainframe, actor, indexedId);
            sessionId = indexedId;
            _hardware.UpdateUi(terminal);
            return DwaineConnectResult.AlreadyConnected;
        }

        sessionId = AllocateSessionId();
        var outputLines = Math.Clamp(
            mainframeConfig.OutputLineLimit,
            1,
            DwaineMainframeComponent.HardMaxOutputLines);
        var outputCharacters = Math.Clamp(
            mainframeConfig.OutputCharacterLimit,
            1,
            DwaineMainframeComponent.HardMaxOutputCharacters);
        var session = new DwaineTerminalSession(
            sessionId,
            terminal,
            actor,
            _timing.CurTime,
            new DwaineBoundedTextBuffer(outputLines, outputCharacters),
            new DwaineBoundedTextBuffer(PendingInputLines, PendingInputCharacters));

        mainframeRuntime.Sessions.Add(sessionId, session);
        mainframeRuntime.TerminalSessions.Add(terminal, sessionId);
        AssignLink(link, mainframe, actor, sessionId);
        var connected = new DwaineMainframeSessionConnectedEvent(sessionId, terminal, actor);
        RaiseLocalEvent(mainframe, ref connected);
        _hardware.UpdateUi(terminal);
        return DwaineConnectResult.Connected;
    }

    public bool TryDisconnect(EntityUid terminal, EntityUid actor)
    {
        if (!TryComp<DwaineTerminalLinkComponent>(terminal, out var link)
            || link.SessionOwner != actor
            || link.Mainframe is not { } mainframe
            || link.Session is not { } session)
        {
            return false;
        }

        RemoveSessionFromMainframe(mainframe, session, terminal, DwaineDisconnectReason.Requested);
        ClearLink(link, DwaineDisconnectReason.Requested);
        _hardware.UpdateUi(terminal);
        return true;
    }

    public bool WriteOutput(EntityUid mainframe, DwaineSessionId sessionId, string text)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Sessions.TryGetValue(sessionId, out var session)
            || !TryGetValidSession(session.Terminal, out var linkedMainframe, out _)
            || linkedMainframe != mainframe)
        {
            return false;
        }

        if (!IsSessionStillValid(mainframe, session))
        {
            ForceDisconnect(session.Terminal, DwaineDisconnectReason.TopologyChanged);
            return false;
        }

        session.Output.Add(text);
        _hardware.UpdateUi(session.Terminal);
        return true;
    }

    public bool TryReadInput(EntityUid mainframe, DwaineSessionId sessionId, out string text)
    {
        text = string.Empty;
        if (!TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        if (!IsSessionStillValid(mainframe, session))
        {
            ForceDisconnect(session.Terminal, DwaineDisconnectReason.TopologyChanged);
            return false;
        }

        return session.PendingInput.TryDequeue(out text);
    }

    public int WriteOutputToAll(EntityUid mainframe, string text)
    {
        if (!TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime))
            return 0;

        var written = 0;
        foreach (var sessionId in runtime.Sessions.Keys.ToArray())
        {
            if (WriteOutput(mainframe, sessionId, text))
                written++;
        }

        return written;
    }

    public int GetSessionCount(EntityUid mainframe)
    {
        return TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime)
            ? runtime.Sessions.Count
            : 0;
    }

    public DwaineSessionId? GetTerminalSession(EntityUid terminal)
    {
        return TryComp<DwaineTerminalLinkComponent>(terminal, out var link) ? link.Session : null;
    }

    public void ValidateAllSessions()
    {
        foreach (var mainframe in _mainframes.ToArray())
        {
            if (TerminatingOrDeleted(mainframe)
                || !TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime))
            {
                RemoveMainframeIndex(mainframe);
                continue;
            }

            ReindexMainframe(mainframe, runtime);
            foreach (var session in runtime.Sessions.Values.ToArray())
            {
                if (!IsSessionStillValid(mainframe, session))
                    ForceDisconnect(session.Terminal, DwaineDisconnectReason.TopologyChanged);
            }
        }
    }

    private DwaineMainframeUiEntry[] GetAvailableMainframes(EntityUid terminal)
    {
        if (_hardware.GetStatus(terminal) != DwaineHardwareStatus.HardwareReady
            || !TryComp<DwaineNetworkConnectorComponent>(terminal, out var network)
            || !network.Enabled
            || !_mainframesByNetwork.TryGetValue(network.NetworkId, out var candidates))
        {
            return [];
        }

        var available = new List<(EntityUid Uid, string Name)>();
        foreach (var candidate in candidates)
        {
            if (_hardware.GetStatus(candidate) == DwaineHardwareStatus.HardwareReady
                && TryValidateTopology(terminal, candidate, out _))
            {
                available.Add((candidate, Name(candidate)));
            }
        }

        available.Sort((left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            return byName != 0 ? byName : left.Uid.Id.CompareTo(right.Uid.Id);
        });
        return available
            .Select(entry => new DwaineMainframeUiEntry(GetNetEntity(entry.Uid), entry.Name))
            .ToArray();
    }

    private bool TryGetValidSession(
        EntityUid terminal,
        out EntityUid mainframe,
        out DwaineTerminalSession session)
    {
        mainframe = EntityUid.Invalid;
        session = default!;
        if (!TryComp<DwaineTerminalLinkComponent>(terminal, out var link)
            || link.Mainframe is not { } linkedMainframe
            || link.Session is not { } sessionId
            || TerminatingOrDeleted(linkedMainframe)
            || !TryComp<DwaineMainframeRuntimeComponent>(linkedMainframe, out var runtime)
            || !runtime.Sessions.TryGetValue(sessionId, out var found)
            || found.Terminal != terminal
            || found.Owner != link.SessionOwner)
        {
            return false;
        }

        mainframe = linkedMainframe;
        session = found;
        return true;
    }

    private bool IsSessionStillValid(EntityUid mainframe, DwaineTerminalSession session)
    {
        return !TerminatingOrDeleted(session.Terminal)
               && !TerminatingOrDeleted(session.Owner)
               && _hardware.GetStatus(session.Terminal) == DwaineHardwareStatus.HardwareReady
               && _hardware.GetStatus(mainframe) == DwaineHardwareStatus.HardwareReady
               && TryComp<DwaineTerminalLinkComponent>(session.Terminal, out var link)
               && link.Mainframe == mainframe
               && link.Session == session.Id
               && link.SessionOwner == session.Owner
               && TryValidateTopology(session.Terminal, mainframe, out _);
    }

    private bool TryValidateTopology(
        EntityUid terminal,
        EntityUid mainframe,
        out DwaineConnectResult result)
    {
        result = DwaineConnectResult.TopologyMismatch;
        if (!TryComp<DwaineNetworkConnectorComponent>(terminal, out var terminalNetwork)
            || !TryComp<DwaineNetworkConnectorComponent>(mainframe, out var mainframeNetwork)
            || !terminalNetwork.Enabled
            || !mainframeNetwork.Enabled
            || string.IsNullOrWhiteSpace(terminalNetwork.NetworkId)
            || terminalNetwork.NetworkId.Length > DwaineNetworkConnectorComponent.HardMaxNetworkIdLength
            || string.IsNullOrWhiteSpace(mainframeNetwork.NetworkId)
            || mainframeNetwork.NetworkId.Length > DwaineNetworkConnectorComponent.HardMaxNetworkIdLength
            || !string.Equals(terminalNetwork.NetworkId, mainframeNetwork.NetworkId, StringComparison.Ordinal))
        {
            return false;
        }

        var range = Math.Min(terminalNetwork.LinkRange, mainframeNetwork.LinkRange);
        if (!float.IsFinite(range)
            || range <= 0f
            || !_transform.GetMapCoordinates(terminal)
                .InRange(_transform.GetMapCoordinates(mainframe), range))
        {
            result = DwaineConnectResult.OutOfRange;
            return false;
        }

        result = DwaineConnectResult.Connected;
        return true;
    }

    private void ForceDisconnect(EntityUid terminal, DwaineDisconnectReason reason)
    {
        if (!TryComp<DwaineTerminalLinkComponent>(terminal, out var link))
            return;

        if (link is { Mainframe: { } mainframe, Session: { } session })
            RemoveSessionFromMainframe(mainframe, session, terminal, reason);

        ClearLink(link, reason);
        if (!TerminatingOrDeleted(terminal))
            _hardware.UpdateUi(terminal);
    }

    private void DisconnectAll(EntityUid mainframe, DwaineDisconnectReason reason)
    {
        if (TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime))
            DisconnectAll(mainframe, runtime, reason);
    }

    private void DisconnectAll(
        EntityUid mainframe,
        DwaineMainframeRuntimeComponent runtime,
        DwaineDisconnectReason reason)
    {
        foreach (var session in runtime.Sessions.Values.ToArray())
        {
            if (TryComp<DwaineTerminalLinkComponent>(session.Terminal, out var link)
                && link.Mainframe == mainframe
                && link.Session == session.Id)
            {
                ClearLink(link, reason);
                if (!TerminatingOrDeleted(session.Terminal))
                    _hardware.UpdateUi(session.Terminal);
            }

            var disconnected = new DwaineMainframeSessionDisconnectedEvent(
                session.Id,
                session.Terminal,
                session.Owner,
                reason);
            RaiseLocalEvent(mainframe, ref disconnected);
        }

        runtime.Sessions.Clear();
        runtime.TerminalSessions.Clear();
    }

    private void RemoveSessionFromMainframe(
        EntityUid mainframe,
        DwaineSessionId session,
        EntityUid terminal,
        DwaineDisconnectReason reason)
    {
        if (!TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime))
            return;

        if (runtime.Sessions.Remove(session, out var removed))
        {
            var disconnected = new DwaineMainframeSessionDisconnectedEvent(
                removed.Id,
                removed.Terminal,
                removed.Owner,
                reason);
            RaiseLocalEvent(mainframe, ref disconnected);
        }
        if (runtime.TerminalSessions.TryGetValue(terminal, out var indexed) && indexed == session)
            runtime.TerminalSessions.Remove(terminal);
    }

    private void ReindexMainframe(EntityUid mainframe, DwaineMainframeRuntimeComponent runtime)
    {
        var current = TryComp<DwaineNetworkConnectorComponent>(mainframe, out var connector)
                      && connector.Enabled
                      && !string.IsNullOrWhiteSpace(connector.NetworkId)
                      && connector.NetworkId.Length <= DwaineNetworkConnectorComponent.HardMaxNetworkIdLength
            ? connector.NetworkId
            : string.Empty;
        if (string.Equals(current, runtime.IndexedNetworkId, StringComparison.Ordinal))
            return;

        if (!string.IsNullOrEmpty(runtime.IndexedNetworkId)
            && _mainframesByNetwork.TryGetValue(runtime.IndexedNetworkId, out var oldSet))
        {
            oldSet.Remove(mainframe);
            if (oldSet.Count == 0)
                _mainframesByNetwork.Remove(runtime.IndexedNetworkId);
        }

        runtime.IndexedNetworkId = current;
        if (string.IsNullOrEmpty(current))
            return;

        if (!_mainframesByNetwork.TryGetValue(current, out var set))
        {
            set = new HashSet<EntityUid>();
            _mainframesByNetwork.Add(current, set);
        }

        set.Add(mainframe);
    }

    private void RemoveMainframeIndex(EntityUid mainframe)
    {
        _mainframes.Remove(mainframe);
        foreach (var (network, set) in _mainframesByNetwork.ToArray())
        {
            set.Remove(mainframe);
            if (set.Count == 0)
                _mainframesByNetwork.Remove(network);
        }
    }

    private DwaineSessionId AllocateSessionId()
    {
        if (_nextSessionId == 0)
            _nextSessionId = 1;

        return new DwaineSessionId(_nextSessionId++);
    }

    private static void AssignLink(
        DwaineTerminalLinkComponent link,
        EntityUid mainframe,
        EntityUid owner,
        DwaineSessionId session)
    {
        link.Mainframe = mainframe;
        link.SessionOwner = owner;
        link.Session = session;
        link.PresentationStatus = DwaineTerminalConnectionStatus.Connected;
    }

    private static void ClearLink(DwaineTerminalLinkComponent link, DwaineDisconnectReason reason)
    {
        link.Mainframe = null;
        link.SessionOwner = null;
        link.Session = null;
        link.PresentationStatus = reason == DwaineDisconnectReason.Requested
            ? DwaineTerminalConnectionStatus.Disconnected
            : DwaineTerminalConnectionStatus.MainframeUnavailable;
    }
}
