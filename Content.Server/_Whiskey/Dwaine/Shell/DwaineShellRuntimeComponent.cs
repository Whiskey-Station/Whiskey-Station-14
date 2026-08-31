// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Transport;

namespace Content.Server._Whiskey.Dwaine.Shell;

[RegisterComponent]
public sealed partial class DwaineShellRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public readonly Dictionary<DwaineSessionId, DwaineShellSession> Sessions = new();
}
