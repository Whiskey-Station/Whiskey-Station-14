// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Hardware;

namespace Content.Server._Whiskey.Dwaine.Transport;

/// <summary>
/// Server-internal identity. It is deliberately not serializable or exposed through the BUI.
/// </summary>
public readonly record struct DwaineSessionId(ulong Value);

public enum DwaineConnectResult : byte
{
    Connected,
    AlreadyConnected,
    InvalidTerminal,
    InvalidMainframe,
    UnauthorizedActor,
    TerminalUnavailable,
    MainframeUnavailable,
    TopologyMismatch,
    OutOfRange,
    CapacityReached,
    TerminalAlreadyConnected,
}

public enum DwaineDisconnectReason : byte
{
    Requested,
    TerminalUnavailable,
    MainframeUnavailable,
    TopologyChanged,
    EntityRemoved,
}

[RegisterComponent]
public sealed partial class DwaineTerminalLinkComponent : Component
{
    public DwaineSessionId? Session;
    public EntityUid? Mainframe;
    public EntityUid? SessionOwner;
    public DwaineTerminalConnectionStatus PresentationStatus;
}

[RegisterComponent]
public sealed partial class DwaineMainframeRuntimeComponent : Component
{
    public readonly Dictionary<DwaineSessionId, DwaineTerminalSession> Sessions = new();
    public readonly Dictionary<EntityUid, DwaineSessionId> TerminalSessions = new();
    public string IndexedNetworkId = string.Empty;
}

public sealed class DwaineTerminalSession(
    DwaineSessionId id,
    EntityUid terminal,
    EntityUid owner,
    TimeSpan connectedAt,
    DwaineBoundedTextBuffer output,
    DwaineBoundedTextBuffer pendingInput)
{
    public DwaineSessionId Id { get; } = id;
    public EntityUid Terminal { get; } = terminal;
    public EntityUid Owner { get; } = owner;
    public TimeSpan ConnectedAt { get; } = connectedAt;
    public DwaineBoundedTextBuffer Output { get; } = output;
    public DwaineBoundedTextBuffer PendingInput { get; } = pendingInput;
}

[ByRefEvent]
public readonly record struct DwaineMainframeInputReceivedEvent(
    DwaineSessionId Session,
    EntityUid Terminal,
    EntityUid Owner,
    string Text);

[ByRefEvent]
public readonly record struct DwaineMainframeSessionConnectedEvent(
    DwaineSessionId Session,
    EntityUid Terminal,
    EntityUid Owner);

[ByRefEvent]
public readonly record struct DwaineMainframeSessionDisconnectedEvent(
    DwaineSessionId Session,
    EntityUid Terminal,
    EntityUid Owner,
    DwaineDisconnectReason Reason);
