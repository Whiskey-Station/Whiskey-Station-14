// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.NanoXp;
using Content.Shared.PDA;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.NanoXp;

/// <summary>
/// Hosts the station-local NanoNet, private per-actor UI state, PDA enrollment and department authorization.
/// It never connects to the host internet and never accepts identity or access claims from the client.
/// </summary>
public sealed partial class NanoXpSystem : EntitySystem
{
    private const string ClientUiType = "NanoXpBoundUserInterface";
    private const int MaxTrackedLoginActors = 512;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan MailInterval = TimeSpan.FromSeconds(1);

    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoXpDeviceComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<NanoXpDeviceComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<NanoXpDeviceComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<NanoXpDeviceComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<NanoXpDeviceComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpEnrollMessage>(OnEnroll);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpLoginMessage>(OnLogin);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpLogoutMessage>(OnLogout);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpSendMailMessage>(OnSendMail);
        SubscribeLocalEvent<NanoXpDeviceComponent, NanoXpLaunchDwaineMessage>(OnLaunchDwaine);
    }

    private void OnComponentInit(Entity<NanoXpDeviceComponent> ent, ref ComponentInit args)
    {
        EnsureComp<NanoXpDeviceRuntimeComponent>(ent);
        _ui.SetUi(ent.Owner, NanoXpUiKey.Key, new InterfaceData(ClientUiType));
    }

    private void OnComponentShutdown(Entity<NanoXpDeviceComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<NanoXpDeviceRuntimeComponent>(ent, out var runtime)
            || !TryGetNetwork(ent.Owner, out _, out var network))
        {
            return;
        }

        foreach (var session in runtime.Sessions.Values)
            network.Store.Disconnect(session);
        runtime.Sessions.Clear();
        runtime.LastMailAt.Clear();
    }

    private void OnGetVerbs(Entity<NanoXpDeviceComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("nano-xp-open-verb"),
            Act = () => _ui.TryOpenUi(ent.Owner, NanoXpUiKey.Key, user),
        });
    }

    private void OnUiOpened(Entity<NanoXpDeviceComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, NanoXpUiKey.Key))
            return;

        SendState(ent, args.Actor, NanoXpNotice.None);
    }

    private void OnUiClosed(Entity<NanoXpDeviceComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, NanoXpUiKey.Key))
            return;

        Disconnect(ent.Owner, args.Actor);
    }

    private void OnRefresh(Entity<NanoXpDeviceComponent> ent, ref NanoXpRefreshMessage args)
    {
        if (IsAuthorizedUiActor(ent.Owner, args))
            SendState(ent, args.Actor, NanoXpNotice.None);
    }

    private void OnEnroll(Entity<NanoXpDeviceComponent> ent, ref NanoXpEnrollMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args))
            return;
        if (!IsOnline(ent))
        {
            SendState(ent, args.Actor, NanoXpNotice.Offline);
            return;
        }
        if (ent.Comp.Kind != NanoXpDeviceKind.Pda
            || !TryGetPdaIdentity(ent.Owner, out var identity))
        {
            SendState(ent, args.Actor, NanoXpNotice.IdentityRequired);
            return;
        }
        if (!TryGetNetwork(ent.Owner, out _, out var network))
        {
            SendState(ent, args.Actor, NanoXpNotice.Offline);
            return;
        }

        var result = network.Store.TryEnroll(
            identity.IdentityKey,
            identity.DisplayName,
            identity.JobTitle,
            identity.Department,
            identity.AccessTags,
            args.Password,
            out var account);
        if (result is not (NanoXpAccountResult.Success or NanoXpAccountResult.AlreadyExists))
        {
            SendState(ent, args.Actor, MapNotice(result));
            return;
        }

        TryLogin(ent, args.Actor, account.Address, args.Password, NanoXpNotice.Enrolled);
    }

    private void OnLogin(Entity<NanoXpDeviceComponent> ent, ref NanoXpLoginMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args))
            return;

        TryLogin(ent, args.Actor, args.Address, args.Password, NanoXpNotice.LoggedIn);
    }

    private void TryLogin(
        Entity<NanoXpDeviceComponent> ent,
        EntityUid actor,
        string address,
        string password,
        NanoXpNotice successNotice)
    {
        if (!IsOnline(ent) || !TryGetNetwork(ent.Owner, out _, out var network))
        {
            SendState(ent, actor, NanoXpNotice.Offline);
            return;
        }
        if (IsLoginThrottled(network, actor))
        {
            SendState(ent, actor, NanoXpNotice.RateLimited);
            return;
        }

        var runtime = EnsureComp<NanoXpDeviceRuntimeComponent>(ent);
        Disconnect(ent.Owner, actor);
        var terminal = AllocateTerminal(network);
        var result = network.Store.TryLogin(
            address,
            password,
            terminal,
            _timing.CurTime,
            SessionLifetime,
            out var session);
        if (result != NanoXpAccountResult.Success
            || !network.Store.TryGetLiveSession(session, _timing.CurTime, out var account))
        {
            RegisterLoginFailure(network, actor);
            SendState(ent, actor, MapNotice(result));
            return;
        }

        if (!IsAccountAllowed(ent, account))
        {
            network.Store.Disconnect(session);
            RegisterLoginFailure(network, actor);
            SendState(ent, actor, NanoXpNotice.AccessDenied);
            return;
        }

        if (ent.Comp.Kind == NanoXpDeviceKind.Pda
            && TryGetPdaIdentity(ent.Owner, out var identity))
        {
            network.Store.RefreshProfile(
                account,
                identity.DisplayName,
                identity.JobTitle,
                identity.Department,
                identity.AccessTags);
        }

        network.LoginThrottle.Remove(actor);
        runtime.Sessions[actor] = session;
        SendState(ent, actor, successNotice);
    }

    private void OnLogout(Entity<NanoXpDeviceComponent> ent, ref NanoXpLogoutMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args))
            return;

        Disconnect(ent.Owner, args.Actor);
        SendState(ent, args.Actor, NanoXpNotice.LoggedOut);
    }

    private void OnSendMail(Entity<NanoXpDeviceComponent> ent, ref NanoXpSendMailMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args)
            || !TryGetAuthenticated(ent, args.Actor, out var network, out var runtime, out var session, out var account))
        {
            return;
        }
        if (runtime.LastMailAt.TryGetValue(args.Actor, out var lastMail)
            && _timing.CurTime - lastMail < MailInterval)
        {
            SendState(ent, args.Actor, NanoXpNotice.RateLimited);
            return;
        }

        var result = network.Store.TrySendMail(
            account.Principal,
            args.Recipient,
            args.Subject,
            args.Body,
            (long) _timing.CurTime.TotalSeconds);
        if (result == NanoXpAccountResult.Success)
            runtime.LastMailAt[args.Actor] = _timing.CurTime;
        SendState(ent, args.Actor, result == NanoXpAccountResult.Success ? NanoXpNotice.MailSent : MapNotice(result));
    }

    private void OnLaunchDwaine(Entity<NanoXpDeviceComponent> ent, ref NanoXpLaunchDwaineMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args)
            || !TryGetAuthenticated(ent, args.Actor, out _, out _, out _, out _)
            || !HasComp<DwaineTerminalComponent>(ent)
            || !_ui.HasUi(ent.Owner, DwaineTerminalUiKey.Key))
        {
            return;
        }

        _ui.TryOpenUi(ent.Owner, DwaineTerminalUiKey.Key, args.Actor);
    }

    private void SendState(Entity<NanoXpDeviceComponent> ent, EntityUid actor, NanoXpNotice notice)
    {
        var online = IsOnline(ent);
        var deviceName = MetaData(ent).EntityName;
        var networkName = Loc.GetString("nano-xp-network-offline");
        var canEnroll = false;
        var suggestedAddress = string.Empty;
        var authenticated = false;
        var address = string.Empty;
        var displayName = string.Empty;
        var jobTitle = string.Empty;
        var department = string.Empty;
        var departmentAuthorized = false;
        var inbox = Array.Empty<NanoXpMailEntry>();
        var directory = Array.Empty<NanoXpDirectoryEntry>();

        if (TryGetNetwork(ent.Owner, out var networkOwner, out var network))
        {
            networkName = Loc.GetString("nano-xp-network-name", ("station", MetaData(networkOwner).EntityName));

            if (ent.Comp.Kind == NanoXpDeviceKind.Pda
                && TryGetPdaIdentity(ent.Owner, out var identity))
            {
                canEnroll = !network.Store.TryGetByIdentity(identity.IdentityKey, out var enrolled);
                suggestedAddress = canEnroll
                    ? network.Store.SuggestAddress(identity.DisplayName)
                    : enrolled.Address;
            }

            if (online
                && TryGetAuthenticated(ent, actor, out _, out _, out _, out var account))
            {
                authenticated = true;
                address = account.Address;
                displayName = account.DisplayName;
                jobTitle = account.JobTitle;
                department = account.Department;
                departmentAuthorized = IsAccountAllowed(ent, account);
                inbox = network.Store.GetInbox(account.Principal)
                    .Select(mail => new NanoXpMailEntry(mail.Id, mail.Sender, mail.Subject, mail.Body, mail.SentAtSeconds))
                    .ToArray();
                directory = network.Store.GetDirectory()
                    .Take(NanoXpLimits.MaxDirectoryEntries)
                    .Select(entry => new NanoXpDirectoryEntry(entry.Address, entry.DisplayName, entry.Department))
                    .ToArray();
            }
        }

        var state = new NanoXpUserInterfaceState(
            ent.Comp.Kind,
            deviceName,
            networkName,
            online,
            canEnroll,
            suggestedAddress,
            authenticated,
            address,
            displayName,
            jobTitle,
            department,
            departmentAuthorized,
            authenticated && HasComp<DwaineTerminalComponent>(ent) && _ui.HasUi(ent.Owner, DwaineTerminalUiKey.Key),
            notice,
            inbox,
            directory);
        _ui.ServerSendUiMessage(ent.Owner, NanoXpUiKey.Key, new NanoXpStateMessage(state), actor);
    }

    private bool TryGetAuthenticated(
        Entity<NanoXpDeviceComponent> ent,
        EntityUid actor,
        out NanoXpNetworkRuntimeComponent network,
        out NanoXpDeviceRuntimeComponent runtime,
        out NanoXpSessionSnapshot session,
        out NanoXpAccountSnapshot account)
    {
        network = null!;
        runtime = null!;
        session = default;
        account = default;
        if (!TryGetNetwork(ent.Owner, out _, out network))
            return false;
        if (!TryComp<NanoXpDeviceRuntimeComponent>(ent, out var foundRuntime))
            return false;

        runtime = foundRuntime;
        if (!runtime.Sessions.TryGetValue(actor, out session)
            || !network.Store.TryGetLiveSession(session, _timing.CurTime, out account)
            || !IsAccountAllowed(ent, account))
        {
            if (session.Terminal != 0)
                network.Store.Disconnect(session);
            runtime.Sessions.Remove(actor);
            return false;
        }

        return true;
    }

    private bool IsAccountAllowed(Entity<NanoXpDeviceComponent> ent, NanoXpAccountSnapshot account)
    {
        if (ent.Comp.Kind == NanoXpDeviceKind.Pda)
        {
            return TryGetPdaIdentity(ent.Owner, out var identity)
                   && string.Equals(identity.IdentityKey, account.IdentityKey, StringComparison.Ordinal);
        }

        if (!TryComp<AccessReaderComponent>(ent, out var reader))
            return true;

        var accessTags = account.AccessTags
            .Select(tag => new ProtoId<AccessLevelPrototype>(tag))
            .ToHashSet();
        return _access.IsAllowed(accessTags, Array.Empty<StationRecordKey>(), ent.Owner, reader);
    }

    private bool TryGetPdaIdentity(EntityUid device, out NanoXpPdaIdentity identity)
    {
        identity = default;
        if (!TryComp<PdaComponent>(device, out var pda)
            || pda.ContainedId is not { } idUid
            || !TryComp<IdCardComponent>(idUid, out var id))
        {
            return false;
        }

        var fullName = id.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
            return false;

        var departments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var department in id.JobDepartments)
        {
            departments.Add(_prototypes.TryIndex(department, out var prototype)
                ? Loc.GetString(prototype.Name)
                : department.Id);
        }
        identity = new NanoXpPdaIdentity(
            idUid.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fullName.Trim(),
            string.IsNullOrWhiteSpace(id.LocalizedJobTitle) ? Loc.GetString("nano-xp-job-unknown") : id.LocalizedJobTitle,
            departments.Count == 0 ? Loc.GetString("nano-xp-department-public") : string.Join(" / ", departments),
            _access.FindAccessTags(idUid).Select(tag => tag.Id).ToArray());
        return true;
    }

    private bool TryGetNetwork(
        EntityUid device,
        out EntityUid owner,
        out NanoXpNetworkRuntimeComponent network)
    {
        owner = _stations.GetOwningStation(device)
                ?? Transform(device).MapUid
                ?? device;
        if (TerminatingOrDeleted(owner))
        {
            network = null!;
            return false;
        }

        network = EnsureComp<NanoXpNetworkRuntimeComponent>(owner);
        return true;
    }

    private void Disconnect(EntityUid device, EntityUid actor)
    {
        if (!TryComp<NanoXpDeviceRuntimeComponent>(device, out var runtime)
            || !runtime.Sessions.Remove(actor, out var session))
        {
            return;
        }

        runtime.LastMailAt.Remove(actor);
        if (TryGetNetwork(device, out _, out var network))
            network.Store.Disconnect(session);
    }

    private bool IsAuthorizedUiActor(EntityUid device, BoundUserInterfaceMessage message)
        => Equals(message.UiKey, NanoXpUiKey.Key)
           && _ui.IsUiOpen(device, NanoXpUiKey.Key, message.Actor);

    private bool IsOnline(Entity<NanoXpDeviceComponent> ent)
        => ent.Comp.Kind == NanoXpDeviceKind.Pda
           || (TryComp<ApcPowerReceiverComponent>(ent, out var receiver)
               ? receiver.Powered
               : !HasComp<PowerCellDrawComponent>(ent) || _powerCell.HasDrawCharge(ent.Owner));

    private bool IsLoginThrottled(NanoXpNetworkRuntimeComponent network, EntityUid actor)
        => network.LoginThrottle.TryGetValue(actor, out var throttle)
           && _timing.CurTime < throttle.NextAttempt;

    private void RegisterLoginFailure(NanoXpNetworkRuntimeComponent network, EntityUid actor)
    {
        if (network.LoginThrottle.Count >= MaxTrackedLoginActors
            && !network.LoginThrottle.ContainsKey(actor))
        {
            foreach (var expired in network.LoginThrottle
                         .Where(pair => pair.Value.NextAttempt <= _timing.CurTime)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                network.LoginThrottle.Remove(expired);
            }

            if (network.LoginThrottle.Count >= MaxTrackedLoginActors)
            {
                var earliest = network.LoginThrottle.MinBy(pair => pair.Value.NextAttempt).Key;
                network.LoginThrottle.Remove(earliest);
            }
        }

        network.LoginThrottle.TryGetValue(actor, out var current);
        var failures = Math.Min(current.Failures + 1, 5);
        network.LoginThrottle[actor] = new NanoXpLoginThrottle(
            failures,
            _timing.CurTime + TimeSpan.FromSeconds(1 << (failures - 1)));
    }

    private static ulong AllocateTerminal(NanoXpNetworkRuntimeComponent network)
    {
        var terminal = network.NextTerminal++;
        if (network.NextTerminal == 0)
            network.NextTerminal = 1;
        return terminal == 0 ? network.NextTerminal++ : terminal;
    }

    private static NanoXpNotice MapNotice(NanoXpAccountResult result)
        => result switch
        {
            NanoXpAccountResult.UnknownRecipient => NanoXpNotice.UnknownRecipient,
            NanoXpAccountResult.InvalidMail => NanoXpNotice.InvalidMail,
            NanoXpAccountResult.MailboxFull => NanoXpNotice.MailboxFull,
            NanoXpAccountResult.InvalidIdentity => NanoXpNotice.IdentityRequired,
            _ => NanoXpNotice.InvalidCredential,
        };

    private readonly record struct NanoXpPdaIdentity(
        string IdentityKey,
        string DisplayName,
        string JobTitle,
        string Department,
        IReadOnlyList<string> AccessTags);
}
