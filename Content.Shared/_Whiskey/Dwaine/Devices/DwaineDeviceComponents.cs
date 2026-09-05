// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Devices;

[Flags]
public enum DwaineDeviceCapability : ushort
{
    None = 0,
    Inspect = 1 << 0,
    Message = 1 << 1,
    TerminalOutput = 1 << 2,
    TerminalInput = 1 << 3,
    Mount = 1 << 4,
}

public enum DwaineDeviceAccess : byte
{
    Public,
    Authenticated,
    Operator,
}

/// <summary>
/// Declares a server-driven DWAINE device. Runtime attachment and opaque handles are never networked.
/// PR 13 extends attachment through explicit network topology; this component does not perform a global scan.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineDeviceComponent : Component
{
    public const int HardMaxIdentifierLength = 64;

    [DataField(required: true)]
    public string DriverId = string.Empty;

    [DataField]
    public string Address = string.Empty;

    [DataField]
    public string Tag = "device";

    [DataField]
    public string DisplayName = "DWAINE device";

    [DataField]
    public DwaineDeviceCapability Capabilities = DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message;

    [DataField]
    public DwaineDeviceAccess Access = DwaineDeviceAccess.Authenticated;

    [DataField]
    public bool Enabled = true;
}

/// <summary>
/// Configures the bounded server-only ABI state owned by a mainframe.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineDeviceAbiComponent : Component
{
    public const int HardMaxAttachedDevices = 1024;
    public const int HardMaxHandles = 4096;
    public const int HardMaxHandlesPerProcess = 128;
    public const int HardMaxMessageCharacters = 8192;

    [DataField]
    public int MaxAttachedDevices = 128;

    [DataField]
    public int MaxHandles = 512;

    [DataField]
    public int MaxHandlesPerProcess = 32;

    [DataField]
    public int MaxMessageCharacters = 2048;

    [DataField]
    public float ScanCooldownSeconds = 1f;
}
