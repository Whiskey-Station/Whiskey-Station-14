// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.NanoXp;

[RegisterComponent]
public sealed partial class NanoXpNetworkRuntimeComponent : Component
{
    public readonly NanoXpAccountStore Store = new();
    public readonly Dictionary<EntityUid, NanoXpLoginThrottle> LoginThrottle = new();
    public ulong NextTerminal = 1;
}

[RegisterComponent]
public sealed partial class NanoXpDeviceRuntimeComponent : Component
{
    public readonly Dictionary<EntityUid, NanoXpSessionSnapshot> Sessions = new();
    public readonly Dictionary<EntityUid, TimeSpan> LastMailAt = new();
}

public readonly record struct NanoXpLoginThrottle(int Failures, TimeSpan NextAttempt);
