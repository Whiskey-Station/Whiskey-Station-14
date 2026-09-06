// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server.Popups;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Storage;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Storage;

/// <summary>
/// Owns insertion, mount lifetime and persistence of DWAINE media. No client-provided identity is accepted.
/// </summary>
public sealed partial class DwaineStorageSystem : EntitySystem
{
    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private PopupSystem _popups = default!;

    private ulong _nextVolumeId = 2;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineStorageDriveComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineStorageDriveComponent, MapInitEvent>(OnDriveMapInit);
        SubscribeLocalEvent<DwaineStorageDriveComponent, DwaineBootRecoveryRequestedEvent>(OnBootRecovery);
        SubscribeLocalEvent<DwaineStorageDriveComponent, EntityTerminatingEvent>(OnDriveTerminating);
        SubscribeLocalEvent<DwaineStorageDriveComponent, GetVerbsEvent<AlternativeVerb>>(OnGetEjectVerbs);
        SubscribeLocalEvent<DwaineStorageMediaComponent, AfterInteractEvent>(OnMediaAfterInteract);
        SubscribeLocalEvent<DwaineStorageRuntimeComponent, ComponentShutdown>(OnStorageRuntimeShutdown);
        SubscribeLocalEvent<DwaineStorageMediaRuntimeComponent, ComponentShutdown>(OnMediaRuntimeShutdown);
        SubscribeLocalEvent<DwaineStorageMediaRuntimeComponent, EntGotRemovedFromContainerMessage>(OnMediaRemoved);
    }

    public DwaineStorageOperationResult TryInsert(EntityUid mainframe, EntityUid media, int slot)
    {
        if (!TryGetDrive(mainframe, out var connector, out var drive, out var storage))
            return new(DwaineStorageResult.MainframeUnavailable);
        if (!connector.Enabled)
            return new(DwaineStorageResult.ConnectorDisabled);

        var slotCount = Math.Clamp(connector.SlotCount, 0, DwaineStorageConnectorComponent.HardMaxSlotCount);
        if (slot < 0 || slot >= slotCount)
            return new(DwaineStorageResult.InvalidSlot);
        if (storage.InsertedBySlot.ContainsKey(slot))
            return new(DwaineStorageResult.SlotOccupied);
        if (TerminatingOrDeleted(media)
            || !TryComp<DwaineStorageMediaComponent>(media, out var mediaConfig)
            || !IsValidMedia(mediaConfig))
        {
            return new(DwaineStorageResult.InvalidMedia);
        }

        if (!Accepts(drive, mediaConfig.Kind))
            return new(DwaineStorageResult.UnsupportedMedia);

        var mediaRuntime = EnsureComp<DwaineStorageMediaRuntimeComponent>(media);
        if (mediaRuntime.InsertedInto is not null || mediaRuntime.MountedOn is not null)
            return new(DwaineStorageResult.AlreadyInserted);

        var physicalSlot = _containers.EnsureContainer<ContainerSlot>(mainframe, ContainerId(slot));
        if (physicalSlot.ContainedEntity is not null)
            return new(DwaineStorageResult.SlotOccupied);
        if (!_containers.Insert(media, physicalSlot))
            return new(DwaineStorageResult.MediaInUse);

        mediaRuntime.Volume ??= CreateVolume(mediaConfig);
        mediaRuntime.Volume.ReadOnly = mediaConfig.ReadOnly;
        mediaRuntime.InsertedInto = mainframe;
        mediaRuntime.Slot = slot;
        storage.InsertedBySlot.Add(slot, media);
        storage.SlotByMedia.Add(media, slot);
        return new(DwaineStorageResult.Success);
    }

    public DwaineStorageOperationResult TryInsertFirstAvailable(
        EntityUid mainframe,
        EntityUid media,
        out int insertedSlot)
    {
        insertedSlot = -1;
        if (!TryGetDrive(mainframe, out var connector, out _, out var storage))
            return new(DwaineStorageResult.MainframeUnavailable);
        if (!connector.Enabled)
            return new(DwaineStorageResult.ConnectorDisabled);

        var slotCount = Math.Clamp(connector.SlotCount, 0, DwaineStorageConnectorComponent.HardMaxSlotCount);
        for (var slot = 0; slot < slotCount; slot++)
        {
            if (storage.InsertedBySlot.ContainsKey(slot))
                continue;

            var result = TryInsert(mainframe, media, slot);
            if (!result.Succeeded)
                return result;

            insertedSlot = slot;
            return result;
        }

        return new(DwaineStorageResult.SlotOccupied);
    }

    public DwaineStorageOperationResult TryMount(EntityUid mainframe, EntityUid media, string mountPath)
    {
        if (!TryGetInserted(mainframe, media, out var connector, out var storage, out var mediaConfig, out var mediaRuntime))
            return new(DwaineStorageResult.NotInserted);
        if (!connector.Enabled)
            return new(DwaineStorageResult.ConnectorDisabled);
        if (!TryComp<DwaineStorageDriveComponent>(mainframe, out var drive) || !Accepts(drive, mediaConfig.Kind))
            return new(DwaineStorageResult.UnsupportedMedia);
        if (!storage.Online
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
        {
            return new(DwaineStorageResult.KernelNotReady);
        }

        if (mediaRuntime.MountedOn is not null)
            return new(DwaineStorageResult.AlreadyMounted);
        if (mediaRuntime.Volume is not { } volume)
            return new(DwaineStorageResult.InvalidMedia);

        volume.ReadOnly = mediaConfig.ReadOnly;
        var canonical = fileSystem.TryCanonicalize(mountPath, fileSystem.Root, out var canonicalPath);
        if (canonical != DwaineVfsResult.Success)
            return new(DwaineStorageResult.InvalidMountPath, canonical);

        var attach = fileSystem.TryAttachVolume(canonicalPath, fileSystem.Root, volume);
        if (attach != DwaineVfsResult.Success)
            return new(DwaineStorageResult.VfsFailure, attach);

        mediaRuntime.MountedOn = mainframe;
        mediaRuntime.MountPath = canonicalPath;
        return new(DwaineStorageResult.Success);
    }

    public DwaineStorageOperationResult TryUnmount(EntityUid mainframe, EntityUid media)
    {
        if (!TryGetInserted(mainframe, media, out _, out _, out _, out var mediaRuntime))
            return new(DwaineStorageResult.NotInserted);
        if (mediaRuntime.MountedOn != mainframe || mediaRuntime.Volume is not { } volume)
            return new(DwaineStorageResult.NotMounted);
        if (IsVolumeBusy(mainframe, volume.Id))
            return new(DwaineStorageResult.Busy);
        if (!_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
            return new(DwaineStorageResult.KernelNotReady);

        var detach = fileSystem.TryDetachVolume(volume.Id, out var detached);
        if (detach != DwaineVfsResult.Success)
            return new(DwaineStorageResult.VfsFailure, detach);
        if (!ReferenceEquals(detached, volume))
            return new(DwaineStorageResult.VfsFailure, DwaineVfsResult.MountUnavailable);

        ClearMount(mediaRuntime);
        return new(DwaineStorageResult.Success);
    }

    public DwaineStorageOperationResult TryFlush(EntityUid mainframe, EntityUid media)
    {
        if (!TryGetInserted(mainframe, media, out _, out _, out _, out var mediaRuntime))
            return new(DwaineStorageResult.NotInserted);
        if (mediaRuntime.Volume is not { } volume)
            return new(DwaineStorageResult.InvalidMedia);

        volume.Dirty = false;
        mediaRuntime.FlushedRevision = volume.Revision;
        mediaRuntime.LastFlushedAt = _timing.CurTime;
        return new(DwaineStorageResult.Success);
    }

    public DwaineStorageOperationResult TryEject(EntityUid mainframe, EntityUid media)
    {
        if (!TryGetInserted(mainframe, media, out _, out var storage, out var mediaConfig, out var mediaRuntime))
            return new(DwaineStorageResult.NotInserted);
        if (!mediaConfig.Removable)
            return new(DwaineStorageResult.NotRemovable);
        if (mediaRuntime.MountedOn is not null)
            return new(DwaineStorageResult.Busy);
        if (mediaRuntime.Volume is { Dirty: true })
            return new(DwaineStorageResult.Dirty);
        if (!TryEjectPhysical(mainframe, media, mediaRuntime.Slot, false))
            return new(DwaineStorageResult.MediaInUse);

        RemoveInsertion(storage, media, mediaRuntime);
        return new(DwaineStorageResult.Success);
    }

    public bool TryGetMediaSnapshot(EntityUid media, out DwaineStorageMediaSnapshot snapshot)
    {
        snapshot = default;
        if (TerminatingOrDeleted(media)
            || !TryComp<DwaineStorageMediaComponent>(media, out var config)
            || !TryComp<DwaineStorageMediaRuntimeComponent>(media, out var runtime)
            || runtime.Volume is not { } volume)
        {
            return false;
        }

        snapshot = new DwaineStorageMediaSnapshot(
            media,
            config.Kind,
            config.Label,
            volume.Id,
            volume.ReadOnly,
            config.Removable,
            volume.Dirty,
            volume.Revision,
            runtime.FlushedRevision,
            runtime.InsertedInto,
            runtime.Slot,
            runtime.MountedOn,
            runtime.MountPath);
        return true;
    }

    public DwaineStorageMediaSnapshot[] GetInsertedMedia(EntityUid mainframe)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineStorageRuntimeComponent>(mainframe, out var storage))
        {
            return [];
        }

        return storage.InsertedBySlot
            .OrderBy(pair => pair.Key)
            .Select(pair => TryGetMediaSnapshot(pair.Value, out var snapshot) ? snapshot : (DwaineStorageMediaSnapshot?) null)
            .Where(snapshot => snapshot.HasValue)
            .Select(snapshot => snapshot!.Value)
            .ToArray();
    }

    private void OnDriveMapInit(Entity<DwaineStorageDriveComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<DwaineStorageConnectorComponent>(ent, out var connector))
            return;
        var count = Math.Min(
            Math.Clamp(connector.SlotCount, 0, DwaineStorageConnectorComponent.HardMaxSlotCount),
            ent.Comp.StartingMedia.Count);
        for (var slot = 0; slot < count; slot++)
        {
            var media = Spawn(ent.Comp.StartingMedia[slot], Transform(ent).Coordinates);
            if (!TryInsert(ent, media, slot).Succeeded)
                QueueDel(media);
        }
    }

    private void OnBootRecovery(Entity<DwaineStorageDriveComponent> ent, ref DwaineBootRecoveryRequestedEvent args)
    {
        if (args.Recovered || string.IsNullOrWhiteSpace(args.Profile))
            return;
        foreach (var media in GetInsertedMedia(ent))
        {
            if (TryComp<DwaineBootMediaComponent>(media.Media, out var boot)
                && boot.Enabled
                && string.Equals(boot.Profile, args.Profile, StringComparison.Ordinal))
            {
                args.Recovered = true;
                return;
            }
        }
    }

    private void OnKernelReady(Entity<DwaineStorageDriveComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineStorageRuntimeComponent>(ent, out var runtime))
            return;

        DetachAllVolumes(ent.Owner, runtime);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (_kernel.TryRegisterService(
                ent.Owner,
                "storage-media",
                new StorageKernelService(this, ent.Owner, args.BootGeneration)))
        {
            return;
        }

        runtime.Online = false;
        runtime.BootGeneration = 0;
        _kernel.Panic(ent.Owner, "storage-service-registration");
    }

    private void OnMediaAfterInteract(Entity<DwaineStorageMediaComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<DwaineStorageDriveComponent>(target))
            return;

        var result = TryInsertFirstAvailable(target, ent.Owner, out var slot);
        var message = result.Succeeded
            ? Loc.GetString("dwaine-storage-inserted", ("label", ent.Comp.Label), ("slot", slot))
            : StorageFailureMessage(result.Result);
        _popups.PopupEntity(message, target, args.User);
        args.Handled = true;
    }

    private void OnGetEjectVerbs(Entity<DwaineStorageDriveComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        foreach (var media in GetInsertedMedia(ent.Owner))
        {
            if (!media.Removable)
                continue;

            var captured = media;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("dwaine-storage-eject-verb", ("label", captured.Label)),
                Act = () =>
                {
                    var result = TryEject(ent.Owner, captured.Media);
                    var message = result.Succeeded
                        ? Loc.GetString("dwaine-storage-ejected", ("label", captured.Label))
                        : StorageFailureMessage(result.Result);
                    _popups.PopupEntity(message, ent.Owner, user);
                },
            });
        }
    }

    private void OnStorageRuntimeShutdown(Entity<DwaineStorageRuntimeComponent> ent, ref ComponentShutdown args)
    {
        DetachAllVolumes(ent.Owner, ent.Comp);
        foreach (var media in ent.Comp.SlotByMedia.Keys.ToArray())
        {
            if (TryComp<DwaineStorageMediaRuntimeComponent>(media, out var mediaRuntime))
            {
                TryEjectPhysical(ent.Owner, media, mediaRuntime.Slot, true);
                RemoveInsertion(ent.Comp, media, mediaRuntime);
            }
        }

        ent.Comp.Online = false;
        ent.Comp.BootGeneration = 0;
    }

    private void OnDriveTerminating(Entity<DwaineStorageDriveComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!TryComp<DwaineStorageRuntimeComponent>(ent, out var storage))
            return;

        DetachAllVolumes(ent.Owner, storage);
        foreach (var media in storage.SlotByMedia.Keys.ToArray())
        {
            if (!TryComp<DwaineStorageMediaRuntimeComponent>(media, out var mediaRuntime))
                continue;

            TryEjectPhysical(ent.Owner, media, mediaRuntime.Slot, true);
            RemoveInsertion(storage, media, mediaRuntime);
        }
    }

    private void OnMediaRuntimeShutdown(Entity<DwaineStorageMediaRuntimeComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.MountedOn is { } mountedOn && ent.Comp.Volume is { } volume)
            TryForceDetach(mountedOn, volume.Id);

        if (ent.Comp.InsertedInto is { } mainframe
            && TryComp<DwaineStorageRuntimeComponent>(mainframe, out var storage))
        {
            storage.InsertedBySlot.Remove(ent.Comp.Slot);
            storage.SlotByMedia.Remove(ent.Owner);
        }

        ClearMount(ent.Comp);
        ent.Comp.InsertedInto = null;
        ent.Comp.Slot = -1;
    }

    private void OnMediaRemoved(
        Entity<DwaineStorageMediaRuntimeComponent> ent,
        ref EntGotRemovedFromContainerMessage args)
    {
        if (ent.Comp.InsertedInto != args.Container.Owner
            || args.Container.ID != ContainerId(ent.Comp.Slot))
        {
            return;
        }

        if (ent.Comp.MountedOn is { } mountedOn && ent.Comp.Volume is { } volume)
            TryForceDetach(mountedOn, volume.Id);
        if (TryComp<DwaineStorageRuntimeComponent>(args.Container.Owner, out var storage))
        {
            storage.InsertedBySlot.Remove(ent.Comp.Slot);
            storage.SlotByMedia.Remove(ent.Owner);
        }

        ClearMount(ent.Comp);
        ent.Comp.InsertedInto = null;
        ent.Comp.Slot = -1;
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (!TryComp<DwaineStorageRuntimeComponent>(mainframe, out var storage)
            || storage.BootGeneration != generation)
        {
            return;
        }

        DetachAllVolumes(mainframe, storage);
        storage.Online = false;
        storage.BootGeneration = 0;
    }

    private void DetachAllVolumes(EntityUid mainframe, DwaineStorageRuntimeComponent storage)
    {
        foreach (var media in storage.SlotByMedia.Keys.ToArray())
        {
            if (!TryComp<DwaineStorageMediaRuntimeComponent>(media, out var mediaRuntime)
                || mediaRuntime.MountedOn != mainframe
                || mediaRuntime.Volume is not { } volume)
            {
                continue;
            }

            TryForceDetach(mainframe, volume.Id);
            ClearMount(mediaRuntime);
        }
    }

    private void TryForceDetach(EntityUid mainframe, DwaineVfsVolumeId volumeId)
    {
        if (TryComp<DwaineFileSystemRuntimeComponent>(mainframe, out var fileRuntime)
            && fileRuntime.FileSystem is { } fileSystem
            && fileSystem.IsVolumeAttached(volumeId))
        {
            fileSystem.TryDetachVolume(volumeId, out _);
        }
    }

    private bool TryGetDrive(
        EntityUid mainframe,
        out DwaineStorageConnectorComponent connector,
        out DwaineStorageDriveComponent drive,
        out DwaineStorageRuntimeComponent runtime)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp(mainframe, out connector!)
            || !TryComp(mainframe, out drive!)
            || !TryComp(mainframe, out runtime!))
        {
            connector = null!;
            drive = null!;
            runtime = null!;
            return false;
        }

        return true;
    }

    private bool TryGetInserted(
        EntityUid mainframe,
        EntityUid media,
        out DwaineStorageConnectorComponent connector,
        out DwaineStorageRuntimeComponent storage,
        out DwaineStorageMediaComponent mediaConfig,
        out DwaineStorageMediaRuntimeComponent mediaRuntime)
    {
        mediaConfig = null!;
        mediaRuntime = null!;
        if (!TryGetDrive(mainframe, out connector, out _, out storage)
            || TerminatingOrDeleted(media)
            || !TryComp(media, out mediaConfig!)
            || !TryComp(media, out mediaRuntime!)
            || mediaRuntime.InsertedInto != mainframe
            || !storage.SlotByMedia.TryGetValue(media, out var slot)
            || slot != mediaRuntime.Slot
            || !storage.InsertedBySlot.TryGetValue(slot, out var inserted)
            || inserted != media)
        {
            return false;
        }

        return true;
    }

    private bool IsVolumeBusy(EntityUid mainframe, DwaineVfsVolumeId volumeId)
    {
        if (!TryComp<DwaineProcessRuntimeComponent>(mainframe, out var processes))
            return false;

        return processes.Processes.Values.Any(process =>
            !process.IsTerminal && process.WorkingDirectory.Volume == volumeId.Value);
    }

    private DwaineVfsVolume CreateVolume(DwaineStorageMediaComponent media)
    {
        var limits = DwaineVfsLimits.FromValues(
            media.MaxNodes,
            media.MaxDepth,
            media.MaxNameLength,
            media.MaxPathLength,
            media.MaxChildrenPerDirectory,
            media.MaxLinkDepth,
            media.MaxTextCharacters,
            media.MaxRecordEntries,
            media.MaxRecordCharacters,
            media.MaxArchiveEntries,
            media.MaxArchiveDepth);
        return new DwaineVfsVolume(AllocateVolumeId(), limits, media.ReadOnly, _timing.CurTime);
    }

    private DwaineVfsVolumeId AllocateVolumeId()
    {
        while (true)
        {
            var candidate = _nextVolumeId++;
            if (_nextVolumeId is 0 or 1)
                _nextVolumeId = 2;
            if (candidate is 0 or 1)
                continue;

            var id = new DwaineVfsVolumeId(candidate);
            var collision = false;
            var query = EntityQueryEnumerator<DwaineStorageMediaRuntimeComponent>();
            while (query.MoveNext(out _, out var runtime))
            {
                if (runtime.Volume?.Id != id)
                    continue;

                collision = true;
                break;
            }

            if (!collision)
                return id;
        }
    }

    private static bool IsValidMedia(DwaineStorageMediaComponent media)
    {
        return !string.IsNullOrWhiteSpace(media.Label)
               && media.Label.Length <= DwaineStorageMediaComponent.HardMaxLabelLength
               && media.Label.All(character => character != '\0' && !char.IsControl(character));
    }

    private static bool Accepts(DwaineStorageDriveComponent drive, DwaineStorageMediaKind kind)
    {
        return kind switch
        {
            DwaineStorageMediaKind.HardDrive => drive.AcceptHardDrives,
            DwaineStorageMediaKind.RemovableDisk => drive.AcceptRemovableDisks,
            DwaineStorageMediaKind.Tape => drive.AcceptTapes,
            _ => false,
        };
    }

    private static void RemoveInsertion(
        DwaineStorageRuntimeComponent storage,
        EntityUid media,
        DwaineStorageMediaRuntimeComponent mediaRuntime)
    {
        storage.InsertedBySlot.Remove(mediaRuntime.Slot);
        storage.SlotByMedia.Remove(media);
        mediaRuntime.InsertedInto = null;
        mediaRuntime.Slot = -1;
    }

    private static void ClearMount(DwaineStorageMediaRuntimeComponent mediaRuntime)
    {
        mediaRuntime.MountedOn = null;
        mediaRuntime.MountPath = string.Empty;
    }

    private bool TryEjectPhysical(EntityUid mainframe, EntityUid media, int slot, bool force)
    {
        if (!_containers.TryGetContainer(mainframe, ContainerId(slot), out var container))
            return true;
        if (container is not ContainerSlot physicalSlot || physicalSlot.ContainedEntity != media)
            return false;

        // During map shutdown there may be no live world parent left for the media.
        var mainframeTransform = Transform(mainframe);
        var canReparent = !force
                          || ((mainframeTransform.MapUid is not { } map || !TerminatingOrDeleted(map))
                              && (mainframeTransform.GridUid is not { } grid || !TerminatingOrDeleted(grid)));
        return _containers.Remove(media, physicalSlot, reparent: canReparent, force: force);
    }

    private static string ContainerId(int slot)
    {
        return $"dwaine-storage-{slot}";
    }

    private string StorageFailureMessage(DwaineStorageResult result)
    {
        var key = result switch
        {
            DwaineStorageResult.SlotOccupied => "dwaine-storage-error-full",
            DwaineStorageResult.UnsupportedMedia => "dwaine-storage-error-unsupported",
            DwaineStorageResult.AlreadyInserted => "dwaine-storage-error-inserted",
            DwaineStorageResult.NotRemovable => "dwaine-storage-error-fixed",
            DwaineStorageResult.Busy => "dwaine-storage-error-busy",
            DwaineStorageResult.Dirty => "dwaine-storage-error-dirty",
            DwaineStorageResult.ConnectorDisabled => "dwaine-storage-error-disabled",
            _ => "dwaine-storage-error-generic",
        };
        return Loc.GetString(key);
    }

    private sealed class StorageKernelService(
        DwaineStorageSystem system,
        EntityUid mainframe,
        ulong generation) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }
}
