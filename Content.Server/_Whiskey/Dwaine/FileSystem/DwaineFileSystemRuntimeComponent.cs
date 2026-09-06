// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.FileSystem;

[RegisterComponent]
public sealed partial class DwaineFileSystemRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public DwaineVirtualFileSystem? FileSystem;
}
