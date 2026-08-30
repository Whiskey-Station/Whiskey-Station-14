// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Serilog.Events;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineKernelTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineKernelTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
            outputLineLimit: 16
            outputCharacterLimit: 2048
          - type: DwaineMainframeRuntime
          - type: DwaineKernel
            autoBoot: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineStorageConnector
            slotCount: 1
          - type: DwaineNetworkConnector
            networkId: kernel-test
            linkRange: 10

        - type: entity
          id: WhiskeyDwaineKernelTestNoStorage
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
          - type: DwaineNetworkConnector
            networkId: kernel-test
            linkRange: 10

        - type: entity
          id: WhiskeyDwaineKernelTestTerminal
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
            networkId: kernel-test
            linkRange: 10
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineKernelTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task BootShutdownAndRepeatedRebootAreDeterministic()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        var service = new RecordingService();

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineKernelTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            var kernel = Server.System<DwaineKernelSystem>();
            Assert.That(kernel.TryBoot(mainframe), Is.True);
            Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.PowerOnSelfTest));
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.SystemReady));
                Assert.That(runtime.BootGeneration, Is.EqualTo(1));
                Assert.That(runtime.Diagnostics.Snapshot().Select(entry => entry.State), Is.EqualTo(new[]
                {
                    DwaineSystemState.PowerOnSelfTest,
                    DwaineSystemState.Bootloader,
                    DwaineSystemState.KernelInitializing,
                    DwaineSystemState.SystemReady,
                }));
                Assert.That(kernel.TryGetClock(mainframe, out var clock), Is.True);
                Assert.That(clock.Running, Is.True);
                Assert.That(kernel.TryRegisterService(mainframe, "test-service", service), Is.True);
                Assert.That(kernel.TryReboot(mainframe), Is.True);
                Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.ShuttingDown));
            });
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.SystemReady));
                Assert.That(runtime.BootGeneration, Is.EqualTo(2));
                Assert.That(service.Calls, Is.EqualTo(1));
                Assert.That(service.LastReason, Is.EqualTo(DwaineKernelShutdownReason.Reboot));
                Assert.That(kernel.TryShutdown(mainframe), Is.True);
            });
        });

        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.PoweredOff));
            Assert.That(kernel.TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.SystemReady));
                Assert.That(runtime.BootGeneration, Is.EqualTo(3));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
#if DEBUG
    [Ignore("Release-only fallback; DebugOpt deliberately stops at the lifecycle invariant assertion.")]
#endif
    public async Task StaleServicesAreShutdownBeforeBootInRelease()
    {
        static bool IgnoreExpectedInvariantLog(string sawmill, LogEvent message)
        {
            return sawmill == "whiskey.dwaine.kernel"
                   && message.RenderMessage().Contains("forcing a bounded cleanup before boot");
        }

        Pair.ServerLogHandler.JudgeLog += IgnoreExpectedInvariantLog;
        try
        {
            await Server.WaitAssertion(() =>
            {
                var maps = Server.System<SharedMapSystem>();
                var map = maps.CreateMap(out var mapId);
                var mainframe = Server.EntMan.SpawnEntity(
                    "WhiskeyDwaineKernelTestMainframe",
                    new MapCoordinates(Vector2.Zero, mapId));
                var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
                var staleService = new RecordingService();

                // Direct registry access deliberately simulates a violated lifecycle invariant.
                Assert.That(runtime.Services.TryRegister("stale-service", staleService), Is.True);
                Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(staleService.Calls, Is.EqualTo(1));
                    Assert.That(staleService.LastReason, Is.EqualTo(DwaineKernelShutdownReason.BootFailed));
                    Assert.That(runtime.Services.Count, Is.Zero);
                    Assert.That(runtime.State, Is.EqualTo(DwaineSystemState.PowerOnSelfTest));
                });

                Server.EntMan.DeleteEntity(map);
            });
        }
        finally
        {
            Pair.ServerLogHandler.JudgeLog -= IgnoreExpectedInvariantLog;
        }
    }

    [Test]
    public async Task FailedBootPanicPowerLossAndDeletionAreContained()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid missingStorage = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        EntityUid deletedDuringBoot = EntityUid.Invalid;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            missingStorage = Server.EntMan.SpawnEntity("WhiskeyDwaineKernelTestNoStorage", origin);
            mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineKernelTestMainframe", origin);
            deletedDuringBoot = Server.EntMan.SpawnEntity("WhiskeyDwaineKernelTestMainframe", origin);
            var kernel = Server.System<DwaineKernelSystem>();
            Assert.That(kernel.TryBoot(missingStorage), Is.True);
            Assert.That(kernel.TryBoot(deletedDuringBoot), Is.True);
            Server.EntMan.DeleteEntity(deletedDuringBoot);
        });

        await Server.WaitRunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            var failed = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(missingStorage);
            Assert.Multiple(() =>
            {
                Assert.That(failed.State, Is.EqualTo(DwaineSystemState.BootFailed));
                Assert.That(failed.Failure, Is.EqualTo(DwaineBootFailure.StorageUnavailable));
                Assert.That(kernel.TryBoot(mainframe), Is.True);
            });
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            Assert.That(kernel.Panic(mainframe, "TEST\nSTACK"), Is.True);
            var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.State, Is.EqualTo(DwaineSystemState.KernelPanic));
                Assert.That(runtime.Failure, Is.EqualTo(DwaineBootFailure.KernelPanic));
                Assert.That(runtime.Diagnostics.Snapshot()[^1].Code, Is.EqualTo("teststack"));
                Assert.That(kernel.TryReboot(mainframe), Is.True);
            });
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var kernel = Server.System<DwaineKernelSystem>();
            var hardware = Server.System<DwaineHardwareSystem>();
            Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.SystemReady));
            hardware.SetPowerEnabled(mainframe, false);
            var runtime = Server.EntMan.GetComponent<DwaineKernelRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.State, Is.EqualTo(DwaineSystemState.PoweredOff));
                Assert.That(runtime.Failure, Is.EqualTo(DwaineBootFailure.PowerLost));
            });
            hardware.SetPowerEnabled(mainframe, true);
            Assert.That(kernel.GetState(mainframe), Is.EqualTo(DwaineSystemState.PoweredOff));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ConnectedTerminalsReceiveBoundedBootDiagnostics()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineSessionId session = default;

        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var maps = Server.System<SharedMapSystem>();
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var kernel = Server.System<DwaineKernelSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            var terminal = entities.SpawnEntity("WhiskeyDwaineKernelTestTerminal", origin);
            mainframe = entities.SpawnEntity("WhiskeyDwaineKernelTestMainframe", origin);
            var actor = entities.SpawnEntity("WhiskeyDwaineKernelTestActor", origin);
            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out session),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(kernel.TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var runtime = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe);
            var output = runtime.Sessions[session].Output.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(output, Has.Length.EqualTo(4));
                Assert.That(output[0], Does.Contain("post-start"));
                Assert.That(output[^1], Does.Contain("system-ready"));
                Assert.That(output.All(line => !line.Contains("Exception", StringComparison.Ordinal)), Is.True);
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    private sealed class RecordingService : IDwaineKernelService
    {
        public int Calls { get; private set; }
        public DwaineKernelShutdownReason LastReason { get; private set; }

        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            Calls++;
            LastReason = context.Reason;
        }
    }
}
