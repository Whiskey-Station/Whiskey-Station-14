// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.Services;

[RegisterComponent]
public sealed partial class DwaineServiceRuntimeComponent : Component
{
    public DwaineServiceStore? Store;
    public bool Online;
    public ulong BootGeneration;
}
