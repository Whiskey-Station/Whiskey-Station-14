// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.FileSystem;

public enum DwaineVfsNodeKind : byte
{
    Directory,
    Text,
    Record,
    UserData,
    SymbolicLink,
    Archive,
    Signal,
    ImageMetadata,
    Program,
    System,
}

[Flags]
public enum DwaineVfsNodeFlags : byte
{
    None = 0,
    ReadOnly = 1 << 0,
    Virtual = 1 << 1,
    System = 1 << 2,
}

[Flags]
public enum DwaineVfsMode : ushort
{
    None = 0,
    OwnerRead = 1 << 0,
    OwnerWrite = 1 << 1,
    OwnerExecute = 1 << 2,
    GroupRead = 1 << 3,
    GroupWrite = 1 << 4,
    GroupExecute = 1 << 5,
    OtherRead = 1 << 6,
    OtherWrite = 1 << 7,
    OtherExecute = 1 << 8,

    OwnerAll = OwnerRead | OwnerWrite | OwnerExecute,
    GroupReadExecute = GroupRead | GroupExecute,
    OtherReadExecute = OtherRead | OtherExecute,
    DefaultDirectory = OwnerAll | GroupReadExecute | OtherReadExecute,
    DefaultFile = OwnerRead | OwnerWrite | GroupRead | OtherRead,
    ReadOnlyFile = OwnerRead | GroupRead | OtherRead,
}

/// <summary>
/// Server-clamped limits for the logical filesystem attached to a DWAINE mainframe.
/// The actual tree remains server-only and is never replicated through this component.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineFileSystemComponent : Component
{
    // The canonical root tree contains fifteen nodes, reaches depth two and has
    // eleven direct children. Configuration is clamped to these structural minima.
    public const int MinimumSystemNodes = 15;
    public const int MinimumSystemDepth = 2;
    public const int MinimumSystemNameLength = 4;
    public const int MinimumSystemPathLength = 9;
    public const int MinimumSystemChildren = 11;

    public const int HardMaxNodes = 65_536;
    public const int HardMaxDepth = 128;
    public const int HardMaxNameLength = 128;
    public const int HardMaxPathLength = 4096;
    public const int HardMaxChildrenPerDirectory = 4096;
    public const int HardMaxLinkDepth = 64;
    public const int HardMaxTextCharacters = 262_144;
    public const int HardMaxRecordEntries = 1024;
    public const int HardMaxRecordCharacters = 262_144;
    public const int HardMaxArchiveEntries = 4096;
    public const int HardMaxArchiveDepth = 32;

    [DataField]
    public int MaxNodes = 8192;

    [DataField]
    public int MaxDepth = 32;

    [DataField]
    public int MaxNameLength = 64;

    [DataField]
    public int MaxPathLength = 1024;

    [DataField]
    public int MaxChildrenPerDirectory = 512;

    [DataField]
    public int MaxLinkDepth = 16;

    [DataField]
    public int MaxTextCharacters = 65_536;

    [DataField]
    public int MaxRecordEntries = 256;

    [DataField]
    public int MaxRecordCharacters = 65_536;

    [DataField]
    public int MaxArchiveEntries = 1024;

    [DataField]
    public int MaxArchiveDepth = 16;
}
