// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Hardware;

namespace Content.Server._Whiskey.Dwaine.Network;

public enum DwaineNetworkResult : byte
{
    Success,
    Pending,
    InvalidNode,
    InvalidAddress,
    InvalidPayload,
    DuplicateAddress,
    Disabled,
    NotFound,
    CrossNetwork,
    AdapterMismatch,
    OutOfRange,
    Interfered,
    RateLimited,
    PayloadTooLarge,
    CapacityReached,
    Unsupported,
    Timeout,
    Disconnected,
}

public readonly record struct DwaineNetworkAddress(string Value)
{
    public bool IsValid => !string.IsNullOrEmpty(Value);
    public override string ToString() => Value;
}

public readonly record struct DwaineNetworkCorrelationId(ulong Value)
{
    public bool IsValid => Value != 0;
}

public readonly record struct DwaineNetworkNodeSnapshot(
    DwaineNetworkAddress Address,
    string NetworkId,
    DwaineNetworkAdapter Adapter,
    int Frequency,
    string Channel,
    string[] Tags,
    bool Online);

public readonly record struct DwaineNetworkMetricsSnapshot(
    ulong Sent,
    ulong Delivered,
    ulong Dropped,
    ulong Discoveries,
    ulong Requests,
    ulong Replies,
    int PendingRequests,
    int CapturedEntries);

public readonly record struct DwaineNetworkCaptureEntry(
    TimeSpan At,
    string Source,
    string Destination,
    string Protocol,
    int PayloadCharacters,
    DwaineNetworkResult Result);

public readonly record struct DwaineNetworkPacket(
    EntityUid SourceEntity,
    DwaineNetworkAddress Source,
    DwaineNetworkAddress Destination,
    string Protocol,
    string Payload,
    DwaineNetworkCorrelationId Correlation);

/// <summary>
/// Server-only typed packet delivery. Receivers can acknowledge or produce one bounded reply; no
/// entity reference is server-local and is never serialized; no object graph, session, PID or
/// capability enters the public ABI.
/// </summary>
[ByRefEvent]
public record struct DwaineNetworkPacketReceivedEvent(DwaineNetworkPacket Packet)
{
    public bool Handled;
    public string? Reply;
}

internal sealed class DwaineNetworkNode
{
    public required EntityUid Entity;
    public required string GeneratedAddress;
    public string Address = string.Empty;
    public string NetworkId = string.Empty;
    public DwaineNetworkAdapter Adapter;
    public int Frequency;
    public string Channel = string.Empty;
    public string[] Tags = [];
    public bool Conflict;
    public ulong Sent;
    public ulong Delivered;
    public ulong Dropped;
    public ulong Discoveries;
    public ulong Requests;
    public ulong Replies;
    public readonly Queue<DwaineNetworkCaptureEntry> Capture = [];
}

internal sealed class DwainePendingNetworkRequest
{
    public required DwaineNetworkCorrelationId Correlation;
    public required EntityUid Source;
    public required EntityUid Destination;
    public required TimeSpan ExpiresAt;
    public DwaineNetworkResult Result = DwaineNetworkResult.Pending;
    public string Reply = string.Empty;
}

internal readonly record struct DwaineNetworkLimits(
    int MaxPayloadCharacters,
    int MaxPendingRequests,
    int MaxDiscoveryResults,
    int MaxCaptureEntries,
    TimeSpan DiscoveryCooldown,
    TimeSpan RequestTimeout);
