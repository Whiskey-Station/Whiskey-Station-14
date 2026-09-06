// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Shell;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Shell;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineShellTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineShellTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
            maxSessions: 4
            outputLineLimit: 128
            outputCharacterLimit: 16384
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
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 16
            maxProcessesPerOwner: 8
            maxDispatchesPerUpdate: 16
            instructionsPerSlice: 8192
            instructionsPerProcess: 65536
            streamChunkLimit: 64
            streamCharacterLimit: 32768
          - type: DwaineProcessRuntime
          - type: DwaineIdentity
            maxAccounts: 16
            maxGroups: 8
            maxSessions: 4
            sessionLifetimeSeconds: 300
          - type: DwaineIdentityRuntime
          - type: DwaineStorageConnector
            slotCount: 1
          - type: DwaineShell
            maxInputLength: 1024
            maxTokens: 128
            maxCommands: 32
          - type: DwaineShellRuntime
          - type: DwaineNetworkConnector
            networkId: shell-test
            linkRange: 10

        - type: entity
          id: WhiskeyDwaineShellTestTerminal
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Terminal
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineTerminalLink
          - type: DwaineTerminal
            maxInputLength: 1024
          - type: DwaineKeyboardInput
          - type: DwaineNetworkConnector
            networkId: shell-test
            linkRange: 10
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineShellTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        EntityUid terminalA = EntityUid.Invalid;
        EntityUid terminalB = EntityUid.Invalid;
        EntityUid actorA = EntityUid.Invalid;
        EntityUid actorB = EntityUid.Invalid;
        DwaineSessionId sessionA = default;
        DwaineSessionId sessionB = default;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineShellTestMainframe", origin);
            terminalA = Server.EntMan.SpawnEntity("WhiskeyDwaineShellTestTerminal", origin);
            terminalB = Server.EntMan.SpawnEntity("WhiskeyDwaineShellTestTerminal", origin);
            actorA = Server.EntMan.SpawnEntity("WhiskeyDwaineShellTestActor", origin);
            actorB = Server.EntMan.SpawnEntity("WhiskeyDwaineShellTestActor", origin);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var identities = Server.System<DwaineIdentitySystem>();
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(identities.TryGetStore(mainframe, out var store), Is.True);
            Assert.That(store.TryCreateAccount("alex", "alex-password", false, out _),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(ui.TryOpenUi(terminalA, DwaineTerminalUiKey.Key, actorA), Is.True);
            Assert.That(transport.TryConnect(terminalA, mainframe, actorA, out sessionA),
                Is.EqualTo(DwaineConnectResult.Connected));
        });

        await Server.WaitRunTicks(2);
        long idleInstructions = 0;
        await Server.WaitAssertion(() =>
        {
            var runtime = Server.EntMan.GetComponent<DwaineShellRuntimeComponent>(mainframe);
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(runtime.Online, Is.True);
            Assert.That(runtime.Sessions, Has.Count.EqualTo(1));
            Assert.That(runtime.Sessions[sessionA].ProcessId, Is.Not.Null);
            Assert.That(processes.TryGetProcess(mainframe, runtime.Sessions[sessionA].ProcessId!.Value, out var process),
                Is.True);
            Assert.That(process.State, Is.EqualTo(DwaineProcessState.Waiting));
            idleInstructions = process.InstructionsConsumed;
            var output = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe)
                .Sessions[sessionA].Output.Snapshot();
            Assert.That(output.Any(line => line.Contains("DWAINE ready")), Is.True);
            Assert.That(output.Any(line => line.Contains("guest-") && line.EndsWith('$')), Is.True);
        });

        await Server.WaitRunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var shell = Server.EntMan.GetComponent<DwaineShellRuntimeComponent>(mainframe).Sessions[sessionA];
            Assert.That(Server.System<DwaineProcessSystem>().TryGetProcess(mainframe, shell.ProcessId!.Value, out var process),
                Is.True);
            Assert.That(process.InstructionsConsumed, Is.EqualTo(idleInstructions),
                "a shell waiting for input must not consume scheduler slices");
        });

        await Send(terminalA, actorA, "su alex alex-password");
        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var identities = Server.System<DwaineIdentitySystem>();
            Assert.That(identities.TryGetSession(mainframe, sessionA, out var identity),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identities.TryGetStore(mainframe, out var store), Is.True);
            Assert.That(store.TryGetAccount(identity.Principal, out var account), Is.True);
            Assert.That(account.Name, Is.EqualTo("alex"));
            var shell = Server.EntMan.GetComponent<DwaineShellRuntimeComponent>(mainframe).Sessions[sessionA];
            Assert.That(shell.Environment["USER"], Is.EqualTo("alex"));
        });

        await Send(terminalA, actorA, "mkdir -p work; echo integration > work/note; cat work/note");
        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var output = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe)
                .Sessions[sessionA].Output.Snapshot();
            Assert.That(output, Does.Contain("integration"));
            Assert.That(Server.System<DwaineFileSystemSystem>().TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryReadText("/home/alex/work/note", fileSystem.Root, out var text),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(text, Is.EqualTo("integration\n"));
        });

        await Server.WaitAssertion(() =>
        {
            var first = new DwaineTerminalInputReceivedEvent(actorA, "echo burst-one");
            Server.EntMan.EventBus.RaiseLocalEvent(terminalA, ref first);
            var second = new DwaineTerminalInputReceivedEvent(actorA, "echo burst-two");
            Server.EntMan.EventBus.RaiseLocalEvent(terminalA, ref second);
        });
        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var output = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe)
                .Sessions[sessionA].Output.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain("burst-one"));
                Assert.That(output, Does.Contain("burst-two"));
            });
        });

        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            Assert.That(ui.TryOpenUi(terminalB, DwaineTerminalUiKey.Key, actorB), Is.True);
            Assert.That(transport.TryConnect(terminalB, mainframe, actorB, out sessionB),
                Is.EqualTo(DwaineConnectResult.Connected));
        });
        await Server.WaitRunTicks(2);
        await Send(terminalB, actorB, "history");
        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var runtime = Server.EntMan.GetComponent<DwaineShellRuntimeComponent>(mainframe);
            var secondOutput = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe)
                .Sessions[sessionB].Output.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Sessions, Has.Count.EqualTo(2));
                Assert.That(Server.System<DwaineProcessSystem>().GetActiveProcessCount(mainframe), Is.EqualTo(2));
                Assert.That(secondOutput.Any(line => line.Contains("alex-password")), Is.False);
                Assert.That(secondOutput.Any(line => line.Contains("mkdir -p work")), Is.False);
            });
            Assert.That(Server.System<DwaineTerminalTransportSystem>().TryDisconnect(terminalA, actorA), Is.True);
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var shellRuntime = Server.EntMan.GetComponent<DwaineShellRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(shellRuntime.Sessions.ContainsKey(sessionA), Is.False);
                Assert.That(shellRuntime.Sessions.ContainsKey(sessionB), Is.True);
                Assert.That(Server.System<DwaineProcessSystem>().GetActiveProcessCount(mainframe), Is.EqualTo(1));
            });
            Assert.That(Server.System<DwaineTerminalTransportSystem>().TryConnect(terminalA, mainframe, actorA, out var reconnected),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(reconnected, Is.Not.EqualTo(sessionA));
            Server.EntMan.DeleteEntity(map);
        });

        async Task Send(EntityUid terminal, EntityUid actor, string command)
        {
            await Server.WaitAssertion(() =>
            {
                var input = new DwaineTerminalInputReceivedEvent(actor, command);
                Server.EntMan.EventBus.RaiseLocalEvent(terminal, ref input);
            });
            await Server.WaitRunTicks(1);
        }
    }

    [Test]
    public async Task ProductionMainframeComposesShellAndServerRuntime()
    {
        await Server.WaitAssertion(() =>
        {
            var mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineShellComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineShellRuntimeComponent>(mainframe), Is.True);
            });
            Server.EntMan.DeleteEntity(mainframe);
        });
    }
}
