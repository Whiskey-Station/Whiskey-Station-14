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
    public const int HardMaxSlotCount = 32;

    [DataField]
    public bool Enabled = true;

    [DataField]
    public int SlotCount = 1;
}

/// <summary>
/// Physical network interface. Routing and discovery land in PR 13.
/// </summary>
[Flags]
public enum DwaineNetworkAdapter : byte
{
    None = 0,
    Wired = 1 << 0,
    Radio = 1 << 1,
    Omni = Wired | Radio,
}

[RegisterComponent]
public sealed partial class DwaineNetworkConnectorComponent : Component
{
    public const int HardMaxNetworkIdLength = 64;
    public const int HardMaxAddressLength = 64;
    public const int HardMaxTagCount = 16;
    public const int HardMaxTagLength = 32;
    public const int MinimumFrequency = 100;
    public const int MaximumFrequency = 9999;
    public const float HardMaxLinkRange = 256f;

    [DataField]
    public bool Enabled = true;

    [DataField]
    public string NetworkId = "station";

    /// <summary>
    /// Optional requested address. The server allocates an opaque unique address when empty and owns
    /// duplicate resolution regardless of prototype configuration.
    /// </summary>
    [DataField]
    public string Address = string.Empty;

    [DataField]
    public DwaineNetworkAdapter Adapter = DwaineNetworkAdapter.Radio;

    [DataField]
    public List<string> Tags = [];

    [DataField]
    public int Frequency = 1459;

    [DataField]
    public string Channel = "station";

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
