// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Storage;

namespace Content.Server._Whiskey.Dwaine.Storage;

public enum DwaineStorageResult : byte
{
    Success,
    MainframeUnavailable,
    ConnectorDisabled,
    KernelNotReady,
    InvalidMedia,
    InvalidSlot,
    SlotOccupied,
    UnsupportedMedia,
    AlreadyInserted,
    NotInserted,
    MediaInUse,
    NotRemovable,
    AlreadyMounted,
    NotMounted,
    Busy,
    Dirty,
    InvalidMountPath,
    VfsFailure,
}

public readonly record struct DwaineStorageMediaSnapshot(
    EntityUid Media,
    DwaineStorageMediaKind Kind,
    string Label,
    DwaineVfsVolumeId VolumeId,
    bool ReadOnly,
    bool Removable,
    bool Dirty,
    ulong Revision,
    ulong FlushedRevision,
    EntityUid? InsertedInto,
    int Slot,
    EntityUid? MountedOn,
    string MountPath);

public readonly record struct DwaineStorageOperationResult(
    DwaineStorageResult Result,
    DwaineVfsResult FileSystemResult = DwaineVfsResult.Success)
{
    public bool Succeeded => Result == DwaineStorageResult.Success;
}
