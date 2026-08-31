// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.FileSystem;

namespace Content.Server._Whiskey.Dwaine.FileSystem;

public readonly record struct DwaineVfsVolumeId(ulong Value)
{
    public static readonly DwaineVfsVolumeId System = new(1);
    public bool IsValid => Value != 0;
}

public readonly record struct DwaineVfsNodeId(ulong Value)
{
    public static readonly DwaineVfsNodeId Root = new(1);
    public bool IsValid => Value != 0;
}

/// <summary>
/// Opaque server-side identity. Paths are presentation; a handle remains stable across rename and move.
/// </summary>
public readonly record struct DwaineVfsNodeHandle(DwaineVfsVolumeId Volume, DwaineVfsNodeId Node)
{
    public static readonly DwaineVfsNodeHandle Root = new(DwaineVfsVolumeId.System, DwaineVfsNodeId.Root);
    public bool IsValid => Volume.IsValid && Node.IsValid;
}

public enum DwaineVfsResult : byte
{
    Success,
    Offline,
    InvalidPath,
    InvalidName,
    RootEscape,
    NotFound,
    AlreadyExists,
    NotDirectory,
    IsDirectory,
    DirectoryNotEmpty,
    RootProtected,
    ReadOnly,
    AccessDenied,
    InvalidType,
    InvalidHandle,
    DepthLimit,
    LinkDepthLimit,
    LinkCycle,
    BrokenLink,
    NodeLimit,
    ChildLimit,
    DataLimit,
    DestinationInsideSource,
    CrossVolumeMoveDenied,
    MountUnavailable,
    VolumeAlreadyAttached,
    MountPointBusy,
    VolumeNotAttached,
}

public readonly record struct DwaineVfsMetadata(
    ulong Owner,
    ulong Group,
    DwaineVfsMode Mode,
    DwaineVfsNodeFlags Flags,
    TimeSpan CreatedAt,
    TimeSpan ModifiedAt);

public readonly record struct DwaineVfsNodeSnapshot(
    DwaineVfsNodeHandle Handle,
    DwaineVfsNodeHandle? Parent,
    string Name,
    DwaineVfsNodeKind Kind,
    DwaineVfsMetadata Metadata,
    int Size,
    int ChildCount);

public sealed class DwaineVfsCreateRequest
{
    public required DwaineVfsNodeKind Kind { get; init; }
    public ulong Owner { get; init; }
    public ulong Group { get; init; }
    public DwaineVfsMode? Mode { get; init; }
    public DwaineVfsNodeFlags Flags { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string?>? Fields { get; init; }
    public DwaineVfsUserData UserData { get; init; } = DwaineVfsUserData.Empty;
    public DwaineVfsSignalData Signal { get; init; } = DwaineVfsSignalData.Empty;
    public DwaineVfsImageMetadata Image { get; init; } = DwaineVfsImageMetadata.Empty;
    public DwaineVfsProgramData Program { get; init; } = DwaineVfsProgramData.Empty;
}

public readonly record struct DwaineVfsUserData(
    string RegisteredName,
    string Assignment,
    IReadOnlyList<string> AccessTags)
{
    public static readonly DwaineVfsUserData Empty = new(string.Empty, string.Empty, Array.Empty<string>());
}

public readonly record struct DwaineVfsSignalData(
    IReadOnlyDictionary<string, string?> Fields,
    string EncryptionTag)
{
    public static readonly DwaineVfsSignalData Empty = new(
        new Dictionary<string, string?>(StringComparer.Ordinal),
        string.Empty);
}

public readonly record struct DwaineVfsImageMetadata(
    string DisplayName,
    string Description,
    string TextPreview)
{
    public static readonly DwaineVfsImageMetadata Empty = new(string.Empty, string.Empty, string.Empty);
}

public readonly record struct DwaineVfsProgramData(
    string ProgramId,
    string Source,
    bool Executable,
    bool Native)
{
    public static readonly DwaineVfsProgramData Empty = new(string.Empty, string.Empty, false, false);
}

public sealed record DwaineVfsArchiveEntry(
    string Name,
    DwaineVfsNodeKind Kind,
    DwaineVfsMetadata Metadata,
    string Text,
    IReadOnlyDictionary<string, string?> Fields,
    DwaineVfsUserData UserData,
    DwaineVfsSignalData Signal,
    DwaineVfsImageMetadata Image,
    DwaineVfsProgramData Program,
    string LinkTarget,
    IReadOnlyList<DwaineVfsArchiveEntry> EmbeddedArchiveEntries,
    IReadOnlyList<DwaineVfsArchiveEntry> Children);

internal readonly record struct DwaineVfsLimits(
    int MaxNodes,
    int MaxDepth,
    int MaxNameLength,
    int MaxPathLength,
    int MaxChildrenPerDirectory,
    int MaxLinkDepth,
    int MaxTextCharacters,
    int MaxRecordEntries,
    int MaxRecordCharacters,
    int MaxArchiveEntries,
    int MaxArchiveDepth)
{
    public static DwaineVfsLimits FromComponent(DwaineFileSystemComponent component)
    {
        return new DwaineVfsLimits(
            Math.Clamp(
                component.MaxNodes,
                DwaineFileSystemComponent.MinimumSystemNodes,
                DwaineFileSystemComponent.HardMaxNodes),
            Math.Clamp(
                component.MaxDepth,
                DwaineFileSystemComponent.MinimumSystemDepth,
                DwaineFileSystemComponent.HardMaxDepth),
            Math.Clamp(
                component.MaxNameLength,
                DwaineFileSystemComponent.MinimumSystemNameLength,
                DwaineFileSystemComponent.HardMaxNameLength),
            Math.Clamp(
                component.MaxPathLength,
                DwaineFileSystemComponent.MinimumSystemPathLength,
                DwaineFileSystemComponent.HardMaxPathLength),
            Math.Clamp(
                component.MaxChildrenPerDirectory,
                DwaineFileSystemComponent.MinimumSystemChildren,
                DwaineFileSystemComponent.HardMaxChildrenPerDirectory),
            Math.Clamp(component.MaxLinkDepth, 1, DwaineFileSystemComponent.HardMaxLinkDepth),
            Math.Clamp(
                component.MaxTextCharacters,
                1,
                DwaineFileSystemComponent.HardMaxTextCharacters),
            Math.Clamp(component.MaxRecordEntries, 1, DwaineFileSystemComponent.HardMaxRecordEntries),
            Math.Clamp(
                component.MaxRecordCharacters,
                1,
                DwaineFileSystemComponent.HardMaxRecordCharacters),
            Math.Clamp(component.MaxArchiveEntries, 1, DwaineFileSystemComponent.HardMaxArchiveEntries),
            Math.Clamp(component.MaxArchiveDepth, 1, DwaineFileSystemComponent.HardMaxArchiveDepth));
    }

    public static DwaineVfsLimits FromValues(
        int maxNodes,
        int maxDepth,
        int maxNameLength,
        int maxPathLength,
        int maxChildrenPerDirectory,
        int maxLinkDepth,
        int maxTextCharacters,
        int maxRecordEntries,
        int maxRecordCharacters,
        int maxArchiveEntries,
        int maxArchiveDepth)
    {
        return new DwaineVfsLimits(
            Math.Clamp(maxNodes, 1, DwaineFileSystemComponent.HardMaxNodes),
            Math.Clamp(maxDepth, 1, DwaineFileSystemComponent.HardMaxDepth),
            Math.Clamp(maxNameLength, 1, DwaineFileSystemComponent.HardMaxNameLength),
            Math.Clamp(maxPathLength, 1, DwaineFileSystemComponent.HardMaxPathLength),
            Math.Clamp(maxChildrenPerDirectory, 1, DwaineFileSystemComponent.HardMaxChildrenPerDirectory),
            Math.Clamp(maxLinkDepth, 1, DwaineFileSystemComponent.HardMaxLinkDepth),
            Math.Clamp(maxTextCharacters, 1, DwaineFileSystemComponent.HardMaxTextCharacters),
            Math.Clamp(maxRecordEntries, 1, DwaineFileSystemComponent.HardMaxRecordEntries),
            Math.Clamp(maxRecordCharacters, 1, DwaineFileSystemComponent.HardMaxRecordCharacters),
            Math.Clamp(maxArchiveEntries, 1, DwaineFileSystemComponent.HardMaxArchiveEntries),
            Math.Clamp(maxArchiveDepth, 1, DwaineFileSystemComponent.HardMaxArchiveDepth));
    }
}
