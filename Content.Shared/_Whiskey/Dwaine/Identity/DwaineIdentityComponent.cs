// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Identity;

/// <summary>
/// Server-clamped identity and authentication limits for a mainframe.
/// Accounts, password verifiers and sessions are never networked through this component.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineIdentityComponent : Component
{
    public const int HardMaxAccounts = 4096;
    public const int HardMaxGroups = 256;
    public const int HardMaxSessions = 2048;
    public const float HardMaxSessionLifetimeSeconds = 86_400f;

    [DataField]
    public int MaxAccounts = 512;

    [DataField]
    public int MaxGroups = 64;

    [DataField]
    public int MaxSessions = 256;

    [DataField]
    public float SessionLifetimeSeconds = 3600f;
}
