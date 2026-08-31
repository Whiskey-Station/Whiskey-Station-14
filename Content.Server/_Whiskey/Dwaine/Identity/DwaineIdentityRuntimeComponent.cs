// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.Identity;

[RegisterComponent]
public sealed partial class DwaineIdentityRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public DwaineIdentityStore? Store;
}
