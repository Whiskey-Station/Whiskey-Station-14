// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Server._Whiskey.VodkaCode.Runtime;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.VodkaCode;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineScaleTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineScaleMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
            maxSessions: 32
          - type: DwaineMainframeRuntime
          - type: DwaineKernel
            autoBoot: false
            requireStorageConnector: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineFileSystem
            maxNodes: 2048
            maxChildrenPerDirectory: 2048
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 128
            maxProcessesPerOwner: 128
            maxDispatchesPerUpdate: 128
            instructionsPerSlice: 1024
          - type: DwaineProcessRuntime
          - type: DwaineIdentity
            maxAccounts: 8
            maxSessions: 64
          - type: DwaineIdentityRuntime
          - type: DwaineNetworkConnector
            networkId: scale
            adapter: Radio
            tags: [mainframe]
            linkRange: 16
          - type: DwaineNetworkEndpoint
            maxDiscoveryResults: 128
          - type: DwaineDeviceAbi
            maxAttachedDevices: 128
            maxHandles: 256
            maxHandlesPerProcess: 32
            scanCooldownSeconds: 0.1
          - type: DwaineDeviceAbiRuntime
          - type: VodkaRuntime
            maxInstructionsPerInvocation: 100000
            maxOutputBytes: 1024
          - type: VodkaRuntimeState

        - type: entity
          id: WhiskeyDwaineScaleTerminal
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Terminal
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineTerminalLink
          - type: DwaineTerminal
          - type: DwaineKeyboardInput
          - type: DwaineNetworkConnector
            networkId: scale
            adapter: Radio
            tags: [terminal]
            linkRange: 16
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineScaleActor
          components:
          - type: Transform

        - type: entity
          id: WhiskeyDwaineScaleDevice
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: scale
            adapter: Radio
            tags: [device, sensor]
            linkRange: 16
          - type: DwaineNetworkEndpoint
          - type: DwaineDevice
            driverId: scale-sensor
            tag: sensor
            displayName: bounded scale sensor
            capabilities: Inspect
            access: Authenticated
        """;

    [Test]
    public async Task FourMainframesScaleSessionsProcessesFilesScriptsAndDevicesWithinBounds()
    {
        EntityUid map = EntityUid.Invalid;
        var mainframes = new List<EntityUid>();
        var principals = new List<DwaineAccountSnapshot>();
        var scripts = new List<(EntityUid Mainframe, DwaineProcessId Process)>();
        var deviceCallers = new List<DwaineProcessId>();
        var terminals = new List<EntityUid>();

        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(Vector2.Zero, mapId);
            for (var index = 0; index < 4; index++)
            {
                var mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineScaleMainframe", coordinates);
                mainframes.Add(mainframe);
                Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
            }

            for (var index = 0; index < 64; index++)
                Server.EntMan.SpawnEntity("WhiskeyDwaineScaleDevice", coordinates);
        });
        await Server.WaitRunTicks(8);

        await Server.WaitAssertion(() =>
        {
            foreach (var mainframe in mainframes)
            {
                Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                    Is.EqualTo(DwaineSystemState.SystemReady));
                Assert.That(Server.System<DwaineIdentitySystem>().TryGetStore(mainframe, out var identities), Is.True);
                Assert.That(identities.TryCreateAccount("scale", "scale-password", true, out var account),
                    Is.EqualTo(DwaineIdentityResult.Success));
                principals.Add(account);

                Assert.That(Server.System<DwaineFileSystemSystem>().TryGetFileSystem(mainframe, out var fileSystem), Is.True);
                Assert.That(fileSystem.TryCreate("/home/scale", fileSystem.Root, new DwaineVfsCreateRequest
                {
                    Kind = DwaineVfsNodeKind.Directory,
                    Owner = account.Principal.Value,
                    Group = DwaineGroupId.Users.Value,
                    Mode = DwaineVfsMode.DefaultDirectory,
                }, Server.Timing.CurTime, out _), Is.EqualTo(DwaineVfsResult.Success));
                Assert.That(fileSystem.TryCreate("/home/scale/load.vodka", fileSystem.Root, new DwaineVfsCreateRequest
                {
                    Kind = DwaineVfsNodeKind.Program,
                    Owner = account.Principal.Value,
                    Group = DwaineGroupId.Users.Value,
                    Mode = DwaineVfsMode.OwnerAll,
                    Program = new DwaineVfsProgramData(
                        "scale-load",
                        "let i = 0; while (i < 256) { i = i + 1; } console.writeln(\"ok\");",
                        true,
                        false),
                }, Server.Timing.CurTime, out _), Is.EqualTo(DwaineVfsResult.Success));

                for (var file = 0; file < 1000; file++)
                {
                    Assert.That(fileSystem.TryCreate($"/tmp/load-{file}", fileSystem.Root, new DwaineVfsCreateRequest
                    {
                        Kind = DwaineVfsNodeKind.Text,
                        Text = "bounded",
                    }, Server.Timing.CurTime, out _), Is.EqualTo(DwaineVfsResult.Success));
                }

                for (var script = 0; script < 32; script++)
                {
                    var started = Server.System<VodkaRuntimeSystem>().TryStart(
                        mainframe,
                        account.Principal,
                        null,
                        DwaineWorkingDirectoryHandle.Root,
                        "/home/scale/load.vodka",
                        [],
                        true);
                    Assert.That(started.Succeeded, Is.True, started.Error);
                    scripts.Add((mainframe, started.ProcessId));
                }

                DwaineProcessId caller = default;
                for (var process = 0; process < 96; process++)
                {
                    Assert.That(Server.System<DwaineProcessSystem>().TrySpawn(mainframe, new DwaineProcessSpawnRequest
                    {
                        Owner = new DwaineProcessOwner(account.Principal.Value),
                        Program = new DwaineProgramDescriptor("scale-wait", "bounded scale waiter"),
                        Implementation = new WaitingProgram(),
                        WorkingDirectory = DwaineWorkingDirectoryHandle.Root,
                    }, out var processId), Is.EqualTo(DwaineProcessSpawnResult.Success));
                    caller = processId;
                }
                deviceCallers.Add(caller);
                Assert.Multiple(() =>
                {
                    Assert.That(Server.System<DwaineProcessSystem>().GetProcessTable(mainframe), Has.Length.EqualTo(128));
                    Assert.That(fileSystem.NodeCount, Is.GreaterThan(1000));
                });
            }
        });

        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var coordinates = Server.EntMan.GetComponent<TransformComponent>(mainframes[0]).Coordinates;
            for (var index = 0; index < 128; index++)
            {
                var terminal = Server.EntMan.SpawnEntity("WhiskeyDwaineScaleTerminal", coordinates);
                var actor = Server.EntMan.SpawnEntity("WhiskeyDwaineScaleActor", coordinates);
                terminals.Add(terminal);
                Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
                Assert.That(transport.TryConnect(terminal, mainframes[index / 32], actor, out _),
                    Is.EqualTo(DwaineConnectResult.Connected));

                if (index == 31)
                    Assert.That(mainframes.Sum(transport.GetSessionCount), Is.EqualTo(32));
            }

            Assert.That(mainframes.Sum(transport.GetSessionCount), Is.EqualTo(128));
            var devices = Server.System<DwaineDeviceSystem>();
            for (var index = 0; index < mainframes.Count; index++)
            {
                Assert.That(devices.TryScan(mainframes[index], deviceCallers[index], principals[index].Principal, out var visible),
                    Is.EqualTo(DwaineDeviceResult.Success));
                Assert.That(visible, Is.EqualTo(64),
                    "remote devices are visible; terminal endpoints remain scoped to their own sessions");
            }
        });

        await Server.WaitRunTicks(40);
        await Server.WaitAssertion(() =>
        {
            var vodka = Server.System<VodkaRuntimeSystem>();
            foreach (var (mainframe, process) in scripts)
            {
                Assert.That(vodka.TryTakeCapturedOutput(mainframe, process, out var output), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(output.ExitCode, Is.Zero, output.StandardError);
                    Assert.That(output.StandardOutput, Is.EqualTo("ok\n"));
                    Assert.That(output.StandardError, Is.Empty);
                });
            }

            foreach (var mainframe in mainframes)
                Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframe), Is.True);
        });
        await Server.WaitRunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            foreach (var mainframe in mainframes)
                Assert.That(processes.GetProcessTable(mainframe), Is.Empty);
            foreach (var mainframe in mainframes)
                Server.EntMan.DeleteEntity(mainframe);
            Server.System<DwaineTerminalTransportSystem>().ValidateAllSessions();
            Assert.That(terminals.All(terminal =>
                    Server.EntMan.GetComponent<DwaineTerminalLinkComponent>(terminal).Session is null),
                Is.True);
            Server.EntMan.DeleteEntity(map);
        });
    }

    private sealed class WaitingProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
            => DwaineProcessStepResult.WaitForInput();
    }
}
