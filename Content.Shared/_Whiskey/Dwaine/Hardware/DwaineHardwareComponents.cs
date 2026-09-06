// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Hardware;

/// <summary>
/// Physical DWAINE chassis classification and power requirements.
/// Runtime power state remains server-owned.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineComputerHardwareComponent : Component
{
    [DataField]
    public DwaineMachineKind Kind = DwaineMachineKind.Computer;

    [DataField]
    public bool RequiresExternalPower = true;
}

/// <summary>
/// Marks hardware capable of presenting the terminal BUI.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineTerminalComponent : Component
{
    public const int HardMaxInputLength = 4096;
    public const int HardMaxOutputLines = 256;
    public const int HardMaxOutputCharacters = 65536;

    [DataField]
    public int MaxInputLength = 512;

    [DataField]
    public int OutputLineLimit = 64;

    [DataField]
    public int OutputCharacterLimit = 8192;
}

/// <summary>
/// Describes the physical display independently from terminal transport.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineDisplayComponent : Component
{
    [DataField]
    public int Columns = 80;

    [DataField]
    public int Rows = 25;
}

/// <summary>
/// Server-validated line input hardware. This is not a shell parser.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineKeyboardInputComponent : Component
{
    [DataField]
    public bool Enabled = true;
}

/// <summary>
/// Physical storage attachment points. Volumes and media land in PR 07.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineStorageConnectorComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public int SlotCount = 1;
}

/// <summary>
/// Physical network interface. Routing and discovery land in PR 13.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineNetworkConnectorComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public string NetworkId = "station";

    [DataField]
    public float LinkRange = 16f;
}

/// <summary>
/// Physical endpoint on a DWAINE device bus. No device ABI is exposed here.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineDeviceBusEndpointComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public string BusId = "primary";

    [DataField]
    public int EndpointLimit = 16;
}
