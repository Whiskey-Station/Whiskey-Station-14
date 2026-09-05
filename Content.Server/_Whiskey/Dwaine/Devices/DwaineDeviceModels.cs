// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Devices;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Devices;

public readonly record struct DwaineDeviceHandle(ulong Value)
{
    public bool IsValid => Value != 0;
}

public readonly record struct DwaineDeviceEndpointId(ulong Value)
{
    public bool IsValid => Value != 0;
}

public enum DwaineDeviceStatus : byte
{
    Ready,
    Offline,
    Busy,
    Faulted,
}

public enum DwaineDeviceResult : byte
{
    Success,
    MainframeUnavailable,
    InvalidDevice,
    InvalidAddress,
    DuplicateAddress,
    CapacityReached,
    AccessDenied,
    Unsupported,
    Offline,
    NotFound,
    StaleHandle,
    MalformedMessage,
    RateLimited,
    DriverFailure,
}

public readonly record struct DwaineDeviceDescriptor(
    string Address,
    string Tag,
    string DriverId,
    string DisplayName,
    DwaineDeviceStatus Status,
    DwaineDeviceCapability Capabilities);

public readonly record struct DwaineDeviceResponse(
    DwaineDeviceResult Result,
    string Payload,
    DwaineDeviceStatus Status = DwaineDeviceStatus.Ready)
{
    public bool Succeeded => Result == DwaineDeviceResult.Success;

    public static DwaineDeviceResponse Success(string payload = "", DwaineDeviceStatus status = DwaineDeviceStatus.Ready)
        => new(DwaineDeviceResult.Success, payload, status);

    public static DwaineDeviceResponse Failure(DwaineDeviceResult result, DwaineDeviceStatus status = DwaineDeviceStatus.Offline)
        => new(result, string.Empty, status);
}

public readonly record struct DwaineDeviceDriverContext(
    EntityUid Mainframe,
    DwaineProcessId ProcessId,
    DwainePrincipalId Principal,
    DwaineDeviceHandle Handle,
    DwaineDeviceCapability GrantedCapabilities);

/// <summary>
/// Raised only after an opaque handle, generation, process owner and capability have been revalidated.
/// Driver systems are trusted server code; no EntityUid crosses the Vodka ABI.
/// </summary>
[ByRefEvent]
public record struct DwaineDeviceMessageEvent(
    DwaineDeviceDriverContext Context,
    string Command,
    string Payload,
    DwaineDeviceResponse Response,
    bool Handled = false);

internal readonly record struct DwaineDeviceAbiLimits(
    int MaxAttachedDevices,
    int MaxHandles,
    int MaxHandlesPerProcess,
    int MaxMessageCharacters,
    TimeSpan ScanCooldown)
{
    public static DwaineDeviceAbiLimits FromComponent(DwaineDeviceAbiComponent component)
    {
        var cooldown = float.IsFinite(component.ScanCooldownSeconds)
            ? component.ScanCooldownSeconds
            : 1f;
        return new DwaineDeviceAbiLimits(
            Math.Clamp(component.MaxAttachedDevices, 1, DwaineDeviceAbiComponent.HardMaxAttachedDevices),
            Math.Clamp(component.MaxHandles, 1, DwaineDeviceAbiComponent.HardMaxHandles),
            Math.Clamp(component.MaxHandlesPerProcess, 1, DwaineDeviceAbiComponent.HardMaxHandlesPerProcess),
            Math.Clamp(component.MaxMessageCharacters, 1, DwaineDeviceAbiComponent.HardMaxMessageCharacters),
            TimeSpan.FromSeconds(Math.Clamp(cooldown, 0.1f, 30f)));
    }
}

internal sealed class DwaineDeviceEndpoint
{
    public required DwaineDeviceEndpointId Id;
    public required string Address;
    public required string Tag;
    public required string DriverId;
    public required string DisplayName;
    public required DwaineDeviceCapability Capabilities;
    public required DwaineDeviceAccess Access;
    public required EntityUid Entity;
    public DwaineSessionId? TerminalSession;
    public DwaineDeviceStatus Status = DwaineDeviceStatus.Ready;
}

internal readonly record struct DwaineDeviceCapabilityEntry(
    DwaineDeviceEndpointId Endpoint,
    DwaineProcessId Process,
    DwainePrincipalId Principal,
    ulong Generation,
    DwaineDeviceCapability Capabilities);

/// <summary>
/// Pure bounded capability table. Tokens are type-separated from integers in Vodka Code and are
/// additionally scoped to process, principal and boot generation on every resolution.
/// </summary>
internal sealed class DwaineDeviceCapabilityTable
{
    private readonly Dictionary<DwaineDeviceHandle, DwaineDeviceCapabilityEntry> _entries = [];
    private readonly Dictionary<DwaineProcessId, int> _byProcess = [];
    private readonly int _capacity;
    private readonly int _perProcessCapacity;
    private ulong _nextHandle = 1;

    public int Count => _entries.Count;

    public DwaineDeviceCapabilityTable(int capacity, int perProcessCapacity)
    {
        if (capacity <= 0 || perProcessCapacity <= 0 || perProcessCapacity > capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _perProcessCapacity = perProcessCapacity;
    }

    public DwaineDeviceResult TryIssue(
        DwaineDeviceEndpointId endpoint,
        DwaineProcessId process,
        DwainePrincipalId principal,
        ulong generation,
        DwaineDeviceCapability available,
        DwaineDeviceCapability requested,
        out DwaineDeviceHandle handle)
    {
        handle = default;
        if (!endpoint.IsValid || !process.IsValid || generation == 0 || requested == DwaineDeviceCapability.None)
            return DwaineDeviceResult.AccessDenied;
        if ((available & requested) != requested)
            return DwaineDeviceResult.Unsupported;

        foreach (var (candidate, entry) in _entries)
        {
            if (entry.Endpoint == endpoint
                && entry.Process == process
                && entry.Principal == principal
                && entry.Generation == generation
                && entry.Capabilities == requested)
            {
                handle = candidate;
                return DwaineDeviceResult.Success;
            }
        }

        if (_entries.Count >= _capacity || _byProcess.GetValueOrDefault(process) >= _perProcessCapacity)
            return DwaineDeviceResult.CapacityReached;
        if (!TryAllocate(out handle))
            return DwaineDeviceResult.CapacityReached;

        _entries.Add(handle, new DwaineDeviceCapabilityEntry(endpoint, process, principal, generation, requested));
        _byProcess[process] = _byProcess.GetValueOrDefault(process) + 1;
        return DwaineDeviceResult.Success;
    }

    public DwaineDeviceResult TryResolve(
        DwaineDeviceHandle handle,
        DwaineProcessId process,
        DwainePrincipalId principal,
        ulong generation,
        DwaineDeviceCapability required,
        out DwaineDeviceCapabilityEntry entry)
    {
        entry = default;
        if (!handle.IsValid || !_entries.TryGetValue(handle, out var stored))
            return DwaineDeviceResult.StaleHandle;
        if (stored.Process != process || stored.Principal != principal || stored.Generation != generation)
            return DwaineDeviceResult.AccessDenied;
        if ((stored.Capabilities & required) != required)
            return DwaineDeviceResult.Unsupported;
        entry = stored;
        return DwaineDeviceResult.Success;
    }

    public int InvalidateEndpoint(DwaineDeviceEndpointId endpoint)
        => RemoveWhere(entry => entry.Endpoint == endpoint);

    public int InvalidateProcess(DwaineProcessId process)
        => RemoveWhere(entry => entry.Process == process);

    public void Clear()
    {
        _entries.Clear();
        _byProcess.Clear();
    }

    private int RemoveWhere(Func<DwaineDeviceCapabilityEntry, bool> predicate)
    {
        var handles = _entries.Where(pair => predicate(pair.Value)).Select(pair => pair.Key).ToArray();
        foreach (var handle in handles)
        {
            var entry = _entries[handle];
            _entries.Remove(handle);
            var count = _byProcess.GetValueOrDefault(entry.Process) - 1;
            if (count <= 0)
                _byProcess.Remove(entry.Process);
            else
                _byProcess[entry.Process] = count;
        }
        return handles.Length;
    }

    private bool TryAllocate(out DwaineDeviceHandle handle)
    {
        for (var attempt = 0; attempt <= _entries.Count; attempt++)
        {
            var value = _nextHandle++;
            if (_nextHandle == 0)
                _nextHandle = 1;
            handle = new DwaineDeviceHandle(value);
            if (handle.IsValid && !_entries.ContainsKey(handle))
                return true;
        }
        handle = default;
        return false;
    }
}
