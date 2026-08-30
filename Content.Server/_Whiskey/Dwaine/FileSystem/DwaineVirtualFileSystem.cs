// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.FileSystem;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.FileSystem;

/// <summary>
/// Pure, server-owned logical filesystem. It never touches the host filesystem and all traversals are bounded.
/// </summary>
public sealed class DwaineVirtualFileSystem
{
    private readonly DwaineVfsLimits _limits;
    private readonly Dictionary<DwaineVfsVolumeId, DwaineVfsVolume> _volumes = new();
    private readonly Dictionary<DwaineVfsNodeHandle, DwaineVfsVolumeId> _mounts = new();
    private readonly Dictionary<DwaineVfsVolumeId, DwaineVfsNodeHandle> _mountPoints = new();

    public DwaineVfsNodeHandle Root => DwaineVfsNodeHandle.Root;
    public int NodeCount => _volumes.Values.Sum(volume => volume.NodeCount);
    public int AttachedVolumeCount => _mountPoints.Count;

    public DwaineVirtualFileSystem(DwaineFileSystemComponent component, TimeSpan now)
        : this(DwaineVfsLimits.FromComponent(component), now)
    {
    }

    internal DwaineVirtualFileSystem(DwaineVfsLimits limits, TimeSpan now)
    {
        _limits = limits;
        var systemVolume = new DwaineVfsVolume(DwaineVfsVolumeId.System, limits, false, now);
        _volumes.Add(systemVolume.Id, systemVolume);
        BootstrapSystemLayout(now);
        systemVolume.Dirty = false;
        systemVolume.Revision = 0;
    }

    public DwaineVfsResult TryAttachVolume(
        string mountPath,
        DwaineVfsNodeHandle workingDirectory,
        DwaineVfsVolume volume)
    {
        if (volume is null || !volume.Id.IsValid || volume.Id == DwaineVfsVolumeId.System)
            return DwaineVfsResult.MountUnavailable;
        if (_volumes.ContainsKey(volume.Id) || _mountPoints.ContainsKey(volume.Id))
            return DwaineVfsResult.VolumeAlreadyAttached;

        var canonical = TryCanonicalize(mountPath, workingDirectory, out var canonicalPath);
        if (canonical != DwaineVfsResult.Success)
            return canonical;
        foreach (var existingMountPoint in _mounts.Keys)
        {
            if (TryGetPath(existingMountPoint, out var existingPath) == DwaineVfsResult.Success
                && string.Equals(existingPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                return DwaineVfsResult.MountPointBusy;
            }
        }

        var resolve = TryResolve(canonicalPath, Root, out var mountPoint);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        if (mountPoint == Root || mountPoint.Volume != DwaineVfsVolumeId.System)
            return DwaineVfsResult.MountUnavailable;
        if (_mounts.ContainsKey(mountPoint))
            return DwaineVfsResult.MountPointBusy;
        if (!TryGetNode(mountPoint, out _, out var directory)
            || directory.Kind != DwaineVfsNodeKind.Directory)
        {
            return DwaineVfsResult.NotDirectory;
        }

        // Hiding an existing tree behind a mount makes cleanup and authorization ambiguous.
        if (directory.Children.Count != 0)
            return DwaineVfsResult.MountPointBusy;

        _volumes.Add(volume.Id, volume);
        _mounts.Add(mountPoint, volume.Id);
        _mountPoints.Add(volume.Id, mountPoint);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryDetachVolume(DwaineVfsVolumeId volumeId, out DwaineVfsVolume volume)
    {
        volume = null!;
        if (volumeId == DwaineVfsVolumeId.System || !_mountPoints.TryGetValue(volumeId, out var mountPoint))
            return DwaineVfsResult.VolumeNotAttached;
        if (!_volumes.TryGetValue(volumeId, out volume!))
            return DwaineVfsResult.MountUnavailable;

        _mountPoints.Remove(volumeId);
        _mounts.Remove(mountPoint);
        _volumes.Remove(volumeId);
        return DwaineVfsResult.Success;
    }

    public bool IsVolumeAttached(DwaineVfsVolumeId volumeId)
    {
        return _mountPoints.ContainsKey(volumeId);
    }

    public DwaineVfsResult TryGetMountPath(DwaineVfsVolumeId volumeId, out string path)
    {
        path = string.Empty;
        return _mountPoints.TryGetValue(volumeId, out var mountPoint)
            ? TryGetPath(mountPoint, out path)
            : DwaineVfsResult.VolumeNotAttached;
    }

    public DwaineVfsResult TryCanonicalize(
        string? path,
        DwaineVfsNodeHandle workingDirectory,
        out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.Length > _limits.MaxPathLength)
            return DwaineVfsResult.InvalidPath;

        if (path.Any(character => character == '\0' || char.IsControl(character)))
            return DwaineVfsResult.InvalidPath;

        string combined;
        if (path[0] == '/')
        {
            combined = path;
        }
        else
        {
            var cwdResult = TryGetPath(workingDirectory, out var cwd);
            if (cwdResult != DwaineVfsResult.Success)
                return cwdResult;

            combined = cwd == "/" ? $"/{path}" : $"{cwd}/{path}";
        }

        if (combined.Length > _limits.MaxPathLength)
            return DwaineVfsResult.InvalidPath;

        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.None))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    return DwaineVfsResult.RootEscape;

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (!IsValidName(segment))
                return DwaineVfsResult.InvalidName;

            segments.Add(segment);
        }

        canonicalPath = segments.Count == 0 ? "/" : $"/{string.Join('/', segments)}";
        return canonicalPath.Length <= _limits.MaxPathLength
            ? DwaineVfsResult.Success
            : DwaineVfsResult.InvalidPath;
    }

    public DwaineVfsResult TryResolve(
        string? path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsNodeHandle handle,
        bool followFinalLink = true)
    {
        handle = default;
        var canonicalResult = TryCanonicalize(path, workingDirectory, out var canonical);
        if (canonicalResult != DwaineVfsResult.Success)
            return canonicalResult;

        if (!TryGetVolume(DwaineVfsVolumeId.System, out var volume))
            return DwaineVfsResult.MountUnavailable;

        var current = volume.Nodes[DwaineVfsNodeId.Root];
        if (canonical == "/")
        {
            handle = Root;
            return DwaineVfsResult.Success;
        }

        var segments = canonical[1..].Split('/');
        var visitedLinks = new HashSet<DwaineVfsNodeHandle>();
        var followedLinks = 0;
        var linkLimit = volume.Limits.MaxLinkDepth;
        for (var index = 0; index < segments.Length; index++)
        {
            if (current.Kind != DwaineVfsNodeKind.Directory)
                return DwaineVfsResult.NotDirectory;

            if (!current.Children.TryGetValue(segments[index], out var childId)
                || !volume.Nodes.TryGetValue(childId, out current!))
            {
                return DwaineVfsResult.NotFound;
            }

            TryEnterMount(ref volume, ref current);
            linkLimit = Math.Min(linkLimit, volume.Limits.MaxLinkDepth);

            var shouldFollow = current.Kind == DwaineVfsNodeKind.SymbolicLink
                               && (followFinalLink || index < segments.Length - 1);
            while (shouldFollow)
            {
                var linkHandle = new DwaineVfsNodeHandle(volume.Id, current.Id);
                if (!visitedLinks.Add(linkHandle))
                    return DwaineVfsResult.LinkCycle;

                if (++followedLinks > linkLimit)
                    return DwaineVfsResult.LinkDepthLimit;

                if (current.LinkTarget is not { } target
                    || !TryGetNode(target, out volume, out current))
                {
                    return DwaineVfsResult.BrokenLink;
                }

                TryEnterMount(ref volume, ref current);
                linkLimit = Math.Min(linkLimit, volume.Limits.MaxLinkDepth);

                shouldFollow = current.Kind == DwaineVfsNodeKind.SymbolicLink;
            }
        }

        handle = new DwaineVfsNodeHandle(volume.Id, current.Id);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetPath(DwaineVfsNodeHandle handle, out string path)
    {
        path = string.Empty;
        if (!TryGetNode(handle, out var volume, out var node))
            return DwaineVfsResult.InvalidHandle;

        if (volume.Id == DwaineVfsVolumeId.System && node.Id == DwaineVfsNodeId.Root)
        {
            path = "/";
            return DwaineVfsResult.Success;
        }

        var segments = new Stack<string>();
        var visited = new HashSet<DwaineVfsNodeId>();
        while (node.Id != DwaineVfsNodeId.Root)
        {
            if (!visited.Add(node.Id)
                || node.Parent is not { } parentId
                || !volume.Nodes.TryGetValue(parentId, out var parent))
            {
                return DwaineVfsResult.InvalidHandle;
            }

            segments.Push(node.Name);
            node = parent;
        }

        var localPath = string.Join('/', segments);
        if (volume.Id == DwaineVfsVolumeId.System)
        {
            path = $"/{localPath}";
            return DwaineVfsResult.Success;
        }

        if (!_mountPoints.TryGetValue(volume.Id, out var mountPoint))
            return DwaineVfsResult.MountUnavailable;
        var mountResult = TryGetPath(mountPoint, out var mountPath);
        if (mountResult != DwaineVfsResult.Success)
            return mountResult;

        path = localPath.Length == 0 ? mountPath : $"{mountPath}/{localPath}";
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetSnapshot(DwaineVfsNodeHandle handle, out DwaineVfsNodeSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetNode(handle, out var volume, out var node))
            return DwaineVfsResult.InvalidHandle;

        snapshot = Snapshot(volume, node);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryList(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsNodeSnapshot[] entries)
    {
        entries = [];
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;

        if (!TryGetNode(handle, out var volume, out var directory))
            return DwaineVfsResult.InvalidHandle;
        if (directory.Kind != DwaineVfsNodeKind.Directory)
            return DwaineVfsResult.NotDirectory;

        entries = directory.Children.Values
            .Select(id => volume.Nodes[id])
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(child => child.Name, StringComparer.Ordinal)
            .Select(child => Snapshot(volume, child))
            .ToArray();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryCreate(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        DwaineVfsCreateRequest request,
        TimeSpan now,
        out DwaineVfsNodeHandle handle)
    {
        handle = default;
        if (request is null
            || request.Kind is DwaineVfsNodeKind.SymbolicLink or DwaineVfsNodeKind.Archive)
        {
            return DwaineVfsResult.InvalidType;
        }

        var parentResult = TryResolveParent(path, workingDirectory, out var volume, out var parent, out var name);
        if (parentResult != DwaineVfsResult.Success)
            return parentResult;

        var validation = ValidateCreate(volume, parent, name, request);
        if (validation != DwaineVfsResult.Success)
            return validation;

        var metadata = new DwaineVfsMetadata(
            request.Owner,
            request.Group,
            request.Mode ?? DefaultMode(request.Kind),
            request.Flags,
            now,
            now);
        var node = AllocateNode(volume, parent, name, request.Kind, metadata);
        if (node is null)
            return DwaineVfsResult.NodeLimit;

        ApplyCreatePayload(node, request);
        parent.Metadata = parent.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        handle = new DwaineVfsNodeHandle(volume.Id, node.Id);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryCreateDirectory(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now,
        out DwaineVfsNodeHandle handle,
        bool createParents = false)
    {
        handle = default;
        if (!createParents)
        {
            return TryCreate(
                path,
                workingDirectory,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Directory },
                now,
                out handle);
        }

        var canonicalResult = TryCanonicalize(path, workingDirectory, out var canonical);
        if (canonicalResult != DwaineVfsResult.Success)
            return canonicalResult;
        if (canonical == "/")
        {
            handle = Root;
            return DwaineVfsResult.Success;
        }

        var currentPath = string.Empty;
        foreach (var segment in canonical[1..].Split('/'))
        {
            currentPath += $"/{segment}";
            var resolve = TryResolve(currentPath, Root, out handle);
            if (resolve == DwaineVfsResult.Success)
            {
                if (!TryGetNode(handle, out _, out var existing)
                    || existing.Kind != DwaineVfsNodeKind.Directory)
                {
                    return DwaineVfsResult.NotDirectory;
                }

                continue;
            }

            if (resolve != DwaineVfsResult.NotFound)
                return resolve;

            var create = TryCreate(
                currentPath,
                Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Directory },
                now,
                out handle);
            if (create != DwaineVfsResult.Success)
                return create;
        }

        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryCreateLink(
        string path,
        string targetPath,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now,
        out DwaineVfsNodeHandle handle)
    {
        handle = default;
        var targetResult = TryResolve(targetPath, workingDirectory, out var target);
        if (targetResult != DwaineVfsResult.Success)
            return targetResult;

        var parentResult = TryResolveParent(path, workingDirectory, out var volume, out var parent, out var name);
        if (parentResult != DwaineVfsResult.Success)
            return parentResult;
        var validation = ValidateContainerMutation(volume, parent, name, 1);
        if (validation != DwaineVfsResult.Success)
            return validation;

        var metadata = new DwaineVfsMetadata(
            0,
            0,
            DwaineVfsMode.DefaultFile,
            DwaineVfsNodeFlags.None,
            now,
            now);
        var node = AllocateNode(volume, parent, name, DwaineVfsNodeKind.SymbolicLink, metadata);
        if (node is null)
            return DwaineVfsResult.NodeLimit;

        node.LinkTarget = target;
        parent.Metadata = parent.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        handle = new DwaineVfsNodeHandle(volume.Id, node.Id);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryReadText(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out string text)
    {
        text = string.Empty;
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node))
            return DwaineVfsResult.InvalidHandle;

        switch (node.Kind)
        {
            case DwaineVfsNodeKind.Text:
            case DwaineVfsNodeKind.System:
                text = node.Text;
                return DwaineVfsResult.Success;
            case DwaineVfsNodeKind.Program:
                text = node.Program.Source;
                return DwaineVfsResult.Success;
            default:
                return node.Kind == DwaineVfsNodeKind.Directory
                    ? DwaineVfsResult.IsDirectory
                    : DwaineVfsResult.InvalidType;
        }
    }

    public DwaineVfsResult TryWriteText(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        string? text,
        bool append,
        TimeSpan now)
    {
        if (text is null)
            return DwaineVfsResult.InvalidType;

        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out var volume, out var node))
            return DwaineVfsResult.InvalidHandle;
        if (IsReadOnly(volume, node))
            return DwaineVfsResult.ReadOnly;

        var existing = node.Kind switch
        {
            DwaineVfsNodeKind.Text or DwaineVfsNodeKind.System => node.Text,
            DwaineVfsNodeKind.Program when !node.Program.Native => node.Program.Source,
            _ => null,
        };
        if (existing is null)
            return node.Kind == DwaineVfsNodeKind.Directory
                ? DwaineVfsResult.IsDirectory
                : DwaineVfsResult.InvalidType;

        var nextLength = append ? existing.Length + text.Length : text.Length;
        if (nextLength > volume.Limits.MaxTextCharacters)
            return DwaineVfsResult.DataLimit;

        var next = append ? existing + text : text;
        if (node.Kind == DwaineVfsNodeKind.Program)
            node.Program = node.Program with { Source = next };
        else
            node.Text = next;

        node.Metadata = node.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetFields(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out IReadOnlyDictionary<string, string?> fields)
    {
        fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node))
            return DwaineVfsResult.InvalidHandle;

        IReadOnlyDictionary<string, string?> source = node.Kind switch
        {
            DwaineVfsNodeKind.Record => node.Fields,
            DwaineVfsNodeKind.Signal => node.Signal.Fields,
            _ => null!,
        };
        if (source is null)
            return DwaineVfsResult.InvalidType;

        fields = new Dictionary<string, string?>(source, StringComparer.Ordinal);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetUserData(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsUserData userData)
    {
        userData = DwaineVfsUserData.Empty;
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node) || node.Kind != DwaineVfsNodeKind.UserData)
            return DwaineVfsResult.InvalidType;

        userData = node.UserData with { AccessTags = node.UserData.AccessTags.ToArray() };
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetSignal(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsSignalData signal)
    {
        signal = DwaineVfsSignalData.Empty;
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node) || node.Kind != DwaineVfsNodeKind.Signal)
            return DwaineVfsResult.InvalidType;

        signal = node.Signal with
        {
            Fields = new Dictionary<string, string?>(node.Signal.Fields, StringComparer.Ordinal),
        };
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetImageMetadata(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsImageMetadata image)
    {
        image = DwaineVfsImageMetadata.Empty;
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node) || node.Kind != DwaineVfsNodeKind.ImageMetadata)
            return DwaineVfsResult.InvalidType;

        image = node.Image;
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetProgram(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsProgramData program)
    {
        program = DwaineVfsProgramData.Empty;
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node) || node.Kind != DwaineVfsNodeKind.Program)
            return DwaineVfsResult.InvalidType;

        program = node.Program;
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryGetArchiveEntries(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out IReadOnlyList<DwaineVfsArchiveEntry> entries)
    {
        entries = Array.Empty<DwaineVfsArchiveEntry>();
        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out _, out var node) || node.Kind != DwaineVfsNodeKind.Archive)
            return DwaineVfsResult.InvalidType;

        entries = node.ArchiveEntries.Select(CloneArchiveEntry).ToArray();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TrySetField(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        string key,
        string? value,
        TimeSpan now)
    {
        if (!IsValidField(key, value))
            return DwaineVfsResult.InvalidName;

        var result = TryResolve(path, workingDirectory, out var handle);
        if (result != DwaineVfsResult.Success)
            return result;
        if (!TryGetNode(handle, out var volume, out var node))
            return DwaineVfsResult.InvalidHandle;
        if (IsReadOnly(volume, node))
            return DwaineVfsResult.ReadOnly;

        Dictionary<string, string?> fields;
        var signal = false;
        if (node.Kind == DwaineVfsNodeKind.Record)
        {
            // Validate in a copy so a rejected mutation is atomic. Reusing node.Fields here
            // would also clear the source before the commit loop below.
            fields = new Dictionary<string, string?>(node.Fields, StringComparer.Ordinal);
        }
        else if (node.Kind == DwaineVfsNodeKind.Signal)
        {
            fields = new Dictionary<string, string?>(node.Signal.Fields, StringComparer.Ordinal);
            signal = true;
        }
        else
        {
            return DwaineVfsResult.InvalidType;
        }

        if (!fields.ContainsKey(key) && fields.Count >= volume.Limits.MaxRecordEntries)
            return DwaineVfsResult.DataLimit;

        fields[key] = value;
        if (RecordCharacters(fields) > volume.Limits.MaxRecordCharacters)
            return DwaineVfsResult.DataLimit;

        if (signal)
            node.Signal = node.Signal with { Fields = fields };
        else
        {
            node.Fields.Clear();
            foreach (var pair in fields)
                node.Fields.Add(pair.Key, pair.Value);
        }

        node.Metadata = node.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TrySetMetadata(
        DwaineVfsNodeHandle handle,
        ulong owner,
        ulong group,
        DwaineVfsMode mode,
        TimeSpan now)
    {
        if (!TryGetNode(handle, out var volume, out var node))
            return DwaineVfsResult.InvalidHandle;
        if (IsReadOnly(volume, node))
            return DwaineVfsResult.ReadOnly;

        node.Metadata = node.Metadata with
        {
            Owner = owner,
            Group = group,
            Mode = mode,
            ModifiedAt = now,
        };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryDelete(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        bool recursive,
        TimeSpan now)
    {
        var result = TryResolve(path, workingDirectory, out var handle, false);
        if (result != DwaineVfsResult.Success)
            return result;
        if (handle.Node == DwaineVfsNodeId.Root)
            return DwaineVfsResult.RootProtected;
        if (!TryGetNode(handle, out var volume, out var node)
            || node.Parent is not { } parentId
            || !volume.Nodes.TryGetValue(parentId, out var parent))
        {
            return DwaineVfsResult.InvalidHandle;
        }

        if (IsReadOnly(volume, node) || IsReadOnly(volume, parent))
            return DwaineVfsResult.ReadOnly;
        if (node.Kind == DwaineVfsNodeKind.Directory && node.Children.Count > 0 && !recursive)
            return DwaineVfsResult.DirectoryNotEmpty;

        parent.Children.Remove(node.Name);
        DeleteSubtree(volume, node);
        parent.Metadata = parent.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryRename(
        string path,
        string newName,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        if (!IsValidName(newName))
            return DwaineVfsResult.InvalidName;

        var result = TryResolve(path, workingDirectory, out var handle, false);
        if (result != DwaineVfsResult.Success)
            return result;
        if (handle.Node == DwaineVfsNodeId.Root)
            return DwaineVfsResult.RootProtected;
        if (!TryGetNode(handle, out var volume, out var node)
            || node.Parent is not { } parentId
            || !volume.Nodes.TryGetValue(parentId, out var parent))
        {
            return DwaineVfsResult.InvalidHandle;
        }

        if (IsReadOnly(volume, node) || IsReadOnly(volume, parent))
            return DwaineVfsResult.ReadOnly;
        if (newName.Length > volume.Limits.MaxNameLength)
            return DwaineVfsResult.InvalidName;
        if (parent.Children.TryGetValue(newName, out var existingId) && existingId != node.Id)
            return DwaineVfsResult.AlreadyExists;

        if (node.Name == newName)
            return DwaineVfsResult.Success;
        if (!FitsSubtreePath(volume, parent, newName, volume, node))
            return DwaineVfsResult.InvalidPath;

        parent.Children.Remove(node.Name);
        node.Name = newName;
        parent.Children.Add(node.Name, node.Id);
        node.Metadata = node.Metadata with { ModifiedAt = now };
        parent.Metadata = parent.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryMove(
        string sourcePath,
        string destinationPath,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var sourceResult = TryResolve(sourcePath, workingDirectory, out var sourceHandle, false);
        if (sourceResult != DwaineVfsResult.Success)
            return sourceResult;
        if (sourceHandle.Node == DwaineVfsNodeId.Root)
            return DwaineVfsResult.RootProtected;
        if (!TryGetNode(sourceHandle, out var sourceVolume, out var source)
            || source.Parent is not { } oldParentId
            || !sourceVolume.Nodes.TryGetValue(oldParentId, out var oldParent))
        {
            return DwaineVfsResult.InvalidHandle;
        }

        var destinationResult = TryResolveParent(
            destinationPath,
            workingDirectory,
            out var destinationVolume,
            out var destinationParent,
            out var destinationName);
        if (destinationResult != DwaineVfsResult.Success)
            return destinationResult;
        if (sourceVolume.Id != destinationVolume.Id)
            return DwaineVfsResult.CrossVolumeMoveDenied;
        if (IsReadOnly(sourceVolume, source)
            || IsReadOnly(sourceVolume, oldParent)
            || IsReadOnly(destinationVolume, destinationParent))
        {
            return DwaineVfsResult.ReadOnly;
        }

        if (destinationParent.Children.ContainsKey(destinationName))
            return DwaineVfsResult.AlreadyExists;
        if (destinationName.Length > destinationVolume.Limits.MaxNameLength)
            return DwaineVfsResult.InvalidName;
        if (oldParent.Id != destinationParent.Id
            && destinationParent.Children.Count >= destinationVolume.Limits.MaxChildrenPerDirectory)
        {
            return DwaineVfsResult.ChildLimit;
        }
        var destinationValidation = ValidateDestinationPath(destinationVolume, destinationParent, destinationName);
        if (destinationValidation != DwaineVfsResult.Success)
            return destinationValidation;
        if (source.Kind == DwaineVfsNodeKind.Directory && IsAncestor(sourceVolume, source.Id, destinationParent.Id))
            return DwaineVfsResult.DestinationInsideSource;
        if (GetDepth(destinationVolume, destinationParent) + SubtreeHeight(sourceVolume, source) + 1
            > destinationVolume.Limits.MaxDepth)
            return DwaineVfsResult.DepthLimit;
        if (!FitsSubtreePath(destinationVolume, destinationParent, destinationName, sourceVolume, source))
            return DwaineVfsResult.InvalidPath;

        oldParent.Children.Remove(source.Name);
        source.Parent = destinationParent.Id;
        source.Name = destinationName;
        destinationParent.Children.Add(source.Name, source.Id);
        source.Metadata = source.Metadata with { ModifiedAt = now };
        oldParent.Metadata = oldParent.Metadata with { ModifiedAt = now };
        destinationParent.Metadata = destinationParent.Metadata with { ModifiedAt = now };
        sourceVolume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryCopy(
        string sourcePath,
        string destinationPath,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now,
        out DwaineVfsNodeHandle copyHandle)
    {
        copyHandle = default;
        var sourceResult = TryResolve(sourcePath, workingDirectory, out var sourceHandle, false);
        if (sourceResult != DwaineVfsResult.Success)
            return sourceResult;
        if (!TryGetNode(sourceHandle, out var sourceVolume, out var source))
            return DwaineVfsResult.InvalidHandle;
        if ((source.Metadata.Flags & DwaineVfsNodeFlags.Virtual) != 0)
            return DwaineVfsResult.ReadOnly;

        var destinationResult = TryResolveParent(
            destinationPath,
            workingDirectory,
            out var destinationVolume,
            out var destinationParent,
            out var destinationName);
        if (destinationResult != DwaineVfsResult.Success)
            return destinationResult;
        if (sourceVolume.Id == destinationVolume.Id
            && source.Kind == DwaineVfsNodeKind.Directory
            && IsAncestor(sourceVolume, source.Id, destinationParent.Id))
        {
            return DwaineVfsResult.DestinationInsideSource;
        }

        var containerResult = ValidateContainerMutation(
            destinationVolume,
            destinationParent,
            destinationName,
            CountSubtree(sourceVolume, source));
        if (containerResult != DwaineVfsResult.Success)
            return containerResult;
        var subtreeValidation = ValidateSubtreeForDestination(sourceVolume, source, destinationVolume.Limits);
        if (subtreeValidation != DwaineVfsResult.Success)
            return subtreeValidation;
        if (GetDepth(destinationVolume, destinationParent) + SubtreeHeight(sourceVolume, source) + 1
            > destinationVolume.Limits.MaxDepth)
            return DwaineVfsResult.DepthLimit;
        if (!FitsSubtreePath(destinationVolume, destinationParent, destinationName, sourceVolume, source))
            return DwaineVfsResult.InvalidPath;

        var created = new List<DwaineVfsNodeId>();
        var clone = CloneSubtree(
            sourceVolume,
            source,
            destinationVolume,
            destinationParent,
            destinationName,
            now,
            created);
        if (clone is null)
        {
            foreach (var id in created)
                destinationVolume.Nodes.Remove(id);
            destinationParent.Children.Remove(destinationName);
            return DwaineVfsResult.InvalidHandle;
        }

        destinationParent.Metadata = destinationParent.Metadata with { ModifiedAt = now };
        destinationVolume.MarkDirty();
        copyHandle = new DwaineVfsNodeHandle(destinationVolume.Id, clone.Id);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryCreateArchive(
        string sourcePath,
        string archivePath,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now,
        out DwaineVfsNodeHandle archiveHandle)
    {
        archiveHandle = default;
        var sourceResult = TryResolve(sourcePath, workingDirectory, out var sourceHandle, false);
        if (sourceResult != DwaineVfsResult.Success)
            return sourceResult;
        if (!TryGetNode(sourceHandle, out var sourceVolume, out var source))
            return DwaineVfsResult.InvalidHandle;

        var count = 0;
        var archiveResult = BuildArchiveEntry(sourceVolume, source, 0, ref count, out var entry);
        if (archiveResult != DwaineVfsResult.Success)
            return archiveResult;

        var parentResult = TryResolveParent(archivePath, workingDirectory, out var volume, out var parent, out var name);
        if (parentResult != DwaineVfsResult.Success)
            return parentResult;
        if (source.Kind == DwaineVfsNodeKind.Directory
            && sourceVolume.Id == volume.Id
            && (source.Id == parent.Id || IsAncestor(sourceVolume, source.Id, parent.Id)))
        {
            return DwaineVfsResult.DestinationInsideSource;
        }

        var validation = ValidateContainerMutation(volume, parent, name, 1);
        if (validation != DwaineVfsResult.Success)
            return validation;
        if (count > volume.Limits.MaxArchiveEntries || ArchiveHeight(entry) > volume.Limits.MaxArchiveDepth)
            return DwaineVfsResult.DataLimit;

        var metadata = new DwaineVfsMetadata(
            0,
            0,
            DwaineVfsMode.DefaultFile,
            DwaineVfsNodeFlags.None,
            now,
            now);
        var archive = AllocateNode(volume, parent, name, DwaineVfsNodeKind.Archive, metadata);
        if (archive is null)
            return DwaineVfsResult.NodeLimit;
        archive.ArchiveEntries.Add(entry);
        parent.Metadata = parent.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        archiveHandle = new DwaineVfsNodeHandle(volume.Id, archive.Id);
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryExtractArchive(
        string archivePath,
        string destinationDirectoryPath,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var archiveResult = TryResolve(archivePath, workingDirectory, out var archiveHandle);
        if (archiveResult != DwaineVfsResult.Success)
            return archiveResult;
        if (!TryGetNode(archiveHandle, out _, out var archive) || archive.Kind != DwaineVfsNodeKind.Archive)
            return DwaineVfsResult.InvalidType;

        var destinationResult = TryResolve(destinationDirectoryPath, workingDirectory, out var destinationHandle);
        if (destinationResult != DwaineVfsResult.Success)
            return destinationResult;
        if (!TryGetNode(destinationHandle, out var volume, out var destination)
            || destination.Kind != DwaineVfsNodeKind.Directory)
        {
            return DwaineVfsResult.NotDirectory;
        }

        if (IsReadOnly(volume, destination))
            return DwaineVfsResult.ReadOnly;
        if (archive.ArchiveEntries.Sum(CountArchiveEntries) > volume.Limits.MaxArchiveEntries)
            return DwaineVfsResult.DataLimit;
        var total = archive.ArchiveEntries.Sum(CountMaterializedArchiveNodes);
        if (volume.NodeCount + total > volume.Limits.MaxNodes)
            return DwaineVfsResult.NodeLimit;
        if (destination.Children.Count + archive.ArchiveEntries.Count > volume.Limits.MaxChildrenPerDirectory)
            return DwaineVfsResult.ChildLimit;
        foreach (var entry in archive.ArchiveEntries)
        {
            if (destination.Children.ContainsKey(entry.Name))
                return DwaineVfsResult.AlreadyExists;
            var entryValidation = ValidateArchiveForDestination(entry, volume.Limits);
            if (entryValidation != DwaineVfsResult.Success)
                return entryValidation;
            if (GetLocalPathLength(volume, destination) + 1 + MaxArchivePathLength(entry)
                > volume.Limits.MaxPathLength)
            {
                return DwaineVfsResult.InvalidPath;
            }
            if (GetDepth(volume, destination) + MaterializedArchiveHeight(entry) + 1 > volume.Limits.MaxDepth)
                return DwaineVfsResult.DepthLimit;
        }

        var created = new List<DwaineVfsNodeId>();
        foreach (var entry in archive.ArchiveEntries)
        {
            if (MaterializeArchiveEntry(volume, destination, entry, now, created) is not null)
                continue;

            foreach (var id in created.ToArray())
            {
                if (volume.Nodes.TryGetValue(id, out var node) && node.Parent is { } parentId
                    && volume.Nodes.TryGetValue(parentId, out var parent))
                {
                    parent.Children.Remove(node.Name);
                }

                volume.Nodes.Remove(id);
            }

            return DwaineVfsResult.BrokenLink;
        }

        destination.Metadata = destination.Metadata with { ModifiedAt = now };
        volume.MarkDirty();
        return DwaineVfsResult.Success;
    }

    public bool IsDirectory(DwaineVfsNodeHandle handle)
    {
        return TryGetNode(handle, out _, out var node) && node.Kind == DwaineVfsNodeKind.Directory;
    }

    internal void UpsertProcessView(ulong processId, string state, ulong generation, TimeSpan now)
    {
        if (TryResolve("/proc", Root, out var procHandle) != DwaineVfsResult.Success
            || !TryGetNode(procHandle, out var volume, out var proc))
        {
            return;
        }

        var name = processId.ToString();
        if (!proc.Children.TryGetValue(name, out var nodeId) || !volume.Nodes.TryGetValue(nodeId, out var node))
        {
            var metadata = new DwaineVfsMetadata(
                0,
                0,
                DwaineVfsMode.ReadOnlyFile,
                DwaineVfsNodeFlags.ReadOnly | DwaineVfsNodeFlags.Virtual | DwaineVfsNodeFlags.System,
                now,
                now);
            node = AllocateNode(volume, proc, name, DwaineVfsNodeKind.Record, metadata);
            if (node is null)
                return;
        }

        node.Fields["pid"] = name;
        node.Fields["state"] = state;
        node.Fields["generation"] = generation.ToString();
        node.Metadata = node.Metadata with { ModifiedAt = now };
    }

    internal void RemoveProcessView(ulong processId)
    {
        if (TryResolve("/proc", Root, out var procHandle) != DwaineVfsResult.Success
            || !TryGetNode(procHandle, out var volume, out var proc))
        {
            return;
        }

        var name = processId.ToString();
        if (!proc.Children.Remove(name, out var nodeId))
            return;

        volume.Nodes.Remove(nodeId);
    }

    internal void ClearProcessViews()
    {
        if (TryResolve("/proc", Root, out var procHandle) != DwaineVfsResult.Success
            || !TryGetNode(procHandle, out var volume, out var proc))
        {
            return;
        }

        foreach (var nodeId in proc.Children.Values)
            volume.Nodes.Remove(nodeId);
        proc.Children.Clear();
    }

    private void BootstrapSystemLayout(TimeSpan now)
    {
        string[] paths =
        [
            "/sys",
            "/sys/drvr",
            "/sys/srv",
            "/bin",
            "/conf",
            "/usr",
            "/home",
            "/dev",
            "/mnt",
            "/proc",
            "/tmp",
            "/var",
            "/etc",
            "/etc/mail",
        ];

        foreach (var path in paths)
        {
            TryCreateDirectory(path, Root, now, out var handle, true);
            if (!TryGetNode(handle, out _, out var node))
                continue;

            var flags = DwaineVfsNodeFlags.System;
            if (path == "/proc")
                flags |= DwaineVfsNodeFlags.ReadOnly | DwaineVfsNodeFlags.Virtual;
            node.Metadata = node.Metadata with { Flags = flags };
        }
    }

    private DwaineVfsResult TryResolveParent(
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsVolume volume,
        out DwaineVfsNode parent,
        out string name)
    {
        volume = null!;
        parent = null!;
        name = string.Empty;
        var canonicalResult = TryCanonicalize(path, workingDirectory, out var canonical);
        if (canonicalResult != DwaineVfsResult.Success)
            return canonicalResult;
        if (canonical == "/")
            return DwaineVfsResult.RootProtected;

        var separator = canonical.LastIndexOf('/');
        name = canonical[(separator + 1)..];
        var parentPath = separator == 0 ? "/" : canonical[..separator];
        var parentResult = TryResolve(parentPath, Root, out var parentHandle);
        if (parentResult != DwaineVfsResult.Success)
            return parentResult;
        if (!TryGetNode(parentHandle, out volume, out parent))
            return DwaineVfsResult.InvalidHandle;
        return parent.Kind == DwaineVfsNodeKind.Directory
            ? DwaineVfsResult.Success
            : DwaineVfsResult.NotDirectory;
    }

    private DwaineVfsResult ValidateCreate(
        DwaineVfsVolume volume,
        DwaineVfsNode parent,
        string name,
        DwaineVfsCreateRequest request)
    {
        var container = ValidateContainerMutation(volume, parent, name, 1);
        if (container != DwaineVfsResult.Success)
            return container;

        var validPayload = request.Kind switch
        {
            DwaineVfsNodeKind.Text or DwaineVfsNodeKind.System => request.Text is not null
                && request.Text.Length <= volume.Limits.MaxTextCharacters,
            DwaineVfsNodeKind.Record => request.Fields is null || ValidateFields(request.Fields, volume.Limits),
            DwaineVfsNodeKind.UserData => ValidateUserData(request.UserData, volume.Limits),
            DwaineVfsNodeKind.Signal => ValidateSignal(request.Signal, volume.Limits),
            DwaineVfsNodeKind.ImageMetadata => ValidateImage(request.Image, volume.Limits),
            DwaineVfsNodeKind.Program => ValidateProgram(request.Program, volume.Limits),
            DwaineVfsNodeKind.Directory => true,
            _ => false,
        };

        return validPayload ? DwaineVfsResult.Success : DwaineVfsResult.DataLimit;
    }

    private DwaineVfsResult ValidateContainerMutation(
        DwaineVfsVolume volume,
        DwaineVfsNode parent,
        string name,
        int nodesRequired)
    {
        if (!IsValidName(name) || name.Length > volume.Limits.MaxNameLength)
            return DwaineVfsResult.InvalidName;
        if (parent.Kind != DwaineVfsNodeKind.Directory)
            return DwaineVfsResult.NotDirectory;
        if (IsReadOnly(volume, parent))
            return DwaineVfsResult.ReadOnly;
        if (parent.Children.ContainsKey(name))
            return DwaineVfsResult.AlreadyExists;
        if (parent.Children.Count >= volume.Limits.MaxChildrenPerDirectory)
            return DwaineVfsResult.ChildLimit;
        if (nodesRequired > volume.Limits.MaxNodes - volume.NodeCount)
            return DwaineVfsResult.NodeLimit;
        if (GetDepth(volume, parent) + 1 > volume.Limits.MaxDepth)
            return DwaineVfsResult.DepthLimit;
        var pathValidation = ValidateDestinationPath(volume, parent, name);
        if (pathValidation != DwaineVfsResult.Success)
            return pathValidation;
        return DwaineVfsResult.Success;
    }

    private DwaineVfsNode? AllocateNode(
        DwaineVfsVolume volume,
        DwaineVfsNode parent,
        string name,
        DwaineVfsNodeKind kind,
        DwaineVfsMetadata metadata)
    {
        if (volume.NodeCount >= volume.Limits.MaxNodes || !volume.TryAllocateId(out var id))
            return null;

        var node = new DwaineVfsNode(id, parent.Id, name, kind, metadata);
        volume.Nodes.Add(id, node);
        parent.Children.Add(name, id);
        return node;
    }

    private static void ApplyCreatePayload(DwaineVfsNode node, DwaineVfsCreateRequest request)
    {
        switch (node.Kind)
        {
            case DwaineVfsNodeKind.Text:
            case DwaineVfsNodeKind.System:
                node.Text = request.Text;
                break;
            case DwaineVfsNodeKind.Record:
                if (request.Fields is not null)
                {
                    foreach (var pair in request.Fields)
                        node.Fields.Add(pair.Key, pair.Value);
                }
                break;
            case DwaineVfsNodeKind.UserData:
                node.UserData = request.UserData with { AccessTags = request.UserData.AccessTags.ToArray() };
                break;
            case DwaineVfsNodeKind.Signal:
                node.Signal = request.Signal with
                {
                    Fields = new Dictionary<string, string?>(request.Signal.Fields, StringComparer.Ordinal),
                };
                break;
            case DwaineVfsNodeKind.ImageMetadata:
                node.Image = request.Image;
                break;
            case DwaineVfsNodeKind.Program:
                node.Program = request.Program;
                break;
        }
    }

    private DwaineVfsNode? CloneSubtree(
        DwaineVfsVolume sourceVolume,
        DwaineVfsNode source,
        DwaineVfsVolume destinationVolume,
        DwaineVfsNode destinationParent,
        string name,
        TimeSpan now,
        List<DwaineVfsNodeId> created)
    {
        var metadata = source.Metadata with { CreatedAt = now, ModifiedAt = now };
        var clone = AllocateNode(destinationVolume, destinationParent, name, source.Kind, metadata);
        if (clone is null)
            return null;
        created.Add(clone.Id);
        CopyPayload(source, clone);

        foreach (var childId in source.Children.Values)
        {
            var child = sourceVolume.Nodes[childId];
            if (CloneSubtree(
                    sourceVolume,
                    child,
                    destinationVolume,
                    clone,
                    child.Name,
                    now,
                    created) is null)
            {
                return null;
            }
        }

        return clone;
    }

    private static void CopyPayload(DwaineVfsNode source, DwaineVfsNode destination)
    {
        destination.Text = source.Text;
        foreach (var pair in source.Fields)
            destination.Fields.Add(pair.Key, pair.Value);
        destination.UserData = source.UserData with { AccessTags = source.UserData.AccessTags.ToArray() };
        destination.Signal = source.Signal with
        {
            Fields = new Dictionary<string, string?>(source.Signal.Fields, StringComparer.Ordinal),
        };
        destination.Image = source.Image;
        destination.Program = source.Program;
        destination.LinkTarget = source.LinkTarget;
        destination.ArchiveEntries.AddRange(source.ArchiveEntries.Select(CloneArchiveEntry));
    }

    private DwaineVfsResult BuildArchiveEntry(
        DwaineVfsVolume volume,
        DwaineVfsNode node,
        int depth,
        ref int count,
        out DwaineVfsArchiveEntry entry)
    {
        entry = null!;
        if (depth > volume.Limits.MaxArchiveDepth)
            return DwaineVfsResult.DepthLimit;
        if (++count > volume.Limits.MaxArchiveEntries)
            return DwaineVfsResult.NodeLimit;
        if ((node.Metadata.Flags & DwaineVfsNodeFlags.Virtual) != 0)
            return DwaineVfsResult.ReadOnly;

        var embeddedEntries = node.ArchiveEntries.Select(CloneArchiveEntry).ToArray();
        var embeddedCount = embeddedEntries.Sum(CountArchiveEntries);
        if (embeddedCount > volume.Limits.MaxArchiveEntries - count)
            return DwaineVfsResult.NodeLimit;
        count += embeddedCount;

        var children = new List<DwaineVfsArchiveEntry>();
        foreach (var childId in node.Children.Values)
        {
            var result = BuildArchiveEntry(volume, volume.Nodes[childId], depth + 1, ref count, out var child);
            if (result != DwaineVfsResult.Success)
                return result;
            children.Add(child);
        }

        var linkTarget = string.Empty;
        if (node.Kind == DwaineVfsNodeKind.SymbolicLink)
        {
            if (node.LinkTarget is not { } target || TryGetPath(target, out linkTarget) != DwaineVfsResult.Success)
                return DwaineVfsResult.BrokenLink;
        }

        entry = new DwaineVfsArchiveEntry(
            node.Name,
            node.Kind,
            node.Metadata,
            node.Text,
            new Dictionary<string, string?>(node.Fields, StringComparer.Ordinal),
            node.UserData with { AccessTags = node.UserData.AccessTags.ToArray() },
            node.Signal with
            {
                Fields = new Dictionary<string, string?>(node.Signal.Fields, StringComparer.Ordinal),
            },
            node.Image,
            node.Program,
            linkTarget,
            embeddedEntries,
            children);
        return DwaineVfsResult.Success;
    }

    private DwaineVfsNode? MaterializeArchiveEntry(
        DwaineVfsVolume volume,
        DwaineVfsNode parent,
        DwaineVfsArchiveEntry entry,
        TimeSpan now,
        List<DwaineVfsNodeId> created)
    {
        var metadata = entry.Metadata with { CreatedAt = now, ModifiedAt = now };
        var node = AllocateNode(volume, parent, entry.Name, entry.Kind, metadata);
        if (node is null)
            return null;
        created.Add(node.Id);
        node.Text = entry.Text;
        foreach (var pair in entry.Fields)
            node.Fields.Add(pair.Key, pair.Value);
        node.UserData = entry.UserData with { AccessTags = entry.UserData.AccessTags.ToArray() };
        node.Signal = entry.Signal with
        {
            Fields = new Dictionary<string, string?>(entry.Signal.Fields, StringComparer.Ordinal),
        };
        node.Image = entry.Image;
        node.Program = entry.Program;
        node.ArchiveEntries.AddRange(entry.EmbeddedArchiveEntries.Select(CloneArchiveEntry));
        if (entry.Kind == DwaineVfsNodeKind.SymbolicLink)
        {
            if (TryResolve(entry.LinkTarget, Root, out var target) != DwaineVfsResult.Success)
                return null;
            node.LinkTarget = target;
        }

        foreach (var child in entry.Children)
        {
            if (MaterializeArchiveEntry(volume, node, child, now, created) is null)
                return null;
        }

        return node;
    }

    private static DwaineVfsArchiveEntry CloneArchiveEntry(DwaineVfsArchiveEntry entry)
    {
        return entry with
        {
            Fields = new Dictionary<string, string?>(entry.Fields, StringComparer.Ordinal),
            UserData = entry.UserData with { AccessTags = entry.UserData.AccessTags.ToArray() },
            Signal = entry.Signal with
            {
                Fields = new Dictionary<string, string?>(entry.Signal.Fields, StringComparer.Ordinal),
            },
            EmbeddedArchiveEntries = entry.EmbeddedArchiveEntries.Select(CloneArchiveEntry).ToArray(),
            Children = entry.Children.Select(CloneArchiveEntry).ToArray(),
        };
    }

    private static DwaineVfsResult ValidateSubtreeForDestination(
        DwaineVfsVolume sourceVolume,
        DwaineVfsNode source,
        DwaineVfsLimits limits)
    {
        if (source.Name.Length > limits.MaxNameLength
            || source.Children.Count > limits.MaxChildrenPerDirectory)
        {
            return DwaineVfsResult.DataLimit;
        }

        var payload = ValidateNodePayloadForDestination(source, limits);
        if (payload != DwaineVfsResult.Success)
            return payload;

        foreach (var childId in source.Children.Values)
        {
            var result = ValidateSubtreeForDestination(sourceVolume, sourceVolume.Nodes[childId], limits);
            if (result != DwaineVfsResult.Success)
                return result;
        }

        return DwaineVfsResult.Success;
    }

    private static DwaineVfsResult ValidateNodePayloadForDestination(DwaineVfsNode node, DwaineVfsLimits limits)
    {
        var valid = node.Kind switch
        {
            DwaineVfsNodeKind.Text or DwaineVfsNodeKind.System => node.Text.Length <= limits.MaxTextCharacters,
            DwaineVfsNodeKind.Record => ValidateFields(node.Fields, limits),
            DwaineVfsNodeKind.UserData => ValidateUserData(node.UserData, limits),
            DwaineVfsNodeKind.Signal => ValidateSignal(node.Signal, limits),
            DwaineVfsNodeKind.ImageMetadata => ValidateImage(node.Image, limits),
            DwaineVfsNodeKind.Program => ValidateProgram(node.Program, limits),
            DwaineVfsNodeKind.Archive => node.ArchiveEntries.Sum(CountArchiveEntries) <= limits.MaxArchiveEntries
                                         && node.ArchiveEntries.All(entry =>
                                             ValidateArchiveForDestination(entry, limits)
                                             == DwaineVfsResult.Success),
            DwaineVfsNodeKind.Directory or DwaineVfsNodeKind.SymbolicLink => true,
            _ => false,
        };
        return valid ? DwaineVfsResult.Success : DwaineVfsResult.DataLimit;
    }

    private static DwaineVfsResult ValidateArchiveForDestination(
        DwaineVfsArchiveEntry entry,
        DwaineVfsLimits limits)
    {
        if (entry.Name.Length > limits.MaxNameLength
            || entry.Children.Count > limits.MaxChildrenPerDirectory
            || CountArchiveEntries(entry) > limits.MaxArchiveEntries
            || ArchiveHeight(entry) > limits.MaxArchiveDepth)
        {
            return DwaineVfsResult.DataLimit;
        }

        var valid = entry.Kind switch
        {
            DwaineVfsNodeKind.Text or DwaineVfsNodeKind.System => entry.Text.Length <= limits.MaxTextCharacters,
            DwaineVfsNodeKind.Record => ValidateFields(entry.Fields, limits),
            DwaineVfsNodeKind.UserData => ValidateUserData(entry.UserData, limits),
            DwaineVfsNodeKind.Signal => ValidateSignal(entry.Signal, limits),
            DwaineVfsNodeKind.ImageMetadata => ValidateImage(entry.Image, limits),
            DwaineVfsNodeKind.Program => ValidateProgram(entry.Program, limits),
            DwaineVfsNodeKind.Archive => entry.EmbeddedArchiveEntries.All(child =>
                ValidateArchiveForDestination(child, limits) == DwaineVfsResult.Success),
            DwaineVfsNodeKind.Directory or DwaineVfsNodeKind.SymbolicLink => true,
            _ => false,
        };
        if (!valid)
            return DwaineVfsResult.DataLimit;

        foreach (var child in entry.Children)
        {
            var result = ValidateArchiveForDestination(child, limits);
            if (result != DwaineVfsResult.Success)
                return result;
        }

        return DwaineVfsResult.Success;
    }

    private static bool ValidateFields(IReadOnlyDictionary<string, string?> fields, DwaineVfsLimits limits)
    {
        return fields.Count <= limits.MaxRecordEntries
               && RecordCharacters(fields) <= limits.MaxRecordCharacters
               && fields.All(pair => IsValidField(pair.Key, pair.Value));
    }

    private static bool ValidateUserData(DwaineVfsUserData userData, DwaineVfsLimits limits)
    {
        if (userData.RegisteredName is null
            || userData.Assignment is null
            || userData.AccessTags is null
            || userData.RegisteredName.Length > 128
            || userData.Assignment.Length > 128
            || userData.AccessTags.Count > limits.MaxRecordEntries)
        {
            return false;
        }

        return userData.AccessTags.All(tag => tag is not null
            && tag.Length is > 0 and <= 64
            && tag.All(character => !char.IsControl(character)));
    }

    private static bool ValidateSignal(DwaineVfsSignalData signal, DwaineVfsLimits limits)
    {
        return signal.EncryptionTag is not null
               && signal.Fields is not null
               && signal.EncryptionTag.Length <= 128
               && ValidateFields(signal.Fields, limits);
    }

    private static bool ValidateImage(DwaineVfsImageMetadata image, DwaineVfsLimits limits)
    {
        return image.DisplayName is not null
               && image.Description is not null
               && image.TextPreview is not null
               && image.DisplayName.Length <= 128
               && image.Description.Length <= 2048
               && image.TextPreview.Length <= limits.MaxTextCharacters;
    }

    private static bool ValidateProgram(DwaineVfsProgramData program, DwaineVfsLimits limits)
    {
        return program.ProgramId is not null
               && program.Source is not null
               && program.ProgramId.Length is > 0 and <= 64
               && program.Source.Length <= limits.MaxTextCharacters
               && program.ProgramId.All(character => character is >= 'a' and <= 'z'
                   or >= '0' and <= '9'
                   or '.' or '-' or '_');
    }

    private bool IsValidName(string name)
    {
        if (name.Length is 0 || name.Length > _limits.MaxNameLength || name is "." or "..")
            return false;

        return name.All(character => character != '/'
                                     && character != '\\'
                                     && character != '\0'
                                     && !char.IsControl(character));
    }

    private static bool IsValidField(string key, string? value)
    {
        return key.Length is > 0 and <= 128
               && key.All(character => character != '\0' && !char.IsControl(character))
               && (value is null || value.Length <= 8192 && value.All(character => character != '\0'));
    }

    private static int RecordCharacters(IReadOnlyDictionary<string, string?> fields)
    {
        return fields.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0));
    }

    private static DwaineVfsMode DefaultMode(DwaineVfsNodeKind kind)
    {
        return kind == DwaineVfsNodeKind.Directory
            ? DwaineVfsMode.DefaultDirectory
            : DwaineVfsMode.DefaultFile;
    }

    private static bool IsReadOnly(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        return volume.ReadOnly || (node.Metadata.Flags & DwaineVfsNodeFlags.ReadOnly) != 0;
    }

    private static DwaineVfsResult ValidateDestinationPath(
        DwaineVfsVolume volume,
        DwaineVfsNode parent,
        string name)
    {
        var parentLength = GetLocalPathLength(volume, parent);
        return parentLength != int.MaxValue
               && (long) parentLength + 1 + name.Length <= volume.Limits.MaxPathLength
            ? DwaineVfsResult.Success
            : DwaineVfsResult.InvalidPath;
    }

    private static int GetLocalPathLength(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        if (node.Id == DwaineVfsNodeId.Root)
            return 0;

        var length = 0;
        var visited = new HashSet<DwaineVfsNodeId>();
        while (node.Id != DwaineVfsNodeId.Root)
        {
            if (!visited.Add(node.Id)
                || node.Parent is not { } parentId
                || !volume.Nodes.TryGetValue(parentId, out var parent))
            {
                return int.MaxValue;
            }

            length += node.Name.Length + 1;
            node = parent;
        }

        return length;
    }

    private static bool FitsSubtreePath(
        DwaineVfsVolume destinationVolume,
        DwaineVfsNode destinationParent,
        string destinationName,
        DwaineVfsVolume sourceVolume,
        DwaineVfsNode source)
    {
        var parentLength = GetLocalPathLength(destinationVolume, destinationParent);
        if (parentLength == int.MaxValue)
            return false;

        return (long) parentLength
               + 1
               + destinationName.Length
               + MaxDescendantPathSuffix(sourceVolume, source) <= destinationVolume.Limits.MaxPathLength;
    }

    private static int MaxDescendantPathSuffix(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        if (node.Children.Count == 0)
            return 0;

        return node.Children.Values.Max(childId =>
        {
            var child = volume.Nodes[childId];
            return 1 + child.Name.Length + MaxDescendantPathSuffix(volume, child);
        });
    }

    private void TryEnterMount(ref DwaineVfsVolume volume, ref DwaineVfsNode node)
    {
        var handle = new DwaineVfsNodeHandle(volume.Id, node.Id);
        if (!_mounts.TryGetValue(handle, out var mountedVolumeId)
            || !_volumes.TryGetValue(mountedVolumeId, out var mountedVolume))
        {
            return;
        }

        volume = mountedVolume;
        node = mountedVolume.Nodes[DwaineVfsNodeId.Root];
    }

    private bool TryGetVolume(DwaineVfsVolumeId id, out DwaineVfsVolume volume)
    {
        return _volumes.TryGetValue(id, out volume!);
    }

    private bool TryGetNode(
        DwaineVfsNodeHandle handle,
        out DwaineVfsVolume volume,
        out DwaineVfsNode node)
    {
        if (!_volumes.TryGetValue(handle.Volume, out volume!)
            || !volume.Nodes.TryGetValue(handle.Node, out node!))
        {
            volume = null!;
            node = null!;
            return false;
        }

        return true;
    }

    private static DwaineVfsNodeSnapshot Snapshot(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        var handle = new DwaineVfsNodeHandle(volume.Id, node.Id);
        DwaineVfsNodeHandle? parent = node.Parent is { } parentId
            ? new DwaineVfsNodeHandle(volume.Id, parentId)
            : null;
        return new DwaineVfsNodeSnapshot(
            handle,
            parent,
            node.Name,
            node.Kind,
            node.Metadata,
            node.Size,
            node.Children.Count);
    }

    private static int GetDepth(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        var depth = 0;
        var visited = new HashSet<DwaineVfsNodeId>();
        while (node.Parent is { } parentId && volume.Nodes.TryGetValue(parentId, out node!))
        {
            if (!visited.Add(parentId))
                return int.MaxValue;
            depth++;
        }

        return depth;
    }

    private static int SubtreeHeight(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        if (node.Kind != DwaineVfsNodeKind.Directory || node.Children.Count == 0)
            return 0;

        return 1 + node.Children.Values.Max(id => SubtreeHeight(volume, volume.Nodes[id]));
    }

    private static int CountSubtree(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        return 1 + node.Children.Values.Sum(id => CountSubtree(volume, volume.Nodes[id]));
    }

    private static bool IsAncestor(DwaineVfsVolume volume, DwaineVfsNodeId ancestor, DwaineVfsNodeId node)
    {
        var visited = new HashSet<DwaineVfsNodeId>();
        while (volume.Nodes.TryGetValue(node, out var current) && current.Parent is { } parent)
        {
            if (!visited.Add(parent))
                return false;
            if (parent == ancestor)
                return true;
            node = parent;
        }

        return false;
    }

    private static void DeleteSubtree(DwaineVfsVolume volume, DwaineVfsNode node)
    {
        foreach (var childId in node.Children.Values.ToArray())
        {
            if (volume.Nodes.TryGetValue(childId, out var child))
                DeleteSubtree(volume, child);
        }

        node.Children.Clear();
        volume.Nodes.Remove(node.Id);
    }

    private static int CountArchiveEntries(DwaineVfsArchiveEntry entry)
    {
        return 1
               + entry.EmbeddedArchiveEntries.Sum(CountArchiveEntries)
               + entry.Children.Sum(CountArchiveEntries);
    }

    private static int CountMaterializedArchiveNodes(DwaineVfsArchiveEntry entry)
    {
        return 1 + entry.Children.Sum(CountMaterializedArchiveNodes);
    }

    private static int ArchiveHeight(DwaineVfsArchiveEntry entry)
    {
        var childHeight = entry.Children.Count == 0 ? 0 : 1 + entry.Children.Max(ArchiveHeight);
        var embeddedHeight = entry.EmbeddedArchiveEntries.Count == 0
            ? 0
            : 1 + entry.EmbeddedArchiveEntries.Max(ArchiveHeight);
        return Math.Max(childHeight, embeddedHeight);
    }

    private static int MaterializedArchiveHeight(DwaineVfsArchiveEntry entry)
    {
        return entry.Children.Count == 0 ? 0 : 1 + entry.Children.Max(MaterializedArchiveHeight);
    }

    private static int MaxArchivePathLength(DwaineVfsArchiveEntry entry)
    {
        if (entry.Children.Count == 0)
            return entry.Name.Length;

        return entry.Name.Length + 1 + entry.Children.Max(MaxArchivePathLength);
    }
}
