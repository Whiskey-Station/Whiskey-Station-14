// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Network;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Network;

/// <summary>
/// Event-indexed DWAINE topology and bounded packet router. Nodes enter only through connector
/// lifecycle events or an explicitly supplied endpoint; routing never enumerates map entities.
/// </summary>
public sealed partial class DwaineNetworkSystem : EntitySystem
{
    [Dependency] private DwaineHardwareSystem _hardware = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, DwaineNetworkNode> _nodes = [];
    private readonly Dictionary<(string Network, string Address), EntityUid> _addresses = [];
    private readonly Dictionary<(string Network, string Tag), HashSet<EntityUid>> _tags = [];
    private readonly HashSet<EntityUid> _jammers = [];
    private readonly Dictionary<DwaineNetworkCorrelationId, DwainePendingNetworkRequest> _pending = [];
    private readonly Dictionary<EntityUid, TimeSpan> _nextDiscovery = [];
    private ulong _nextGeneratedAddress = 1;
    private ulong _nextCorrelation = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineNetworkConnectorComponent, MapInitEvent>(OnConnectorMapInit);
        SubscribeLocalEvent<DwaineNetworkConnectorComponent, ComponentShutdown>(OnConnectorShutdown);
        SubscribeLocalEvent<DwaineNetworkJammerComponent, MapInitEvent>(OnJammerMapInit);
        SubscribeLocalEvent<DwaineNetworkJammerComponent, ComponentShutdown>(OnJammerShutdown);
        SubscribeLocalEvent<DwaineNetworkBootClientComponent, DwaineBootRecoveryRequestedEvent>(OnBootRecovery);
        SubscribeLocalEvent<DwaineNetworkBootProviderComponent, DwaineNetworkPacketReceivedEvent>(OnBootPacket);
    }

    public DwaineNetworkResult GetNode(EntityUid entity, out DwaineNetworkNodeSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetNode(entity, out var node, out var connector))
            return DwaineNetworkResult.InvalidNode;
        if (node.Conflict)
            return DwaineNetworkResult.DuplicateAddress;

        snapshot = Snapshot(node, IsOnline(entity, connector));
        return snapshot.Online ? DwaineNetworkResult.Success : DwaineNetworkResult.Disabled;
    }

    public DwaineNetworkResult CanReach(EntityUid source, EntityUid destination)
    {
        if (!TryGetUsableNode(source, out var sourceNode, out var sourceConnector, out var sourceFailure))
            return sourceFailure;
        if (!TryGetUsableNode(destination, out var destinationNode, out var destinationConnector, out var destinationFailure))
            return destinationFailure;
        return CanReach(sourceNode, sourceConnector, destinationNode, destinationConnector);
    }

    public DwaineNetworkResult Discover(
        EntityUid source,
        string? tag,
        out DwaineNetworkNodeSnapshot[] results)
    {
        results = [];
        if (!TryGetUsableNode(source, out var sourceNode, out var sourceConnector, out var failure))
            return failure;
        var limits = GetLimits(source);
        if (_nextDiscovery.TryGetValue(source, out var next) && _timing.CurTime < next)
            return DwaineNetworkResult.RateLimited;
        _nextDiscovery[source] = _timing.CurTime + limits.DiscoveryCooldown;
        SaturatingIncrement(ref sourceNode.Discoveries);

        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? string.Empty : NormalizeIdentifier(tag);
        if (tag is not null && normalizedTag.Length == 0)
            return DwaineNetworkResult.InvalidAddress;

        IEnumerable<EntityUid> candidates;
        if (normalizedTag.Length > 0)
        {
            candidates = _tags.TryGetValue((sourceNode.NetworkId, normalizedTag), out var tagged)
                ? tagged.ToArray()
                : [];
        }
        else
        {
            candidates = _addresses
                .Where(pair => pair.Key.Network == sourceNode.NetworkId)
                .Select(pair => pair.Value)
                .ToArray();
        }

        var visible = new List<DwaineNetworkNodeSnapshot>();
        foreach (var candidate in candidates)
        {
            if (candidate == source || !TryGetNode(candidate, out var node, out var connector))
                continue;
            if (CanReach(sourceNode, sourceConnector, node, connector)
                != DwaineNetworkResult.Success)
            {
                continue;
            }
            visible.Add(Snapshot(node, IsOnline(candidate, connector)));
            if (visible.Count >= limits.MaxDiscoveryResults)
                break;
        }

        results = visible.OrderBy(entry => entry.Address.Value, StringComparer.Ordinal).ToArray();
        return DwaineNetworkResult.Success;
    }

    public DwaineNetworkResult TrySend(
        EntityUid source,
        string destination,
        string protocol,
        string payload)
    {
        var prepare = TryPrepareRoute(source, destination, protocol, payload,
            out var sourceNode, out var destinationNode, out var target, out var limits);
        if (prepare != DwaineNetworkResult.Success)
        {
            RecordFailure(source, destination, protocol, payload.Length, prepare);
            return prepare;
        }

        var delivered = Deliver(sourceNode, destinationNode, target, protocol, payload, default, limits, out var packetEvent);
        if (delivered != DwaineNetworkResult.Success)
            return delivered;
        return packetEvent.Handled ? DwaineNetworkResult.Success : DwaineNetworkResult.Unsupported;
    }

    public DwaineNetworkResult TryRequest(
        EntityUid source,
        string destination,
        string protocol,
        string payload,
        out DwaineNetworkCorrelationId correlation)
    {
        correlation = default;
        var prepare = TryPrepareRoute(source, destination, protocol, payload,
            out var sourceNode, out var destinationNode, out var target, out var limits);
        if (prepare != DwaineNetworkResult.Success)
        {
            RecordFailure(source, destination, protocol, payload.Length, prepare);
            return prepare;
        }

        PruneCompletedFor(source);
        if (_pending.Values.Count(request => request.Source == source) >= limits.MaxPendingRequests
            || !TryAllocateCorrelation(out correlation))
        {
            RecordFailure(source, destination, protocol, payload.Length, DwaineNetworkResult.CapacityReached);
            return DwaineNetworkResult.CapacityReached;
        }

        var pending = new DwainePendingNetworkRequest
        {
            Correlation = correlation,
            Source = source,
            Destination = target,
            ExpiresAt = _timing.CurTime + limits.RequestTimeout,
        };
        _pending.Add(correlation, pending);
        SaturatingIncrement(ref sourceNode.Requests);

        var delivered = Deliver(sourceNode, destinationNode, target, protocol, payload, correlation, limits, out var packetEvent);
        if (delivered != DwaineNetworkResult.Success)
        {
            _pending.Remove(correlation);
            return delivered;
        }
        if (!packetEvent.Handled)
            return DwaineNetworkResult.Pending;

        pending.Result = DwaineNetworkResult.Success;
        pending.Reply = packetEvent.Reply ?? string.Empty;
        SaturatingIncrement(ref sourceNode.Replies);
        return DwaineNetworkResult.Success;
    }

    public DwaineNetworkResult TryTakeReply(
        EntityUid source,
        DwaineNetworkCorrelationId correlation,
        out string reply)
    {
        reply = string.Empty;
        if (!correlation.IsValid
            || !_pending.TryGetValue(correlation, out var pending)
            || pending.Source != source)
        {
            return DwaineNetworkResult.NotFound;
        }

        if (pending.Result != DwaineNetworkResult.Pending)
        {
            _pending.Remove(correlation);
            reply = pending.Reply;
            return pending.Result;
        }
        if (_timing.CurTime >= pending.ExpiresAt)
        {
            _pending.Remove(correlation);
            RecordFailure(source, string.Empty, "timeout", 0, DwaineNetworkResult.Timeout);
            return DwaineNetworkResult.Timeout;
        }
        var reachability = CanReach(pending.Source, pending.Destination);
        if (reachability != DwaineNetworkResult.Success)
        {
            _pending.Remove(correlation);
            return DwaineNetworkResult.Disconnected;
        }
        return DwaineNetworkResult.Pending;
    }

    public DwaineNetworkMetricsSnapshot GetMetrics(EntityUid source)
    {
        if (!TryGetNode(source, out var node, out _))
            return default;
        return new DwaineNetworkMetricsSnapshot(
            node.Sent,
            node.Delivered,
            node.Dropped,
            node.Discoveries,
            node.Requests,
            node.Replies,
            _pending.Values.Count(request => request.Source == source),
            node.Capture.Count);
    }

    public DwaineNetworkCaptureEntry[] GetCapture(EntityUid source)
    {
        return TryGetNode(source, out var node, out _)
            ? node.Capture.ToArray()
            : [];
    }

    private void OnConnectorMapInit(Entity<DwaineNetworkConnectorComponent> ent, ref MapInitEvent args)
        => Register(ent.Owner);

    private void OnConnectorShutdown(Entity<DwaineNetworkConnectorComponent> ent, ref ComponentShutdown args)
        => RemoveNode(ent.Owner);

    private void OnJammerMapInit(Entity<DwaineNetworkJammerComponent> ent, ref MapInitEvent args)
        => _jammers.Add(ent.Owner);

    private void OnJammerShutdown(Entity<DwaineNetworkJammerComponent> ent, ref ComponentShutdown args)
        => _jammers.Remove(ent.Owner);

    private void OnBootRecovery(
        Entity<DwaineNetworkBootClientComponent> ent,
        ref DwaineBootRecoveryRequestedEvent args)
    {
        if (!ent.Comp.Enabled
            || NormalizeIdentifier(ent.Comp.ProviderAddress).Length == 0
            || NormalizeIdentifier(ent.Comp.RecoveryProfile).Length == 0)
        {
            return;
        }
        var request = TryRequest(
            ent.Owner,
            ent.Comp.ProviderAddress,
            "dwaine.netboot",
            NormalizeIdentifier(ent.Comp.RecoveryProfile),
            out var correlation);
        if (request is not (DwaineNetworkResult.Success or DwaineNetworkResult.Pending))
            return;
        var reply = TryTakeReply(ent.Owner, correlation, out var profile);
        args.Recovered = reply == DwaineNetworkResult.Success
                         && string.Equals(
                             NormalizeIdentifier(profile),
                             NormalizeIdentifier(ent.Comp.RecoveryProfile),
                             StringComparison.Ordinal);
    }

    private void OnBootPacket(
        Entity<DwaineNetworkBootProviderComponent> ent,
        ref DwaineNetworkPacketReceivedEvent args)
    {
        if (!string.Equals(args.Packet.Protocol, "dwaine.netboot", StringComparison.Ordinal))
            return;
        args.Handled = true;
        if (!ent.Comp.Enabled || args.Packet.Correlation is not { IsValid: true })
        {
            args.Reply = "offline";
            return;
        }
        var profile = NormalizeIdentifier(ent.Comp.RecoveryProfile);
        args.Reply = profile.Length > 0 && string.Equals(args.Packet.Payload, profile, StringComparison.Ordinal)
            ? profile
            : "denied";
    }

    private DwaineNetworkResult TryPrepareRoute(
        EntityUid source,
        string destination,
        string protocol,
        string payload,
        out DwaineNetworkNode sourceNode,
        out DwaineNetworkNode destinationNode,
        out EntityUid target,
        out DwaineNetworkLimits limits)
    {
        sourceNode = null!;
        destinationNode = null!;
        target = EntityUid.Invalid;
        limits = default;
        if (!TryGetUsableNode(source, out sourceNode, out var sourceConnector, out var failure))
            return failure;
        var address = NormalizeIdentifier(destination);
        if (address.Length == 0)
            return DwaineNetworkResult.InvalidAddress;
        if (!_addresses.TryGetValue((sourceNode.NetworkId, address), out target)
            || !TryGetUsableNode(target, out destinationNode, out var destinationConnector, out failure))
        {
            var sourceNetwork = sourceNode.NetworkId;
            return _nodes.Values.Any(node => node.Address == address && node.NetworkId != sourceNetwork)
                ? DwaineNetworkResult.CrossNetwork
                : DwaineNetworkResult.NotFound;
        }
        if (destinationNode.NetworkId != sourceNode.NetworkId || destinationNode.Address != address)
            return DwaineNetworkResult.NotFound;
        if (!IsProtocol(protocol) || payload.IndexOf('\0') >= 0)
            return DwaineNetworkResult.InvalidPayload;

        var reachability = CanReach(sourceNode, sourceConnector, destinationNode, destinationConnector);
        if (reachability != DwaineNetworkResult.Success)
            return reachability;
        if (!TryComp<DwaineNetworkEndpointComponent>(source, out var sourceEndpoint)
            || !sourceEndpoint.Enabled
            || !TryComp<DwaineNetworkEndpointComponent>(target, out var destinationEndpoint)
            || !destinationEndpoint.Enabled)
        {
            return DwaineNetworkResult.Unsupported;
        }

        var sourceLimits = GetLimits(sourceEndpoint);
        var destinationLimits = GetLimits(destinationEndpoint);
        limits = new DwaineNetworkLimits(
            Math.Min(sourceLimits.MaxPayloadCharacters, destinationLimits.MaxPayloadCharacters),
            sourceLimits.MaxPendingRequests,
            sourceLimits.MaxDiscoveryResults,
            sourceLimits.MaxCaptureEntries,
            sourceLimits.DiscoveryCooldown,
            sourceLimits.RequestTimeout);
        return payload.Length <= limits.MaxPayloadCharacters
            ? DwaineNetworkResult.Success
            : DwaineNetworkResult.PayloadTooLarge;
    }

    private DwaineNetworkResult Deliver(
        DwaineNetworkNode source,
        DwaineNetworkNode destination,
        EntityUid target,
        string protocol,
        string payload,
        DwaineNetworkCorrelationId correlation,
        DwaineNetworkLimits limits,
        out DwaineNetworkPacketReceivedEvent packetEvent)
    {
        SaturatingIncrement(ref source.Sent);
        packetEvent = new DwaineNetworkPacketReceivedEvent(new DwaineNetworkPacket(
            source.Entity,
            new DwaineNetworkAddress(source.Address),
            new DwaineNetworkAddress(destination.Address),
            protocol.ToLowerInvariant(),
            payload,
            correlation));
        RaiseLocalEvent(target, ref packetEvent);
        if (!packetEvent.Handled && string.Equals(protocol, "dwaine.ping", StringComparison.OrdinalIgnoreCase))
        {
            packetEvent.Handled = true;
            packetEvent.Reply = "pong";
        }
        if (packetEvent.Reply is { } reply && reply.Length > limits.MaxPayloadCharacters)
        {
            packetEvent.Handled = false;
            packetEvent.Reply = null;
            Record(source, destination.Address, protocol, payload.Length, DwaineNetworkResult.PayloadTooLarge);
            return DwaineNetworkResult.PayloadTooLarge;
        }

        SaturatingIncrement(ref source.Delivered);
        Record(source, destination.Address, protocol, payload.Length,
            packetEvent.Handled ? DwaineNetworkResult.Success : DwaineNetworkResult.Unsupported);
        return DwaineNetworkResult.Success;
    }

    private DwaineNetworkResult CanReach(
        DwaineNetworkNode source,
        DwaineNetworkConnectorComponent sourceConnector,
        DwaineNetworkNode destination,
        DwaineNetworkConnectorComponent destinationConnector)
    {
        if (source.NetworkId != destination.NetworkId)
            return DwaineNetworkResult.CrossNetwork;

        var wired = source.Adapter.HasFlag(DwaineNetworkAdapter.Wired)
                    && destination.Adapter.HasFlag(DwaineNetworkAdapter.Wired);
        if (wired)
            return DwaineNetworkResult.Success;

        var radio = source.Adapter.HasFlag(DwaineNetworkAdapter.Radio)
                    && destination.Adapter.HasFlag(DwaineNetworkAdapter.Radio);
        if (!radio || source.Frequency != destination.Frequency || source.Channel != destination.Channel)
            return DwaineNetworkResult.AdapterMismatch;

        var range = Math.Min(sourceConnector.LinkRange, destinationConnector.LinkRange);
        if (!float.IsFinite(range) || range <= 0f || range > DwaineNetworkConnectorComponent.HardMaxLinkRange)
            return DwaineNetworkResult.OutOfRange;
        if (!_transform.GetMapCoordinates(source.Entity)
                .InRange(_transform.GetMapCoordinates(destination.Entity), range))
        {
            return DwaineNetworkResult.OutOfRange;
        }
        return IsJammed(source, destination) ? DwaineNetworkResult.Interfered : DwaineNetworkResult.Success;
    }

    private bool IsJammed(DwaineNetworkNode source, DwaineNetworkNode destination)
    {
        foreach (var jammer in _jammers.ToArray())
        {
            if (TerminatingOrDeleted(jammer)
                || !TryComp<DwaineNetworkJammerComponent>(jammer, out var config))
            {
                _jammers.Remove(jammer);
                continue;
            }
            if (!config.Enabled
                || NormalizeIdentifier(config.NetworkId) != source.NetworkId
                || config.Frequency != source.Frequency
                || NormalizeIdentifier(config.Channel) != source.Channel
                || !float.IsFinite(config.Range)
                || config.Range <= 0f
                || config.Range > DwaineNetworkConnectorComponent.HardMaxLinkRange)
            {
                continue;
            }
            var coordinates = _transform.GetMapCoordinates(jammer);
            if (coordinates.InRange(_transform.GetMapCoordinates(source.Entity), config.Range)
                || coordinates.InRange(_transform.GetMapCoordinates(destination.Entity), config.Range))
            {
                return true;
            }
        }
        return false;
    }

    private bool TryGetUsableNode(
        EntityUid entity,
        out DwaineNetworkNode node,
        out DwaineNetworkConnectorComponent connector,
        out DwaineNetworkResult failure)
    {
        failure = DwaineNetworkResult.InvalidNode;
        if (!TryGetNode(entity, out node, out connector))
            return false;
        if (node.Conflict)
        {
            failure = DwaineNetworkResult.DuplicateAddress;
            return false;
        }
        if (!IsOnline(entity, connector))
        {
            failure = DwaineNetworkResult.Disabled;
            return false;
        }
        failure = DwaineNetworkResult.Success;
        return true;
    }

    private bool TryGetNode(
        EntityUid entity,
        out DwaineNetworkNode node,
        out DwaineNetworkConnectorComponent connector)
    {
        node = null!;
        connector = null!;
        if (TerminatingOrDeleted(entity)
            || !TryComp<DwaineNetworkConnectorComponent>(entity, out var foundConnector))
        {
            RemoveNode(entity);
            return false;
        }
        connector = foundConnector;
        node = _nodes.TryGetValue(entity, out var foundNode)
            ? foundNode
            : Register(entity);
        Refresh(node, connector);
        return node.NetworkId.Length > 0 && node.Address.Length > 0;
    }

    private DwaineNetworkNode Register(EntityUid entity)
    {
        if (_nodes.TryGetValue(entity, out var existing))
            return existing;
        var generated = $"node-{_nextGeneratedAddress++}";
        if (_nextGeneratedAddress == 0)
            _nextGeneratedAddress = 1;
        var node = new DwaineNetworkNode
        {
            Entity = entity,
            GeneratedAddress = generated,
        };
        _nodes.Add(entity, node);
        if (TryComp<DwaineNetworkConnectorComponent>(entity, out var connector))
            Refresh(node, connector);
        return node;
    }

    private void Refresh(DwaineNetworkNode node, DwaineNetworkConnectorComponent connector)
    {
        RemoveIndexes(node);
        node.NetworkId = NormalizeIdentifier(connector.NetworkId, DwaineNetworkConnectorComponent.HardMaxNetworkIdLength);
        var requestedAddress = string.IsNullOrWhiteSpace(connector.Address)
            ? node.GeneratedAddress
            : connector.Address;
        node.Address = NormalizeIdentifier(requestedAddress, DwaineNetworkConnectorComponent.HardMaxAddressLength);
        node.Adapter = connector.Adapter & DwaineNetworkAdapter.Omni;
        node.Frequency = connector.Frequency;
        node.Channel = NormalizeIdentifier(connector.Channel, DwaineNetworkConnectorComponent.HardMaxTagLength);
        node.Tags = connector.Tags
            .Take(DwaineNetworkConnectorComponent.HardMaxTagCount)
            .Select(tag => NormalizeIdentifier(tag, DwaineNetworkConnectorComponent.HardMaxTagLength))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        node.Conflict = false;

        if (node.NetworkId.Length == 0 || node.Address.Length == 0 || node.Adapter == DwaineNetworkAdapter.None)
            return;
        var key = (node.NetworkId, node.Address);
        if (_addresses.TryGetValue(key, out var owner) && owner != node.Entity)
        {
            node.Conflict = true;
            return;
        }
        _addresses[key] = node.Entity;
        foreach (var tag in node.Tags)
        {
            var tagKey = (node.NetworkId, tag);
            if (!_tags.TryGetValue(tagKey, out var set))
            {
                set = [];
                _tags.Add(tagKey, set);
            }
            set.Add(node.Entity);
        }
    }

    private void RemoveNode(EntityUid entity)
    {
        if (!_nodes.Remove(entity, out var node))
            return;
        RemoveIndexes(node);
        _nextDiscovery.Remove(entity);
        foreach (var pending in _pending.Values)
        {
            if (pending.Source == entity || pending.Destination == entity)
                pending.Result = DwaineNetworkResult.Disconnected;
        }
    }

    private void RemoveIndexes(DwaineNetworkNode node)
    {
        if (node.NetworkId.Length == 0)
            return;
        var addressKey = (node.NetworkId, node.Address);
        if (_addresses.TryGetValue(addressKey, out var owner) && owner == node.Entity)
            _addresses.Remove(addressKey);
        foreach (var tag in node.Tags)
        {
            var tagKey = (node.NetworkId, tag);
            if (!_tags.TryGetValue(tagKey, out var set))
                continue;
            set.Remove(node.Entity);
            if (set.Count == 0)
                _tags.Remove(tagKey);
        }
    }

    private bool IsOnline(EntityUid entity, DwaineNetworkConnectorComponent connector)
    {
        if (!connector.Enabled
            || (HasComp<DwaineComputerHardwareComponent>(entity)
                && _hardware.GetStatus(entity) != DwaineHardwareStatus.HardwareReady))
        {
            return false;
        }

        if (!connector.Adapter.HasFlag(DwaineNetworkAdapter.Radio))
            return connector.Adapter.HasFlag(DwaineNetworkAdapter.Wired);

        return connector.Frequency is >= DwaineNetworkConnectorComponent.MinimumFrequency
                   and <= DwaineNetworkConnectorComponent.MaximumFrequency
               && NormalizeIdentifier(connector.Channel, DwaineNetworkConnectorComponent.HardMaxTagLength).Length > 0;
    }

    private static DwaineNetworkNodeSnapshot Snapshot(DwaineNetworkNode node, bool online)
        => new(
            new DwaineNetworkAddress(node.Address),
            node.NetworkId,
            node.Adapter,
            node.Frequency,
            node.Channel,
            node.Tags.ToArray(),
            online && !node.Conflict);

    private DwaineNetworkLimits GetLimits(EntityUid entity)
        => TryComp<DwaineNetworkEndpointComponent>(entity, out var endpoint)
            ? GetLimits(endpoint)
            : GetLimits(null);

    private static DwaineNetworkLimits GetLimits(DwaineNetworkEndpointComponent? component)
    {
        var cooldown = component is not null && float.IsFinite(component.DiscoveryCooldownSeconds)
            ? component.DiscoveryCooldownSeconds
            : 1f;
        var timeout = component is not null && float.IsFinite(component.RequestTimeoutSeconds)
            ? component.RequestTimeoutSeconds
            : 3f;
        return new DwaineNetworkLimits(
            Math.Clamp(component?.MaxPayloadCharacters ?? 2048, 1, DwaineNetworkEndpointComponent.HardMaxPayloadCharacters),
            Math.Clamp(component?.MaxPendingRequests ?? 64, 1, DwaineNetworkEndpointComponent.HardMaxPendingRequests),
            Math.Clamp(component?.MaxDiscoveryResults ?? 64, 1, DwaineNetworkEndpointComponent.HardMaxDiscoveryResults),
            Math.Clamp(component?.MaxCaptureEntries ?? 128, 1, DwaineNetworkEndpointComponent.HardMaxCaptureEntries),
            TimeSpan.FromSeconds(Math.Clamp(cooldown, 0.1f, 30f)),
            TimeSpan.FromSeconds(Math.Clamp(timeout, 0.1f, DwaineNetworkEndpointComponent.HardMaxRequestTimeoutSeconds)));
    }

    private bool TryAllocateCorrelation(out DwaineNetworkCorrelationId correlation)
    {
        for (var attempt = 0; attempt <= _pending.Count; attempt++)
        {
            var value = _nextCorrelation++;
            if (_nextCorrelation == 0)
                _nextCorrelation = 1;
            correlation = new DwaineNetworkCorrelationId(value);
            if (correlation.IsValid && !_pending.ContainsKey(correlation))
                return true;
        }
        correlation = default;
        return false;
    }

    private void PruneCompletedFor(EntityUid source)
    {
        foreach (var correlation in _pending
                     .Where(pair => pair.Value.Source == source
                                    && _timing.CurTime >= pair.Value.ExpiresAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _pending.Remove(correlation);
        }
    }

    private void RecordFailure(
        EntityUid source,
        string destination,
        string protocol,
        int payloadCharacters,
        DwaineNetworkResult result)
    {
        if (!TryGetNode(source, out var node, out _))
            return;
        SaturatingIncrement(ref node.Dropped);
        Record(node, destination, protocol, payloadCharacters, result);
    }

    private void Record(
        DwaineNetworkNode source,
        string destination,
        string protocol,
        int payloadCharacters,
        DwaineNetworkResult result)
    {
        var limit = GetLimits(source.Entity).MaxCaptureEntries;
        source.Capture.Enqueue(new DwaineNetworkCaptureEntry(
            _timing.CurTime,
            source.Address,
            destination,
            protocol,
            payloadCharacters,
            result));
        while (source.Capture.Count > limit)
            source.Capture.Dequeue();
    }

    private static string NormalizeIdentifier(string? value, int maximumLength = 64)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            return string.Empty;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value.ToLowerInvariant()
            : string.Empty;
    }

    private static bool IsProtocol(string? value)
        => NormalizeIdentifier(value, 64).Length > 0;

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value < ulong.MaxValue)
            value++;
    }
}
