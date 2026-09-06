// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Storage;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Storage;
using Content.Shared.Interaction;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineStorageTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineStorageTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
          - type: DwaineMainframeRuntime
          - type: DwaineKernel
            autoBoot: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineFileSystem
            maxNodes: 512
            maxDepth: 20
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 16
            maxProcessesPerOwner: 8
            maxDispatchesPerUpdate: 8
            instructionsPerSlice: 8
            instructionsPerProcess: 128
            completedRetentionSeconds: 300
          - type: DwaineProcessRuntime
          - type: DwaineStorageConnector
            slotCount: 3
          - type: DwaineStorageDrive
          - type: DwaineStorageRuntime

        - type: entity
          id: WhiskeyDwaineStorageTestDisk
          components:
          - type: Transform
          - type: DwaineStorageMedia
            kind: RemovableDisk
            label: test-disk
            maxNodes: 128
            maxDepth: 12
          - type: DwaineStorageMediaRuntime

        - type: entity
          id: WhiskeyDwaineStorageTestReadOnly
          components:
          - type: Transform
          - type: DwaineStorageMedia
            kind: RemovableDisk
            label: protected
            readOnly: true
            maxNodes: 128
            maxDepth: 12
          - type: DwaineStorageMediaRuntime

        - type: entity
          id: WhiskeyDwaineStorageTestTape
          components:
          - type: Transform
          - type: DwaineStorageMedia
            kind: Tape
            label: test-tape
            maxNodes: 128
            maxDepth: 12
          - type: DwaineStorageMediaRuntime

        - type: entity
          id: WhiskeyDwaineStorageTestHardDrive
          components:
          - type: Transform
          - type: DwaineStorageMedia
            kind: HardDrive
            label: fixed
            removable: false
            maxNodes: 128
            maxDepth: 12
          - type: DwaineStorageMediaRuntime

        - type: entity
          id: WhiskeyDwaineBootMediaTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
          - type: DwaineMainframeRuntime
          - type: DwaineKernel
            autoBoot: false
            requireStorageConnector: true
            requireBootMedia: true
            bootProfile: test-system-v1
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineFileSystem
          - type: DwaineFileSystemRuntime
          - type: DwaineStorageConnector
            slotCount: 1
          - type: DwaineStorageDrive
            startingMedia: [WhiskeyDwaineBootMediaTestTape]
          - type: DwaineStorageRuntime

        - type: entity
          id: WhiskeyDwaineBootMediaTestTape
          components:
          - type: Transform
          - type: DwaineStorageMedia
            kind: Tape
            label: recovery
            readOnly: true
          - type: DwaineStorageMediaRuntime
          - type: DwaineBootMedia
            profile: test-system-v1
        """;

    [Test]
    public async Task DeletingMapWithInsertedMediaDoesNotAttemptWorldReparent()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        EntityUid hardDrive = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(Vector2.Zero, mapId);
            mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineStorageTestMainframe", coordinates);
            hardDrive = Server.EntMan.SpawnEntity("WhiskeyDwaineStorageTestHardDrive", coordinates);
            Assert.That(
                Server.System<DwaineStorageSystem>().TryInsert(mainframe, hardDrive, 0).Result,
                Is.EqualTo(DwaineStorageResult.Success));

            Server.EntMan.DeleteEntity(map);
        });
        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.Deleted(map), Is.True);
                Assert.That(Server.EntMan.Deleted(mainframe), Is.True);
                Assert.That(Server.EntMan.Deleted(hardDrive), Is.True);
            });
        });
    }

    [Test]
    public async Task BootRequiresExactInsertedDataOnlyMediaProfile()
    {
        EntityUid map = EntityUid.Invalid;
        MapId mapId = default;
        EntityUid rejected = EntityUid.Invalid;
        EntityUid accepted = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out mapId);
            var coordinates = new MapCoordinates(Vector2.Zero, mapId);
            rejected = Server.EntMan.SpawnEntity("WhiskeyDwaineBootMediaTestMainframe", coordinates);
            var storage = Server.System<DwaineStorageSystem>();
            var media = storage.GetInsertedMedia(rejected);
            Assert.That(media, Has.Length.EqualTo(1));
            Server.EntMan.DeleteEntity(media[0].Media);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(rejected), Is.True);
        });
        await Server.WaitRunTicks(4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(rejected), Is.EqualTo(DwaineSystemState.BootFailed));
            accepted = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineBootMediaTestMainframe",
                new MapCoordinates(new Vector2(1, 0), mapId));
            Assert.That(Server.System<DwaineStorageSystem>().GetInsertedMedia(accepted), Has.Length.EqualTo(1));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(accepted), Is.True);
        });
        await Server.WaitRunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(accepted), Is.EqualTo(DwaineSystemState.SystemReady));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task RemovableDiskPersistsAcrossFlushEjectAndReinsert()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        EntityUid disk = EntityUid.Invalid;

        await Server.WaitAssertion(() =>
        {
            disk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            var storage = Server.System<DwaineStorageSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/disk", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryInsert(mainframe, disk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(Server.EntMan.GetComponent<TransformComponent>(disk).ParentUid, Is.EqualTo(mainframe));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/disk").Succeeded, Is.True);
            Assert.That(
                fileSystem.TryCreate(
                    "/mnt/disk/data",
                    fileSystem.Root,
                    new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "persistent" },
                    Server.Timing.CurTime,
                    out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryGetMediaSnapshot(disk, out var dirty), Is.True);
            Assert.That(dirty.Dirty, Is.True);
            Assert.That(storage.TryUnmount(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryEject(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Dirty));
            Assert.That(storage.TryFlush(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryEject(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(Server.EntMan.GetComponent<TransformComponent>(disk).ParentUid, Is.Not.EqualTo(mainframe));

            Assert.That(storage.TryInsert(mainframe, disk, 1).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/disk").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(fileSystem.TryReadText("/mnt/disk/data", fileSystem.Root, out var text),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(text, Is.EqualTo("persistent"));
            Assert.That(storage.TryGetMediaSnapshot(disk, out var mounted), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(mounted.Slot, Is.EqualTo(1));
                Assert.That(mounted.MountPath, Is.EqualTo("/mnt/disk"));
                Assert.That(mounted.FlushedRevision, Is.EqualTo(mounted.Revision));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task SlotsKindsAndReadOnlyMediaAreValidatedServerSide()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        await Server.WaitAssertion(() =>
        {
            var disk = Spawn("WhiskeyDwaineStorageTestReadOnly", map);
            var tape = Spawn("WhiskeyDwaineStorageTestTape", map);
            var hardDrive = Spawn("WhiskeyDwaineStorageTestHardDrive", map);
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(storage.TryInsert(mainframe, disk, 3).Result, Is.EqualTo(DwaineStorageResult.InvalidSlot));
            Assert.That(storage.TryInsert(mainframe, disk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryInsert(mainframe, disk, 1).Result, Is.EqualTo(DwaineStorageResult.AlreadyInserted));
            Assert.That(storage.TryInsert(mainframe, tape, 0).Result, Is.EqualTo(DwaineStorageResult.SlotOccupied));
            var drive = Server.EntMan.GetComponent<DwaineStorageDriveComponent>(mainframe);
            drive.AcceptTapes = false;
            Assert.That(storage.TryInsert(mainframe, tape, 1).Result,
                Is.EqualTo(DwaineStorageResult.UnsupportedMedia));
            drive.AcceptTapes = true;
            Assert.That(storage.TryInsert(mainframe, tape, 1).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryInsert(mainframe, hardDrive, 2).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryEject(mainframe, hardDrive).Result, Is.EqualTo(DwaineStorageResult.NotRemovable));

            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/ro", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/ro").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(
                fileSystem.TryCreate(
                    "/mnt/ro/forbidden",
                    fileSystem.Root,
                    new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text },
                    Server.Timing.CurTime,
                    out _),
                Is.EqualTo(DwaineVfsResult.ReadOnly));
            Assert.That(storage.TryUnmount(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryEject(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));

            Assert.That(fileSystem.TryCreateDirectory("/mnt/tape", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryMount(mainframe, tape, "/mnt/tape").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(
                fileSystem.TryCreate(
                    "/mnt/tape/archive-source",
                    fileSystem.Root,
                    new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "tape data" },
                    Server.Timing.CurTime,
                    out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryUnmount(mainframe, tape).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryFlush(mainframe, tape).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryEject(mainframe, tape).Result, Is.EqualTo(DwaineStorageResult.Success));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ActiveProcessWorkingDirectoryPreventsUnmount()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        await Server.WaitAssertion(() =>
        {
            var disk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/work", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryInsert(mainframe, disk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/work").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(fileSystem.TryCreateDirectory("/mnt/work/job", fileSystem.Root, Server.Timing.CurTime, out var cwd),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(
                processes.TrySpawn(
                    mainframe,
                    new DwaineProcessSpawnRequest
                    {
                        Owner = new DwaineProcessOwner(7),
                        Program = new DwaineProgramDescriptor("hold", "hold"),
                        Implementation = new HoldProgram(),
                        WorkingDirectory = DwaineFileSystemSystem.ToWorkingDirectory(cwd),
                    },
                    out var processId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));

            Assert.That(storage.TryUnmount(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Busy));
            Assert.That(storage.TryEject(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Busy));
            Assert.That(processes.TryExit(mainframe, processId), Is.EqualTo(DwaineProcessControlResult.Success));
            Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(7), processId), Is.True);
            Assert.That(storage.TryUnmount(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.Success));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ShutdownUnmountsButKeepsInsertedPersistentMedia()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        EntityUid disk = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            disk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/reboot", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryInsert(mainframe, disk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/reboot").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(
                fileSystem.TryCreate(
                    "/mnt/reboot/state",
                    fileSystem.Root,
                    new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "after reboot" },
                    Server.Timing.CurTime,
                    out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframe), Is.True);
        });

        await Server.WaitRunTicks(4);
        await Server.WaitAssertion(() =>
        {
            var storage = Server.System<DwaineStorageSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineFileSystemRuntimeComponent>(mainframe);
            Assert.That(runtime.FileSystem!.AttachedVolumeCount, Is.EqualTo(0));
            Assert.That(storage.TryGetMediaSnapshot(disk, out var snapshot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.InsertedInto, Is.EqualTo(mainframe));
                Assert.That(snapshot.MountedOn, Is.Null);
                Assert.That(snapshot.Dirty, Is.True);
            });
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/reboot").Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryReadText("/mnt/reboot/state", fileSystem.Root, out var text),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(text, Is.EqualTo("after reboot"));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task MediaAndMainframeDestructionCleanBothSidesOfTheRelationship()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        EntityUid survivingDisk = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var destroyedDisk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            survivingDisk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/gone", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryInsert(mainframe, destroyedDisk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryMount(mainframe, destroyedDisk, "/mnt/gone").Result,
                Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryInsert(mainframe, survivingDisk, 1).Result, Is.EqualTo(DwaineStorageResult.Success));
            Server.EntMan.DeleteEntity(destroyedDisk);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var storage = Server.System<DwaineStorageSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineFileSystemRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.FileSystem!.AttachedVolumeCount, Is.EqualTo(0));
                Assert.That(storage.GetInsertedMedia(mainframe), Has.Length.EqualTo(1));
            });
            Server.EntMan.DeleteEntity(mainframe);
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var mediaRuntime = Server.EntMan.GetComponent<DwaineStorageMediaRuntimeComponent>(survivingDisk);
            Assert.Multiple(() =>
            {
                Assert.That(mediaRuntime.InsertedInto, Is.Null);
                Assert.That(mediaRuntime.MountedOn, Is.Null);
                Assert.That(mediaRuntime.Slot, Is.EqualTo(-1));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ExternalContainerRemovalInvalidatesMountAndBothRelationshipIndexes()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        await Server.WaitAssertion(() =>
        {
            var disk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var storage = Server.System<DwaineStorageSystem>();
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            var containers = Server.System<ContainerSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreateDirectory("/mnt/external", fileSystem.Root, Server.Timing.CurTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(storage.TryInsert(mainframe, disk, 0).Result, Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(storage.TryMount(mainframe, disk, "/mnt/external").Result,
                Is.EqualTo(DwaineStorageResult.Success));
            Assert.That(containers.TryGetContainer(mainframe, "dwaine-storage-0", out var container), Is.True);
            Assert.That(container, Is.TypeOf<ContainerSlot>());
            Assert.That(containers.Remove(disk, (ContainerSlot) container!, force: true), Is.True);

            Assert.That(storage.TryGetMediaSnapshot(disk, out var snapshot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.InsertedInto, Is.Null);
                Assert.That(snapshot.MountedOn, Is.Null);
                Assert.That(snapshot.Slot, Is.EqualTo(-1));
                Assert.That(storage.GetInsertedMedia(mainframe), Is.Empty);
                Assert.That(fileSystem.AttachedVolumeCount, Is.EqualTo(0));
                Assert.That(storage.TryUnmount(mainframe, disk).Result, Is.EqualTo(DwaineStorageResult.NotInserted));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProductionPrototypesComposeDrivesAndEveryMediaKind()
    {
        await Server.WaitAssertion(() =>
        {
            var mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);
            var hardDrive = Server.EntMan.SpawnEntity("WhiskeyDwaineHardDrive", MapCoordinates.Nullspace);
            var disk = Server.EntMan.SpawnEntity("WhiskeyDwaineRemovableDisk", MapCoordinates.Nullspace);
            var readOnly = Server.EntMan.SpawnEntity("WhiskeyDwaineReadOnlyDisk", MapCoordinates.Nullspace);
            var tape = Server.EntMan.SpawnEntity("WhiskeyDwaineTape", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineStorageDriveComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineStorageRuntimeComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.GetComponent<DwaineStorageMediaComponent>(hardDrive).Kind,
                    Is.EqualTo(DwaineStorageMediaKind.HardDrive));
                Assert.That(Server.EntMan.GetComponent<DwaineStorageMediaComponent>(disk).Kind,
                    Is.EqualTo(DwaineStorageMediaKind.RemovableDisk));
                Assert.That(Server.EntMan.GetComponent<DwaineStorageMediaComponent>(readOnly).ReadOnly, Is.True);
                Assert.That(Server.EntMan.GetComponent<DwaineStorageMediaComponent>(tape).Kind,
                    Is.EqualTo(DwaineStorageMediaKind.Tape));
            });
            Server.EntMan.DeleteEntity(mainframe);
            Server.EntMan.DeleteEntity(hardDrive);
            Server.EntMan.DeleteEntity(disk);
            Server.EntMan.DeleteEntity(readOnly);
            Server.EntMan.DeleteEntity(tape);
        });
    }

    [Test]
    public async Task ReachablePlayerInteractionUsesTheFirstAvailablePhysicalSlot()
    {
        var (map, mainframe) = await SpawnReadyMainframe();
        await Server.WaitAssertion(() =>
        {
            var user = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var disk = Spawn("WhiskeyDwaineStorageTestDisk", map);
            var coordinates = Server.EntMan.GetComponent<TransformComponent>(mainframe).Coordinates;
            var interaction = new AfterInteractEvent(user, disk, mainframe, coordinates, true);
            Server.EntMan.EventBus.RaiseLocalEvent(disk, interaction);

            Assert.That(interaction.Handled, Is.True);
            Assert.That(Server.System<DwaineStorageSystem>().TryGetMediaSnapshot(disk, out var snapshot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.InsertedInto, Is.EqualTo(mainframe));
                Assert.That(snapshot.Slot, Is.EqualTo(0));
                Assert.That(Server.EntMan.GetComponent<TransformComponent>(disk).ParentUid, Is.EqualTo(mainframe));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    private async Task<(EntityUid Map, EntityUid Mainframe)> SpawnReadyMainframe()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineStorageTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });
        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(Server.EntMan.GetComponent<DwaineStorageRuntimeComponent>(mainframe).Online, Is.True);
        });
        return (map, mainframe);
    }

    private EntityUid Spawn(string prototype, EntityUid map)
    {
        var mapId = Server.EntMan.GetComponent<TransformComponent>(map).MapID;
        return Server.EntMan.SpawnEntity(prototype, new MapCoordinates(Vector2.Zero, mapId));
    }

    private sealed class HoldProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            return DwaineProcessStepResult.Yield();
        }
    }
}
