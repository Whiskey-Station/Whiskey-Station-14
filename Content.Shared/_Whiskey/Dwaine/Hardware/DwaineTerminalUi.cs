// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Whiskey.Dwaine.Hardware;

[Serializable, NetSerializable]
public enum DwaineTerminalUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum DwaineTerminalConnectionStatus : byte
{
    Disconnected,
    Connected,
    MainframeUnavailable,
}

/// <summary>
/// Presentation-only mainframe target. The server revalidates the entity and topology on connect.
/// </summary>
[Serializable, NetSerializable]
public sealed class DwaineMainframeUiEntry(NetEntity entity, string name)
{
    public readonly NetEntity Entity = entity;
    public readonly string Name = name;
}

/// <summary>
/// Presentation-safe hardware state. It intentionally contains no user, process,
/// credential, filesystem, mainframe, or permission authority.
/// </summary>
[Serializable, NetSerializable]
public sealed class DwaineTerminalBoundUserInterfaceState(
    DwaineHardwareStatus status,
    bool powerEnabled,
    bool hasPowerSupply,
    int maxInputLength,
    int displayColumns,
    int displayRows,
    int storageSlots,
    string networkId,
    string busId,
    string[] output,
    DwaineTerminalConnectionStatus connectionStatus,
    string connectedMainframe,
    DwaineMainframeUiEntry[] availableMainframes) : BoundUserInterfaceState
{
    public readonly DwaineHardwareStatus Status = status;
    public readonly bool PowerEnabled = powerEnabled;
    public readonly bool HasPowerSupply = hasPowerSupply;
    public readonly int MaxInputLength = maxInputLength;
    public readonly int DisplayColumns = displayColumns;
    public readonly int DisplayRows = displayRows;
    public readonly int StorageSlots = storageSlots;
    public readonly string NetworkId = networkId;
    public readonly string BusId = busId;
    public readonly string[] Output = output;
    public readonly DwaineTerminalConnectionStatus ConnectionStatus = connectionStatus;
    public readonly string ConnectedMainframe = connectedMainframe;
    public readonly DwaineMainframeUiEntry[] AvailableMainframes = availableMainframes;
}

/// <summary>
/// Requests a power transition. The desired state is derived on the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class DwaineTerminalTogglePowerMessage : BoundUserInterfaceMessage;

/// <summary>
/// Carries only unprivileged text. The server derives terminal and actor identity
/// from the BUI envelope and validates the text again.
/// </summary>
[Serializable, NetSerializable]
public sealed class DwaineTerminalInputMessage(string text) : BoundUserInterfaceMessage
{
    public readonly string Text = text;
}

/// <summary>
/// Requests a target only. Session identity and ownership are always assigned by the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class DwaineTerminalConnectMessage(NetEntity target) : BoundUserInterfaceMessage
{
    public readonly NetEntity Target = target;
}

[Serializable, NetSerializable]
public sealed class DwaineTerminalDisconnectMessage : BoundUserInterfaceMessage;
