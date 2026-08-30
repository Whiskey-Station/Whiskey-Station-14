// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;

namespace Content.Server._Whiskey.Dwaine.Storage;

[RegisterComponent]
public sealed partial class DwaineStorageRuntimeComponent : Component
{
    internal readonly Dictionary<int, EntityUid> InsertedBySlot = new();
    internal readonly Dictionary<EntityUid, int> SlotByMedia = new();
    public bool Online;
    public ulong BootGeneration;
}

[RegisterComponent]
public sealed partial class DwaineStorageMediaRuntimeComponent : Component
{
    public DwaineVfsVolume? Volume;
    public ulong FlushedRevision;
    public TimeSpan? LastFlushedAt;
    public EntityUid? InsertedInto;
    public int Slot = -1;
    public EntityUid? MountedOn;
    public string MountPath = string.Empty;
}
