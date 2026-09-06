// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Whiskey.Dwaine.Hardware;

[Serializable, NetSerializable]
public enum DwaineTerminalUiKey : byte
{
    Key,
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
    string[] output) : BoundUserInterfaceState
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
