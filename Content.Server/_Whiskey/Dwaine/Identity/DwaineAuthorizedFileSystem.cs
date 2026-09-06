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
        DwaineGroupId group,
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
        if (!identities.GroupExists(group))
            return DwaineVfsResult.AccessDenied;

        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        if (fileSystem.TryGetSnapshot(handle, out var snapshot) != DwaineVfsResult.Success)
            return DwaineVfsResult.InvalidHandle;

        return fileSystem.TrySetMetadata(handle, owner.Value, group.Value, snapshot.Metadata.Mode, now);
    }

    private DwaineVfsResult CheckPath(
        DwainePrincipalId principal,
        string path,
        DwaineVfsNodeHandle workingDirectory,
        DwaineIdentityPermission permission,
        out DwaineVfsNodeSnapshot snapshot)
    {
        snapshot = default;
        var resolve = fileSystem.TryResolve(path, workingDirectory, out var handle);
        if (resolve != DwaineVfsResult.Success)
            return resolve;
        var snapshotResult = fileSystem.TryGetSnapshot(handle, out snapshot);
        if (snapshotResult != DwaineVfsResult.Success)
            return snapshotResult;

        IReadOnlySet<DwaineGroupId> groups = new HashSet<DwaineGroupId>();
        if (principal != DwainePrincipalId.System)
        {
            if (!identities.TryGetAccount(principal, out var account) || !account.Enabled)
                return DwaineVfsResult.AccessDenied;
            groups = account.Groups;
        }

        return DwaineFileAccessPolicy.CanAccess(principal, groups, snapshot.Metadata, permission)
            ? DwaineVfsResult.Success
            : DwaineVfsResult.AccessDenied;
    }
}
