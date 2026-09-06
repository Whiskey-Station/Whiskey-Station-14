// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineFileSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineFileSystemTestMainframe
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
            maxNodes: 256
            maxDepth: 16
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 8
            maxProcessesPerOwner: 4
            maxDispatchesPerUpdate: 8
            instructionsPerSlice: 8
            instructionsPerProcess: 64
            completedRetentionSeconds: 300
          - type: DwaineProcessRuntime
          - type: DwaineStorageConnector
            slotCount: 1
        """;

    [Test]
    public async Task KernelLifecycleKeepsTheLogicalTreeAndRevokesOfflineAccess()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineVfsNodeHandle fileHandle = default;
        DwaineVirtualFileSystem tree = null;

        await SpawnReadyMainframe((createdMap, createdMainframe) =>
        {
            map = createdMap;
            mainframe = createdMainframe;
        });

        await Server.WaitAssertion(() =>
        {
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            tree = fileSystem;
            Assert.That(
                fileSystem.TryCreate(
                    "/home/persistent.txt",
                    fileSystem.Root,
                    new DwaineVfsCreateRequest
                    {
                        Kind = DwaineVfsNodeKind.Text,
                        Text = "survives a clean reboot",
                    },
                    Server.Timing.CurTime,
                    out fileHandle),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframe), Is.True);
        });

        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineFileSystemRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(fileSystems.TryGetFileSystem(mainframe, out _), Is.False);
                Assert.That(runtime.Online, Is.False);
                Assert.That(runtime.FileSystem, Is.SameAs(tree));
            });
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(fileSystem.TryReadText("/home/persistent.txt", fileSystem.Root, out var text),
                    Is.EqualTo(DwaineVfsResult.Success));
                Assert.That(text, Is.EqualTo("survives a clean reboot"));
                Assert.That(fileSystem.TryGetPath(fileHandle, out var path), Is.EqualTo(DwaineVfsResult.Success));
                Assert.That(path, Is.EqualTo("/home/persistent.txt"));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProcessWorkingDirectoriesAndProcViewsUseValidatedServerHandles()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId processId = default;

        await SpawnReadyMainframe((createdMap, createdMainframe) =>
        {
            map = createdMap;
            mainframe = createdMainframe;
        });

        await Server.WaitAssertion(() =>
        {
            var fileSystems = Server.System<DwaineFileSystemSystem>();
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(
                fileSystem.TryCreateDirectory("/home/worker", fileSystem.Root, Server.Timing.CurTime, out var working),
                Is.EqualTo(DwaineVfsResult.Success));

            var invalidRequest = Request(new DwaineWorkingDirectoryHandle(99, 99));
            Assert.That(processes.TrySpawn(mainframe, invalidRequest, out _),
                Is.EqualTo(DwaineProcessSpawnResult.InvalidWorkingDirectory));

            var request = Request(DwaineFileSystemSystem.ToWorkingDirectory(working));
            Assert.That(processes.TrySpawn(mainframe, request, out processId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TryGetProcess(mainframe, processId, out var process), Is.True);
            Assert.That(process.WorkingDirectory, Is.EqualTo(request.WorkingDirectory));

            Assert.That(fileSystem.TryGetFields($"/proc/{processId.Value}", fileSystem.Root, out var fields),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(fields["state"], Is.EqualTo(DwaineProcessState.Ready.ToString()));
                Assert.That(fields["generation"], Is.EqualTo("1"));
            });

            Assert.That(processes.TryExit(mainframe, processId), Is.EqualTo(DwaineProcessControlResult.Success));
            Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(42), processId), Is.True);
            Assert.That(fileSystem.TryResolve($"/proc/{processId.Value}", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.NotFound));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProductionMainframeComposesFilesystemContractsAndRuntime()
    {
        await Server.WaitAssertion(() =>
        {
            var entity = Server.EntMan.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineFileSystemComponent>(entity), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineFileSystemRuntimeComponent>(entity), Is.True);
            });
            Server.EntMan.DeleteEntity(entity);
        });
    }

    private async Task SpawnReadyMainframe(System.Action<EntityUid, EntityUid> callback)
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineFileSystemTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });
        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            callback(map, mainframe);
        });
    }

    private static DwaineProcessSpawnRequest Request(DwaineWorkingDirectoryHandle workingDirectory)
    {
        return new DwaineProcessSpawnRequest
        {
            Owner = new DwaineProcessOwner(42),
            Program = new DwaineProgramDescriptor("hold", "hold"),
            Implementation = new HoldProgram(),
            WorkingDirectory = workingDirectory,
        };
    }

    private sealed class HoldProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            return DwaineProcessStepResult.Yield();
        }
    }
}
