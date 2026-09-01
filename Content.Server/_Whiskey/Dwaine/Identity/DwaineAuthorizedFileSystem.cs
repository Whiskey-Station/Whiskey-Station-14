// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using System;
using System.Collections.Generic;

namespace Content.Server._Whiskey.Dwaine.Identity;

/// <summary>
/// Permission-enforcing façade used by shells, Vodka syscalls and services.
/// Raw VFS primitives remain inaccessible to untrusted execution contexts.
/// </summary>
public sealed class DwaineAuthorizedFileSystem(
    DwaineVirtualFileSystem fileSystem,
    DwaineIdentityStore identities)
{
    public DwaineVfsResult TryResolveDirectory(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsNodeHandle directory)
    {
        directory = default;
        var access = CheckPath(
            principal,
            path,
            workingDirectory,
            DwaineIdentityPermission.Execute,
            out var snapshot);
        if (access != DwaineVfsResult.Success)
            return access;
        if (snapshot.Kind != DwaineVfsNodeKind.Directory)
            return DwaineVfsResult.NotDirectory;

        directory = snapshot.Handle;
        return DwaineVfsResult.Success;
    }

    public DwaineVfsResult TryList(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsNodeSnapshot[] entries)
    {
        entries = [];
        var access = CheckPath(
            principal,
            path,
            workingDirectory,
            DwaineIdentityPermission.Read | DwaineIdentityPermission.Execute,
            out _);
        return access == DwaineVfsResult.Success
            ? fileSystem.TryList(path, workingDirectory, out entries)
            : access;
    }

    public DwaineVfsResult TryReadText(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out string text)
    {
        text = string.Empty;
        var access = CheckPath(principal, path, workingDirectory, DwaineIdentityPermission.Read, out _);
        return access == DwaineVfsResult.Success
            ? fileSystem.TryReadText(path, workingDirectory, out text)
            : access;
    }

    /// <summary>
    /// Returns metadata only when the caller may read the target. This is the narrow stat primitive
    /// used by sandboxed file predicates so inaccessible paths cannot be distinguished from absent ones.
    /// </summary>
    public DwaineVfsResult TryStat(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out DwaineVfsNodeSnapshot snapshot)
    {
        return CheckPath(principal, path, workingDirectory, DwaineIdentityPermission.Read, out snapshot);
    }

    public DwaineVfsResult TryGetFields(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        out IReadOnlyDictionary<string, string?> fields)
    {
        fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var access = CheckPath(principal, path, workingDirectory, DwaineIdentityPermission.Read, out _);
        return access == DwaineVfsResult.Success
            ? fileSystem.TryGetFields(path, workingDirectory, out fields)
            : access;
    }

    public DwaineVfsResult TryWriteText(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        string text,
        bool append,
        TimeSpan now)
    {
        var access = CheckPath(principal, path, workingDirectory, DwaineIdentityPermission.Write, out _);
        return access == DwaineVfsResult.Success
            ? fileSystem.TryWriteText(path, workingDirectory, text, append, now)
            : access;
    }

    public DwaineVfsResult TryCreateText(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        string text,
        DwaineVfsMode? mode,
        TimeSpan now)
    {
        var parentAccess = CheckParentMutation(principal, path, workingDirectory);
        if (parentAccess != DwaineVfsResult.Success)
            return parentAccess;
        if (!TryGetEnabledAccount(principal, out _))
            return DwaineVfsResult.AccessDenied;

        return fileSystem.TryCreate(
            path,
            workingDirectory,
            new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Text,
                Owner = principal.Value,
                Group = DwaineGroupId.Users.Value,
                Mode = mode,
                Text = text,
            },
            now,
            out _);
    }

    public DwaineVfsResult TryCreateDirectory(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now,
        out DwaineVfsNodeHandle directory)
    {
        directory = default;
        var parentAccess = CheckParentMutation(principal, path, workingDirectory);
        if (parentAccess != DwaineVfsResult.Success)
            return parentAccess;
        if (!TryGetEnabledAccount(principal, out _))
            return DwaineVfsResult.AccessDenied;

        return fileSystem.TryCreate(
            path,
            workingDirectory,
            new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Directory,
                Owner = principal.Value,
                Group = DwaineGroupId.Users.Value,
            },
            now,
            out directory);
    }

    public DwaineVfsResult TryCreateLink(
        DwainePrincipalId principal,
        string path,
        string target,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var targetAccess = CheckPath(
            principal,
            target,
            workingDirectory,
            DwaineIdentityPermission.Read,
            out _);
        if (targetAccess != DwaineVfsResult.Success)
            return targetAccess;
        var parentAccess = CheckParentMutation(principal, path, workingDirectory);
        if (parentAccess != DwaineVfsResult.Success)
            return parentAccess;
        var create = fileSystem.TryCreateLink(path, target, workingDirectory, now, out var handle);
        return create == DwaineVfsResult.Success
            ? fileSystem.TrySetMetadata(
                handle,
                principal.Value,
                DwaineGroupId.Users.Value,
                DwaineVfsMode.DefaultFile,
                now)
            : create;
    }

    public DwaineVfsResult TryDelete(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        bool recursive,
        TimeSpan now)
    {
        var parentAccess = CheckParentMutation(principal, path, workingDirectory);
        if (parentAccess != DwaineVfsResult.Success)
            return parentAccess;
        if (recursive)
        {
            var subtreeAccess = CheckSubtree(principal, path, workingDirectory, true);
            if (subtreeAccess != DwaineVfsResult.Success)
                return subtreeAccess;
        }

        return fileSystem.TryDelete(path, workingDirectory, recursive, now);
    }

    public DwaineVfsResult TryCopy(
        DwainePrincipalId principal,
        string source,
        string destination,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var sourceAccess = CheckSubtree(principal, source, workingDirectory, false);
        if (sourceAccess != DwaineVfsResult.Success)
            return sourceAccess;
        var destinationAccess = CheckParentMutation(principal, destination, workingDirectory);
        if (destinationAccess != DwaineVfsResult.Success)
            return destinationAccess;
        var copy = fileSystem.TryCopy(source, destination, workingDirectory, now, out var copied);
        return copy == DwaineVfsResult.Success
            ? ReownSubtree(copied, principal, now)
            : copy;
    }

    public DwaineVfsResult TryMove(
        DwainePrincipalId principal,
        string source,
        string destination,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var sourceParent = CheckParentMutation(principal, source, workingDirectory);
        if (sourceParent != DwaineVfsResult.Success)
            return sourceParent;
        var destinationParent = CheckParentMutation(principal, destination, workingDirectory);
        return destinationParent == DwaineVfsResult.Success
            ? fileSystem.TryMove(source, destination, workingDirectory, now)
            : destinationParent;
    }

    public DwaineVfsResult TryCreateArchive(
        DwainePrincipalId principal,
        string source,
        string archive,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var sourceAccess = CheckSubtree(principal, source, workingDirectory, false);
        if (sourceAccess != DwaineVfsResult.Success)
            return sourceAccess;
        var destinationAccess = CheckParentMutation(principal, archive, workingDirectory);
        if (destinationAccess != DwaineVfsResult.Success)
            return destinationAccess;
        var create = fileSystem.TryCreateArchive(source, archive, workingDirectory, now, out var handle);
        return create == DwaineVfsResult.Success
            ? fileSystem.TrySetMetadata(
                handle,
                principal.Value,
                DwaineGroupId.Users.Value,
                DwaineVfsMode.DefaultFile,
                now)
            : create;
    }

    public DwaineVfsResult TryListArchive(
        DwainePrincipalId principal,
        string archive,
        DwaineVfsNodeHandle workingDirectory,
        out IReadOnlyList<DwaineVfsArchiveEntry> entries)
    {
        entries = [];
        var access = CheckPath(
            principal,
            archive,
            workingDirectory,
            DwaineIdentityPermission.Read,
            out _);
        return access == DwaineVfsResult.Success
            ? fileSystem.TryGetArchiveEntries(archive, workingDirectory, out entries)
            : access;
    }

    public DwaineVfsResult TryExtractArchive(
        DwainePrincipalId principal,
        string archive,
        string destination,
        DwaineVfsNodeHandle workingDirectory,
        TimeSpan now)
    {
        var archiveAccess = CheckPath(
            principal,
            archive,
            workingDirectory,
            DwaineIdentityPermission.Read,
            out _);
        if (archiveAccess != DwaineVfsResult.Success)
            return archiveAccess;
        var destinationAccess = CheckPath(
            principal,
            destination,
            workingDirectory,
            DwaineIdentityPermission.Write | DwaineIdentityPermission.Execute,
            out _);
        return destinationAccess == DwaineVfsResult.Success
            ? fileSystem.TryExtractArchive(archive, destination, workingDirectory, now)
            : destinationAccess;
    }

    public DwaineVfsResult CheckExecute(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory)
    {
        return CheckPath(principal, path, workingDirectory, DwaineIdentityPermission.Execute, out _);
    }

    public DwaineVfsResult TryChangeMode(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        DwaineVfsMode mode,
        TimeSpan now)
    {
        if (principal != DwainePrincipalId.System
            && (!identities.TryGetAccount(principal, out var account) || !account.Enabled))
        {
            return DwaineVfsResult.AccessDenied;
        }

        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        if (fileSystem.TryGetSnapshot(handle, out var snapshot) != DwaineVfsResult.Success)
            return DwaineVfsResult.InvalidHandle;

        var systemOperator = identities.HasPermission(principal, DwaineIdentityPermission.ChangeMode);
        if (!DwaineFileAccessPolicy.CanChangeMode(principal, snapshot.Metadata, systemOperator))
            return DwaineVfsResult.AccessDenied;

        return fileSystem.TrySetMetadata(
            handle,
            snapshot.Metadata.Owner,
            snapshot.Metadata.Group,
            mode,
            now);
    }

    public DwaineVfsResult TryChangeOwner(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        DwainePrincipalId owner,
        DwaineGroupId? group,
        TimeSpan now)
    {
        if (!DwaineFileAccessPolicy.CanChangeOwner(
                principal,
                identities.HasPermission(principal, DwaineIdentityPermission.ChangeOwner)))
        {
            return DwaineVfsResult.AccessDenied;
        }
        if (owner != DwainePrincipalId.System && !identities.TryGetAccount(owner, out _))
            return DwaineVfsResult.AccessDenied;
        if (group is { } selectedGroup && !identities.GroupExists(selectedGroup))
            return DwaineVfsResult.AccessDenied;

        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        if (fileSystem.TryGetSnapshot(handle, out var snapshot) != DwaineVfsResult.Success)
            return DwaineVfsResult.InvalidHandle;

        return fileSystem.TrySetMetadata(
            handle,
            owner.Value,
            group?.Value ?? snapshot.Metadata.Group,
            snapshot.Metadata.Mode,
            now);
    }

    private DwaineVfsResult CheckPath(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        DwaineIdentityPermission permission,
        out DwaineVfsNodeSnapshot snapshot)
    {
        snapshot = default;
        var traversal = CheckTraversal(principal, path, workingDirectory);
        if (traversal != DwaineVfsResult.Success)
            return traversal;
        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        var snapshotResult = fileSystem.TryGetSnapshot(handle, out snapshot);
        if (snapshotResult != DwaineVfsResult.Success)
            return snapshotResult;

        if (!TryGetEnabledAccount(principal, out var groups))
            return DwaineVfsResult.AccessDenied;

        return DwaineFileAccessPolicy.CanAccess(principal, groups, snapshot.Metadata, permission)
            ? DwaineVfsResult.Success
            : DwaineVfsResult.AccessDenied;
    }

    private DwaineVfsResult CheckTraversal(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory)
    {
        var canonical = fileSystem.TryCanonicalize(path, workingDirectory, out var canonicalPath);
        if (canonical != DwaineVfsResult.Success)
            return canonical;
        if (!TryGetEnabledAccount(principal, out var groups))
            return DwaineVfsResult.AccessDenied;

        var segments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefix = string.Empty;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            prefix += $"/{segments[index]}";
            var resolve = fileSystem.TryResolve(prefix, fileSystem.Root, out var handle);
            if (resolve != DwaineVfsResult.Success)
                return resolve;
            var get = fileSystem.TryGetSnapshot(handle, out var snapshot);
            if (get != DwaineVfsResult.Success)
                return get;
            if (snapshot.Kind != DwaineVfsNodeKind.Directory)
                return DwaineVfsResult.NotDirectory;
            if (!DwaineFileAccessPolicy.CanAccess(
                    principal,
                    groups,
                    snapshot.Metadata,
                    DwaineIdentityPermission.Execute))
            {
                return DwaineVfsResult.AccessDenied;
            }
        }

        return DwaineVfsResult.Success;
    }

    private DwaineVfsResult CheckParentMutation(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory)
    {
        var canonical = fileSystem.TryCanonicalize(path, workingDirectory, out var canonicalPath);
        if (canonical != DwaineVfsResult.Success)
            return canonical;
        if (canonicalPath == "/")
            return DwaineVfsResult.RootProtected;

        var slash = canonicalPath.LastIndexOf('/');
        var parentPath = slash == 0 ? "/" : canonicalPath[..slash];
        return CheckPath(
            principal,
            parentPath,
            fileSystem.Root,
            DwaineIdentityPermission.Write | DwaineIdentityPermission.Execute,
            out _);
    }

    private DwaineVfsResult CheckSubtree(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        bool requireDirectoryMutation)
    {
        return CheckSubtree(
            principal,
            path,
            workingDirectory,
            requireDirectoryMutation,
            new HashSet<DwaineVfsNodeHandle>());
    }

    private DwaineVfsResult CheckSubtree(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        bool requireDirectoryMutation,
        HashSet<DwaineVfsNodeHandle> visited)
    {
        var traversal = CheckTraversal(principal, path, workingDirectory);
        if (traversal != DwaineVfsResult.Success)
            return traversal;
        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle, false);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        var get = fileSystem.TryGetSnapshot(handle, out var snapshot);
        if (get != DwaineVfsResult.Success)
            return get;
        if (!TryGetEnabledAccount(principal, out var groups))
            return DwaineVfsResult.AccessDenied;

        var required = DwaineIdentityPermission.Read;
        if (snapshot.Kind == DwaineVfsNodeKind.Directory)
            required |= DwaineIdentityPermission.Execute;
        if (requireDirectoryMutation && snapshot.Kind == DwaineVfsNodeKind.Directory)
            required |= DwaineIdentityPermission.Write;
        if (!DwaineFileAccessPolicy.CanAccess(principal, groups, snapshot.Metadata, required))
            return DwaineVfsResult.AccessDenied;
        if (snapshot.Kind != DwaineVfsNodeKind.Directory || !visited.Add(handle))
            return DwaineVfsResult.Success;

        var list = fileSystem.TryList(path, workingDirectory, out var entries);
        if (list != DwaineVfsResult.Success)
            return list;
        foreach (var entry in entries)
        {
            if (fileSystem.TryGetPath(entry.Handle, out var childPath) != DwaineVfsResult.Success)
                return DwaineVfsResult.InvalidHandle;
            var child = CheckSubtree(
                principal,
                childPath,
                fileSystem.Root,
                requireDirectoryMutation,
                visited);
            if (child != DwaineVfsResult.Success)
                return child;
        }

        return DwaineVfsResult.Success;
    }

    private bool TryGetEnabledAccount(
        DwainePrincipalId principal,
        out IReadOnlySet<DwaineGroupId> groups)
    {
        if (principal == DwainePrincipalId.System)
        {
            groups = new HashSet<DwaineGroupId>();
            return true;
        }

        if (identities.TryGetAccount(principal, out var account) && account.Enabled)
        {
            groups = account.Groups;
            return true;
        }

        groups = new HashSet<DwaineGroupId>();
        return false;
    }

    private DwaineVfsResult ReownSubtree(
        DwaineVfsNodeHandle handle,
        DwainePrincipalId principal,
        TimeSpan now)
    {
        var get = fileSystem.TryGetSnapshot(handle, out var snapshot);
        if (get != DwaineVfsResult.Success)
            return get;
        var set = fileSystem.TrySetMetadata(
            handle,
            principal.Value,
            DwaineGroupId.Users.Value,
            snapshot.Metadata.Mode,
            now);
        if (set != DwaineVfsResult.Success || snapshot.Kind != DwaineVfsNodeKind.Directory)
            return set;
        var path = fileSystem.TryGetPath(handle, out var directoryPath);
        if (path != DwaineVfsResult.Success)
            return path;
        var list = fileSystem.TryList(directoryPath, fileSystem.Root, out var children);
        if (list != DwaineVfsResult.Success)
            return list;
        foreach (var child in children)
        {
            var childResult = ReownSubtree(child.Handle, principal, now);
            if (childResult != DwaineVfsResult.Success)
                return childResult;
        }
        return DwaineVfsResult.Success;
    }
}
