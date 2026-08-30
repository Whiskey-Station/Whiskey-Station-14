// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.FileSystem;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.FileSystem;

internal sealed class DwaineVfsNode
{
    public readonly DwaineVfsNodeId Id;
    public DwaineVfsNodeId? Parent;
    public string Name;
    public readonly DwaineVfsNodeKind Kind;
    public DwaineVfsMetadata Metadata;
    public readonly Dictionary<string, DwaineVfsNodeId> Children = new(StringComparer.OrdinalIgnoreCase);
    public string Text = string.Empty;
    public readonly Dictionary<string, string?> Fields = new(StringComparer.Ordinal);
    public DwaineVfsUserData UserData = DwaineVfsUserData.Empty;
    public DwaineVfsSignalData Signal = DwaineVfsSignalData.Empty;
    public DwaineVfsImageMetadata Image = DwaineVfsImageMetadata.Empty;
    public DwaineVfsProgramData Program = DwaineVfsProgramData.Empty;
    public DwaineVfsNodeHandle? LinkTarget;
    public readonly List<DwaineVfsArchiveEntry> ArchiveEntries = new();

    public DwaineVfsNode(
        DwaineVfsNodeId id,
        DwaineVfsNodeId? parent,
        string name,
        DwaineVfsNodeKind kind,
        DwaineVfsMetadata metadata)
    {
        Id = id;
        Parent = parent;
        Name = name;
        Kind = kind;
        Metadata = metadata;
    }

    public int Size
    {
        get
        {
            return Kind switch
            {
                DwaineVfsNodeKind.Directory => Children.Count,
                DwaineVfsNodeKind.Text or DwaineVfsNodeKind.System => Text.Length,
                DwaineVfsNodeKind.Record => Fields.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0)),
                DwaineVfsNodeKind.UserData => UserData.RegisteredName.Length
                                               + UserData.Assignment.Length
                                               + UserData.AccessTags.Sum(tag => tag.Length),
                DwaineVfsNodeKind.SymbolicLink => 1,
                DwaineVfsNodeKind.Archive => ArchiveEntries.Sum(ArchiveSize),
                DwaineVfsNodeKind.Signal => Signal.EncryptionTag.Length
                                            + Signal.Fields.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0)),
                DwaineVfsNodeKind.ImageMetadata => Image.DisplayName.Length
                                                   + Image.Description.Length
                                                   + Image.TextPreview.Length,
                DwaineVfsNodeKind.Program => Program.ProgramId.Length + Program.Source.Length,
                _ => 0,
            };
        }
    }

    private static int ArchiveSize(DwaineVfsArchiveEntry entry)
    {
        return entry.Name.Length
               + entry.Text.Length
               + entry.Fields.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0))
               + entry.EmbeddedArchiveEntries.Sum(ArchiveSize)
               + entry.Children.Sum(ArchiveSize);
    }
}

/// <summary>
/// A bounded logical volume. PR 07 attaches instances owned by physical media to a mainframe VFS.
/// </summary>
public sealed class DwaineVfsVolume
{
    internal readonly Dictionary<DwaineVfsNodeId, DwaineVfsNode> Nodes = new();
    internal readonly DwaineVfsLimits Limits;
    internal ulong NextNodeId = 2;

    public DwaineVfsVolumeId Id { get; }
    public bool ReadOnly { get; internal set; }
    public bool Dirty { get; internal set; }
    public ulong Revision { get; internal set; }
    public int NodeCount => Nodes.Count;

    internal DwaineVfsVolume(
        DwaineVfsVolumeId id,
        DwaineVfsLimits limits,
        bool readOnly,
        TimeSpan now)
    {
        if (!id.IsValid)
            throw new ArgumentOutOfRangeException(nameof(id));

        Id = id;
        Limits = limits;
        ReadOnly = readOnly;
        var metadata = new DwaineVfsMetadata(
            0,
            0,
            DwaineVfsMode.DefaultDirectory,
            DwaineVfsNodeFlags.System,
            now,
            now);
        Nodes.Add(DwaineVfsNodeId.Root, new DwaineVfsNode(
            DwaineVfsNodeId.Root,
            null,
            string.Empty,
            DwaineVfsNodeKind.Directory,
            metadata));
    }

    internal bool TryAllocateId(out DwaineVfsNodeId nodeId)
    {
        for (var attempts = 0; attempts <= Nodes.Count; attempts++)
        {
            var candidate = NextNodeId++;
            if (NextNodeId == 0)
                NextNodeId = 2;
            if (candidate is 0 or 1)
                continue;

            nodeId = new DwaineVfsNodeId(candidate);
            if (!Nodes.ContainsKey(nodeId))
                return true;
        }

        nodeId = default;
        return false;
    }

    internal void MarkDirty()
    {
        Dirty = true;
        Revision++;
        if (Revision == 0)
            Revision = 1;
    }
}
