// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Network;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Storage;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Server.Power.Components;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Storage;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Devices;

/// <summary>
/// Owns the device bus and opaque capability handles. Endpoints enter through an explicit local
/// attachment, validated transport session, inserted media, or the indexed network topology.
/// </summary>
public sealed partial class DwaineDeviceSystem : EntitySystem
{
    [Dependency] private DwaineHardwareSystem _hardware = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineNetworkSystem _network = default!;
    [Dependency] private DwaineProcessSystem _processes = default!;
    [Dependency] private DwaineStorageSystem _storage = default!;
    [Dependency] private DwaineTerminalTransportSystem _transport = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _hostsByDevice = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineDeviceAbiComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineDeviceAbiRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineDeviceAbiRuntimeComponent, DwaineMainframeSessionConnectedEvent>(OnSessionConnected);
        SubscribeLocalEvent<DwaineDeviceAbiRuntimeComponent, DwaineMainframeSessionDisconnectedEvent>(OnSessionDisconnected);
        SubscribeLocalEvent<DwaineDeviceAbiRuntimeComponent, DwaineProcessRemovedEvent>(OnProcessRemoved);
        SubscribeLocalEvent<DwaineDeviceComponent, ComponentShutdown>(OnDeviceShutdown);
    }

    public DwaineDeviceResult TryAttachLocal(EntityUid mainframe, EntityUid device)
    {
        if (!TryGetRuntime(mainframe, out var config, out var runtime))
            return DwaineDeviceResult.MainframeUnavailable;
        if (TerminatingOrDeleted(device)
            || !TryComp<DwaineDeviceComponent>(device, out var deviceConfig)
            || !IsValidDevice(deviceConfig))
        {
            return DwaineDeviceResult.InvalidDevice;
        }
        if (!TryComp<DwaineDeviceBusEndpointComponent>(mainframe, out var hostBus)
            || !hostBus.Enabled
            || !TryComp<DwaineDeviceBusEndpointComponent>(device, out var deviceBus)
            || !deviceBus.Enabled
            || !string.Equals(hostBus.BusId, deviceBus.BusId, StringComparison.Ordinal))
        {
            return DwaineDeviceResult.InvalidDevice;
        }

        return Attach(mainframe, device, deviceConfig, config, runtime, null, false);
    }

    public DwaineDeviceResult TryScan(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        out int visibleCount)
    {
        visibleCount = 0;
        if (!TryGetRuntime(mainframe, out var config, out var runtime))
            return DwaineDeviceResult.MainframeUnavailable;
        if (!ValidateCaller(mainframe, process, principal, out _))
            return DwaineDeviceResult.AccessDenied;

        var limits = DwaineDeviceAbiLimits.FromComponent(config);
        if (runtime.NextScanAt.TryGetValue(process, out var next) && _timing.CurTime < next)
            return DwaineDeviceResult.RateLimited;
        runtime.NextScanAt[process] = _timing.CurTime + limits.ScanCooldown;

        Reconcile(mainframe, config, runtime);
        ReconcileNetwork(mainframe, config, runtime);
        visibleCount = runtime.Endpoints.Values.Count(endpoint => CanAccess(mainframe, process, principal, endpoint));
        return DwaineDeviceResult.Success;
    }

    public DwaineDeviceDescriptor[] ListDevices(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        string? tag = null)
    {
        if (!TryGetRuntime(mainframe, out var config, out var runtime)
            || !ValidateCaller(mainframe, process, principal, out _))
        {
            return [];
        }

        Reconcile(mainframe, config, runtime);
        return runtime.Endpoints.Values
            .Where(endpoint => (string.IsNullOrEmpty(tag)
                                || string.Equals(endpoint.Tag, tag, StringComparison.OrdinalIgnoreCase))
                               && CanAccess(mainframe, process, principal, endpoint))
            .OrderBy(endpoint => endpoint.Address, StringComparer.Ordinal)
            .Select(endpoint => Snapshot(mainframe, endpoint))
            .ToArray();
    }

    public DwaineDeviceResult TryAcquire(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        string address,
        DwaineDeviceCapability requested,
        out DwaineDeviceHandle handle)
    {
        handle = default;
        if (!TryGetRuntime(mainframe, out var config, out var runtime)
            || runtime.Handles is null)
        {
            return DwaineDeviceResult.MainframeUnavailable;
        }
        if (!ValidateCaller(mainframe, process, principal, out _))
            return DwaineDeviceResult.AccessDenied;
        if (!IsValidIdentifier(address))
            return DwaineDeviceResult.InvalidAddress;

        Reconcile(mainframe, config, runtime);
        if (!runtime.ByAddress.TryGetValue(address, out var endpointId)
            || !runtime.Endpoints.TryGetValue(endpointId, out var endpoint))
        {
            return DwaineDeviceResult.NotFound;
        }
        if (!CanAccess(mainframe, process, principal, endpoint))
            return DwaineDeviceResult.AccessDenied;
        if (GetStatus(mainframe, endpoint) != DwaineDeviceStatus.Ready)
            return DwaineDeviceResult.Offline;

        return runtime.Handles.TryIssue(
            endpoint.Id,
            process,
            principal,
            runtime.BootGeneration,
            endpoint.Capabilities,
            requested,
            out handle);
    }

    public DwaineDeviceResponse TryMessage(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        DwaineDeviceHandle handle,
        string command,
        string payload)
    {
        if (!TryGetRuntime(mainframe, out var config, out var runtime) || runtime.Handles is null)
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.MainframeUnavailable);
        if (!ValidateCaller(mainframe, process, principal, out var caller))
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.AccessDenied);
        var limits = DwaineDeviceAbiLimits.FromComponent(config);
        if (!IsValidCommand(command)
            || payload.IndexOf('\0') >= 0
            || payload.Length > limits.MaxMessageCharacters)
        {
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.MalformedMessage);
        }

        var required = string.Equals(command, "status", StringComparison.OrdinalIgnoreCase)
            ? DwaineDeviceCapability.Inspect
            : DwaineDeviceCapability.Message;
        var resolve = runtime.Handles.TryResolve(
            handle,
            process,
            principal,
            runtime.BootGeneration,
            required,
            out var capability);
        if (resolve != DwaineDeviceResult.Success)
            return DwaineDeviceResponse.Failure(resolve);
        if (!runtime.Endpoints.TryGetValue(capability.Endpoint, out var endpoint))
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.StaleHandle);
        if (!CanAccess(mainframe, process, principal, endpoint))
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.AccessDenied);

        var status = GetStatus(mainframe, endpoint);
        if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
            return DwaineDeviceResponse.Success(status.ToString().ToLowerInvariant(), status);
        if (status != DwaineDeviceStatus.Ready)
            return DwaineDeviceResponse.Failure(DwaineDeviceResult.Offline, status);

        if (endpoint.TerminalSession is { } terminalSession)
        {
            if (caller.TerminalSession?.Value != terminalSession.Value
                || !capability.Capabilities.HasFlag(DwaineDeviceCapability.TerminalOutput)
                || !string.Equals(command, "write", StringComparison.OrdinalIgnoreCase))
            {
                return DwaineDeviceResponse.Failure(DwaineDeviceResult.AccessDenied);
            }
            return _transport.WriteOutput(mainframe, terminalSession, payload)
                ? DwaineDeviceResponse.Success()
                : DwaineDeviceResponse.Failure(DwaineDeviceResult.Offline);
        }

        var message = new DwaineDeviceMessageEvent(
            new DwaineDeviceDriverContext(mainframe, process, principal, handle, capability.Capabilities),
            command,
            payload,
            DwaineDeviceResponse.Failure(DwaineDeviceResult.Unsupported));
        RaiseLocalEvent(endpoint.Entity, ref message);
        return message.Handled ? message.Response : DwaineDeviceResponse.Failure(DwaineDeviceResult.Unsupported);
    }

    public DwaineDeviceResult TryResolveEntity(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        DwaineDeviceHandle handle,
        DwaineDeviceCapability required,
        out EntityUid entity)
    {
        entity = default;
        if (!TryGetRuntime(mainframe, out _, out var runtime) || runtime.Handles is null)
            return DwaineDeviceResult.MainframeUnavailable;
        if (!ValidateCaller(mainframe, process, principal, out _))
            return DwaineDeviceResult.AccessDenied;
        var resolve = runtime.Handles.TryResolve(
            handle,
            process,
            principal,
            runtime.BootGeneration,
            required,
            out var entry);
        if (resolve != DwaineDeviceResult.Success)
            return resolve;
        if (!runtime.Endpoints.TryGetValue(entry.Endpoint, out var endpoint))
            return DwaineDeviceResult.StaleHandle;
        if (!CanAccess(mainframe, process, principal, endpoint))
            return DwaineDeviceResult.AccessDenied;
        if (GetStatus(mainframe, endpoint) != DwaineDeviceStatus.Ready)
            return DwaineDeviceResult.Offline;
        entity = endpoint.Entity;
        return DwaineDeviceResult.Success;
    }

    public DwaineDeviceResult TryInjectTerminalInput(
        EntityUid mainframe,
        EntityUid trustedDevice,
        string text)
    {
        if (text.Length > DwaineTerminalComponent.HardMaxInputLength || text.IndexOf('\0') >= 0)
            return DwaineDeviceResult.MalformedMessage;
        if (!TryGetRuntime(mainframe, out _, out var runtime)
            || !runtime.ByEntity.TryGetValue(trustedDevice, out var endpointId)
            || !runtime.Endpoints.TryGetValue(endpointId, out var endpoint)
            || endpoint.TerminalSession is not { } session
            || !endpoint.Capabilities.HasFlag(DwaineDeviceCapability.TerminalInput))
        {
            return DwaineDeviceResult.AccessDenied;
        }
        return _transport.TryInjectTrustedInput(mainframe, session, text)
            ? DwaineDeviceResult.Success
            : DwaineDeviceResult.Offline;
    }

    private void OnKernelReady(Entity<DwaineDeviceAbiComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineDeviceAbiRuntimeComponent>(ent, out var runtime))
            return;
        Cleanup(ent.Owner, runtime);
        var limits = DwaineDeviceAbiLimits.FromComponent(ent.Comp);
        runtime.Handles = new DwaineDeviceCapabilityTable(limits.MaxHandles, limits.MaxHandlesPerProcess);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (!_kernel.TryRegisterService(
                ent.Owner,
                "device-abi",
                new DeviceKernelService(this, ent.Owner, args.BootGeneration)))
        {
            Cleanup(ent.Owner, runtime);
            _kernel.Panic(ent.Owner, "device-abi-service-registration");
            return;
        }
        Reconcile(ent.Owner, ent.Comp, runtime);
    }

    private void OnRuntimeShutdown(Entity<DwaineDeviceAbiRuntimeComponent> ent, ref ComponentShutdown args)
        => Cleanup(ent.Owner, ent.Comp);

    private void OnSessionConnected(Entity<DwaineDeviceAbiRuntimeComponent> ent, ref DwaineMainframeSessionConnectedEvent args)
    {
        if (TryComp<DwaineDeviceAbiComponent>(ent, out var config) && ent.Comp.Online)
            AttachTerminal(ent.Owner, args.Session, args.Terminal, config, ent.Comp);
    }

    private void OnSessionDisconnected(Entity<DwaineDeviceAbiRuntimeComponent> ent, ref DwaineMainframeSessionDisconnectedEvent args)
    {
        if (ent.Comp.ByTerminalSession.TryGetValue(args.Session, out var endpoint))
            Detach(ent.Owner, ent.Comp, endpoint);
    }

    private void OnProcessRemoved(Entity<DwaineDeviceAbiRuntimeComponent> ent, ref DwaineProcessRemovedEvent args)
    {
        ent.Comp.Handles?.InvalidateProcess(args.ProcessId);
        ent.Comp.NextScanAt.Remove(args.ProcessId);
    }

    private void OnDeviceShutdown(Entity<DwaineDeviceComponent> ent, ref ComponentShutdown args)
    {
        if (!_hostsByDevice.Remove(ent.Owner, out var hosts))
            return;
        foreach (var host in hosts.ToArray())
        {
            if (TryComp<DwaineDeviceAbiRuntimeComponent>(host, out var runtime)
                && runtime.ByEntity.TryGetValue(ent.Owner, out var endpoint))
            {
                Detach(host, runtime, endpoint);
            }
        }
    }

    private void Reconcile(EntityUid mainframe, DwaineDeviceAbiComponent config, DwaineDeviceAbiRuntimeComponent runtime)
    {
        if (TryComp<DwaineDeviceComponent>(mainframe, out var localDevice)
            && !runtime.ByEntity.ContainsKey(mainframe))
        {
            Attach(mainframe, mainframe, localDevice, config, runtime, null, false);
        }

        if (TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var transport))
        {
            foreach (var session in transport.Sessions.Values)
            {
                if (!runtime.ByTerminalSession.ContainsKey(session.Id))
                    AttachTerminal(mainframe, session.Id, session.Terminal, config, runtime);
            }
        }

        var inserted = _storage.GetInsertedMedia(mainframe);
        var liveMedia = inserted.Select(media => media.Media).ToHashSet();
        foreach (var media in inserted)
        {
            if (TryComp<DwaineDeviceComponent>(media.Media, out var deviceConfig)
                && !runtime.ByEntity.ContainsKey(media.Media))
            {
                Attach(mainframe, media.Media, deviceConfig, config, runtime, null, false);
            }
        }

        foreach (var (entity, endpointId) in runtime.ByEntity.ToArray())
        {
            if (!HasComp<DwaineStorageMediaComponent>(entity) || liveMedia.Contains(entity))
                continue;
            Detach(mainframe, runtime, endpointId);
        }
    }

    private void ReconcileNetwork(
        EntityUid mainframe,
        DwaineDeviceAbiComponent config,
        DwaineDeviceAbiRuntimeComponent runtime)
    {
        var limits = DwaineDeviceAbiLimits.FromComponent(config);
        if (_network.FindReachableEntities(mainframe, "device", limits.MaxAttachedDevices, out var devices)
            != DwaineNetworkResult.Success)
        {
            return;
        }
        var reachable = devices.ToHashSet();
        foreach (var endpoint in runtime.Endpoints.Values
                     .Where(endpoint => endpoint.NetworkAttached && !reachable.Contains(endpoint.Entity))
                     .Select(endpoint => endpoint.Id)
                     .ToArray())
        {
            Detach(mainframe, runtime, endpoint);
        }
        foreach (var device in devices)
        {
            if (runtime.ByEntity.ContainsKey(device)
                || !TryComp<DwaineDeviceComponent>(device, out var deviceConfig)
                || !IsValidDevice(deviceConfig))
            {
                continue;
            }
            Attach(mainframe, device, deviceConfig, config, runtime, null, true);
        }
    }

    private DwaineDeviceResult Attach(
        EntityUid mainframe,
        EntityUid device,
        DwaineDeviceComponent deviceConfig,
        DwaineDeviceAbiComponent abiConfig,
        DwaineDeviceAbiRuntimeComponent runtime,
        DwaineSessionId? terminalSession,
        bool networkAttached)
    {
        if (runtime.ByEntity.ContainsKey(device))
            return DwaineDeviceResult.Success;
        var limits = DwaineDeviceAbiLimits.FromComponent(abiConfig);
        if (runtime.Endpoints.Count >= limits.MaxAttachedDevices)
            return DwaineDeviceResult.CapacityReached;

        var address = string.IsNullOrWhiteSpace(deviceConfig.Address)
            ? AllocateAddress(runtime, deviceConfig.Tag)
            : deviceConfig.Address.ToLowerInvariant();
        if (!IsValidIdentifier(address))
            return DwaineDeviceResult.InvalidAddress;
        if (runtime.ByAddress.ContainsKey(address))
            return DwaineDeviceResult.DuplicateAddress;
        if (!TryAllocateEndpoint(runtime, out var endpointId))
            return DwaineDeviceResult.CapacityReached;

        var endpoint = new DwaineDeviceEndpoint
        {
            Id = endpointId,
            Address = address,
            Tag = deviceConfig.Tag.ToLowerInvariant(),
            DriverId = deviceConfig.DriverId.ToLowerInvariant(),
            DisplayName = deviceConfig.DisplayName,
            Capabilities = deviceConfig.Capabilities,
            Access = deviceConfig.Access,
            Entity = device,
            TerminalSession = terminalSession,
            NetworkAttached = networkAttached,
            Status = deviceConfig.Enabled ? DwaineDeviceStatus.Ready : DwaineDeviceStatus.Offline,
        };
        runtime.Endpoints.Add(endpointId, endpoint);
        runtime.ByAddress.Add(address, endpointId);
        runtime.ByEntity.Add(device, endpointId);
        if (terminalSession is { } session)
            runtime.ByTerminalSession.Add(session, endpointId);
        if (!_hostsByDevice.TryGetValue(device, out var hosts))
        {
            hosts = [];
            _hostsByDevice.Add(device, hosts);
        }
        hosts.Add(mainframe);
        return DwaineDeviceResult.Success;
    }

    private void AttachTerminal(
        EntityUid mainframe,
        DwaineSessionId session,
        EntityUid terminal,
        DwaineDeviceAbiComponent config,
        DwaineDeviceAbiRuntimeComponent runtime)
    {
        if (runtime.ByTerminalSession.ContainsKey(session))
            return;
        var terminalConfig = new DwaineDeviceComponent
        {
            DriverId = "user-terminal",
            Tag = "terminal",
            DisplayName = "user terminal",
            Capabilities = DwaineDeviceCapability.Inspect
                           | DwaineDeviceCapability.Message
                           | DwaineDeviceCapability.TerminalOutput
                           | DwaineDeviceCapability.TerminalInput,
            Access = DwaineDeviceAccess.Public,
        };
        Attach(mainframe, terminal, terminalConfig, config, runtime, session, false);
    }

    private void Detach(EntityUid mainframe, DwaineDeviceAbiRuntimeComponent runtime, DwaineDeviceEndpointId endpointId)
    {
        if (!runtime.Endpoints.Remove(endpointId, out var endpoint))
            return;
        runtime.ByAddress.Remove(endpoint.Address);
        runtime.ByEntity.Remove(endpoint.Entity);
        if (endpoint.TerminalSession is { } session)
            runtime.ByTerminalSession.Remove(session);
        runtime.Handles?.InvalidateEndpoint(endpointId);
        if (_hostsByDevice.TryGetValue(endpoint.Entity, out var hosts))
        {
            hosts.Remove(mainframe);
            if (hosts.Count == 0)
                _hostsByDevice.Remove(endpoint.Entity);
        }
    }

    private bool CanAccess(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        DwaineDeviceEndpoint endpoint)
    {
        if (!_processes.TryGetProcess(mainframe, process, out var caller)
            || caller.Owner.Value != principal.Value)
        {
            return false;
        }
        if (endpoint.TerminalSession is { } terminalSession
            && caller.TerminalSession?.Value != terminalSession.Value)
        {
            return false;
        }
        if (!_identities.TryGetStore(mainframe, out var identities)
            || !identities.TryGetAccount(principal, out var account)
            || !account.Enabled)
        {
            return false;
        }
        return endpoint.Access switch
        {
            DwaineDeviceAccess.Public => true,
            DwaineDeviceAccess.Authenticated => !account.Temporary,
            DwaineDeviceAccess.Operator => identities.HasPermission(principal, DwaineIdentityPermission.ManageUsers),
            _ => false,
        };
    }

    private bool ValidateCaller(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        out DwaineProcessSnapshot snapshot)
    {
        return _processes.TryGetProcess(mainframe, process, out snapshot)
               && snapshot.State is not (Content.Shared._Whiskey.Dwaine.Process.DwaineProcessState.Exited
                   or Content.Shared._Whiskey.Dwaine.Process.DwaineProcessState.Faulted)
               && snapshot.Owner.Value == principal.Value
               && _identities.TryGetStore(mainframe, out var identities)
               && identities.TryGetAccount(principal, out var account)
               && account.Enabled;
    }

    private DwaineDeviceStatus GetStatus(EntityUid mainframe, DwaineDeviceEndpoint endpoint)
    {
        if (TerminatingOrDeleted(endpoint.Entity))
            return DwaineDeviceStatus.Offline;
        if (endpoint.TerminalSession is { } session)
            return _transport.HasSession(mainframe, session) ? DwaineDeviceStatus.Ready : DwaineDeviceStatus.Offline;
        if (TryComp<DwaineDeviceComponent>(endpoint.Entity, out var config) && !config.Enabled)
            return DwaineDeviceStatus.Offline;
        if (TryComp<ApcPowerReceiverComponent>(endpoint.Entity, out var receiver) && !receiver.Powered)
            return DwaineDeviceStatus.Offline;
        if (_hardware.GetStatus(endpoint.Entity) is { } hardwareStatus
            && hardwareStatus != DwaineHardwareStatus.HardwareReady)
        {
            return DwaineDeviceStatus.Offline;
        }
        if (endpoint.NetworkAttached
            && _network.CanReach(mainframe, endpoint.Entity) != DwaineNetworkResult.Success)
        {
            return DwaineDeviceStatus.Offline;
        }
        if (string.Equals(endpoint.DriverId, "radio", StringComparison.Ordinal)
            && _network.GetNode(endpoint.Entity, out _) != DwaineNetworkResult.Success)
        {
            return DwaineDeviceStatus.Offline;
        }
        if (HasComp<DwaineStorageMediaComponent>(endpoint.Entity)
            && (!_storage.TryGetMediaSnapshot(endpoint.Entity, out var media) || media.InsertedInto != mainframe))
        {
            return DwaineDeviceStatus.Offline;
        }
        return endpoint.Status;
    }

    private DwaineDeviceDescriptor Snapshot(EntityUid mainframe, DwaineDeviceEndpoint endpoint)
        => new(endpoint.Address, endpoint.Tag, endpoint.DriverId, endpoint.DisplayName, GetStatus(mainframe, endpoint), endpoint.Capabilities);

    private bool TryGetRuntime(
        EntityUid mainframe,
        out DwaineDeviceAbiComponent config,
        out DwaineDeviceAbiRuntimeComponent runtime)
    {
        config = null!;
        runtime = null!;
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineDeviceAbiComponent>(mainframe, out var foundConfig)
            || !TryComp<DwaineDeviceAbiRuntimeComponent>(mainframe, out var foundRuntime))
        {
            return false;
        }
        config = foundConfig;
        runtime = foundRuntime;
        return runtime.Online
               && runtime.Handles is not null
               && _kernel.GetState(mainframe) == DwaineSystemState.SystemReady;
    }

    private void Cleanup(EntityUid mainframe, DwaineDeviceAbiRuntimeComponent runtime)
    {
        foreach (var endpoint in runtime.Endpoints.Values.ToArray())
        {
            if (_hostsByDevice.TryGetValue(endpoint.Entity, out var hosts))
            {
                hosts.Remove(mainframe);
                if (hosts.Count == 0)
                    _hostsByDevice.Remove(endpoint.Entity);
            }
        }
        runtime.Handles?.Clear();
        runtime.Handles = null;
        runtime.Endpoints.Clear();
        runtime.ByAddress.Clear();
        runtime.ByEntity.Clear();
        runtime.ByTerminalSession.Clear();
        runtime.NextScanAt.Clear();
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private static bool IsValidDevice(DwaineDeviceComponent component)
        => IsValidIdentifier(component.DriverId)
           && IsValidIdentifier(component.Tag)
           && (string.IsNullOrEmpty(component.Address) || IsValidIdentifier(component.Address))
           && !string.IsNullOrWhiteSpace(component.DisplayName)
           && component.DisplayName.Length <= DwaineDeviceComponent.HardMaxIdentifierLength
           && component.Capabilities != DwaineDeviceCapability.None;

    private static bool IsValidIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > DwaineDeviceComponent.HardMaxIdentifierLength)
            return false;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static bool IsValidCommand(string? command)
        => IsValidIdentifier(command);

    private static string AllocateAddress(DwaineDeviceAbiRuntimeComponent runtime, string tag)
    {
        var prefix = IsValidIdentifier(tag) ? tag.ToLowerInvariant() : "device";
        for (var attempt = 0; attempt <= runtime.Endpoints.Count; attempt++)
        {
            var value = runtime.NextGeneratedAddress++;
            if (runtime.NextGeneratedAddress == 0)
                runtime.NextGeneratedAddress = 1;
            var candidate = $"{prefix}-{value}";
            if (!runtime.ByAddress.ContainsKey(candidate))
                return candidate;
        }
        return string.Empty;
    }

    private static bool TryAllocateEndpoint(DwaineDeviceAbiRuntimeComponent runtime, out DwaineDeviceEndpointId endpoint)
    {
        for (var attempt = 0; attempt <= runtime.Endpoints.Count; attempt++)
        {
            var value = runtime.NextEndpointId++;
            if (runtime.NextEndpointId == 0)
                runtime.NextEndpointId = 1;
            endpoint = new DwaineDeviceEndpointId(value);
            if (endpoint.IsValid && !runtime.Endpoints.ContainsKey(endpoint))
                return true;
        }
        endpoint = default;
        return false;
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (TryComp<DwaineDeviceAbiRuntimeComponent>(mainframe, out var runtime)
            && runtime.BootGeneration == generation)
        {
            Cleanup(mainframe, runtime);
        }
    }

    private sealed class DeviceKernelService(DwaineDeviceSystem system, EntityUid mainframe, ulong generation)
        : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }
}
