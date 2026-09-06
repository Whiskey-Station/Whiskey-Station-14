// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Network;

/// <summary>
/// Bounded packet endpoint configuration. Packet state and addresses remain server-only.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineNetworkEndpointComponent : Component
{
    public const int HardMaxPayloadCharacters = 16_384;
    public const int HardMaxPendingRequests = 256;
    public const int HardMaxDiscoveryResults = 256;
    public const int HardMaxCaptureEntries = 512;
    public const float HardMaxRequestTimeoutSeconds = 30f;

    [DataField]
    public bool Enabled = true;

    [DataField]
    public int MaxPayloadCharacters = 2048;

    [DataField]
    public int MaxPendingRequests = 64;

    [DataField]
    public int MaxDiscoveryResults = 64;

    [DataField]
    public int MaxCaptureEntries = 128;

    [DataField]
    public float DiscoveryCooldownSeconds = 1f;

    [DataField]
    public float RequestTimeoutSeconds = 3f;
}

/// <summary>
/// Explicit radio interference source. The network system indexes these components at map init;
/// packet routing never searches every entity on a map.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineNetworkJammerComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public string NetworkId = "station";

    [DataField]
    public int Frequency = 1459;

    [DataField]
    public string Channel = "station";

    [DataField]
    public float Range = 8f;
}

/// <summary>
/// Explicit allow-list for bootloader recovery. A booting mainframe may contact only this address
/// and must receive the configured profile; arbitrary discovery is never trusted as boot media.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineNetworkBootClientComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField(required: true)]
    public string ProviderAddress = string.Empty;

    [DataField]
    public string RecoveryProfile = "whiskey-recovery-v1";
}

/// <summary>
/// Read-only network recovery endpoint. It returns a fixed bounded profile, never native code.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineNetworkBootProviderComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public string RecoveryProfile = "whiskey-recovery-v1";
}

/// <summary>
/// Mainframe message service. Mailboxes are keyed by server-derived principals in Server state.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineCommunicationServiceComponent : Component
{
    public const int HardMaxMessages = 4096;
    public const int HardMaxMessagesPerUser = 256;
    public const int HardMaxMessageCharacters = 4096;
    public const int HardMaxFileCharacters = 16_384;

    [DataField]
    public int MaxMessages = 512;

    [DataField]
    public int MaxMessagesPerUser = 64;

    [DataField]
    public int MaxMessageCharacters = 1024;

    [DataField]
    public int MaxFileCharacters = 8192;
}
