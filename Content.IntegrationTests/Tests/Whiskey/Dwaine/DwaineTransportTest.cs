// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Transport;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineTransportTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineTransportTestTerminal
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Terminal
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineTerminalLink
          - type: DwaineTerminal
            maxInputLength: 32
          - type: DwaineKeyboardInput
          - type: DwaineNetworkConnector
            networkId: test
            linkRange: 10
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineTransportTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
            maxSessions: 2
            outputLineLimit: 3
            outputCharacterLimit: 12
          - type: DwaineMainframeRuntime
          - type: DwaineNetworkConnector
            networkId: test
            linkRange: 10

        - type: entity
          id: WhiskeyDwaineTransportTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task ConnectInputOutputDisconnectAndReconnectAreAuthoritative()
    {
        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var mapSystem = Server.System<SharedMapSystem>();
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var map = mapSystem.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            var terminal = entities.SpawnEntity("WhiskeyDwaineTransportTestTerminal", origin);
            var mainframe = entities.SpawnEntity("WhiskeyDwaineTransportTestMainframe", origin);
            var actor = entities.SpawnEntity("WhiskeyDwaineTransportTestActor", origin);
            var intruder = entities.SpawnEntity("WhiskeyDwaineTransportTestActor", origin);

            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out var first),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out var duplicate),
                Is.EqualTo(DwaineConnectResult.AlreadyConnected));
            Assert.That(duplicate, Is.EqualTo(first));
            Assert.That(transport.GetSessionCount(mainframe), Is.EqualTo(1));

            ui.CloseUi(terminal, DwaineTerminalUiKey.Key, actor);
            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out var afterUiReconnect),
                Is.EqualTo(DwaineConnectResult.AlreadyConnected));
            Assert.That(afterUiReconnect, Is.EqualTo(first));

            var input = new DwaineTerminalInputReceivedEvent(actor, "status");
            entities.EventBus.RaiseLocalEvent(terminal, ref input);
            Assert.That(transport.TryReadInput(mainframe, first, out var received), Is.True);
            Assert.That(received, Is.EqualTo("status"));

            Assert.That(transport.WriteOutput(mainframe, first, "1111"), Is.True);
            Assert.That(transport.WriteOutput(mainframe, first, "2222"), Is.True);
            Assert.That(transport.WriteOutput(mainframe, first, "3333"), Is.True);
            Assert.That(transport.WriteOutput(mainframe, first, "4444"), Is.True);
            var runtime = entities.GetComponent<DwaineMainframeRuntimeComponent>(mainframe);
            Assert.That(runtime.Sessions[first].Output.Snapshot(), Is.EqualTo(new[] { "2222", "3333", "4444" }));

            Assert.That(transport.TryDisconnect(terminal, intruder), Is.False);
            Assert.That(transport.TryDisconnect(terminal, actor), Is.True);
            Assert.That(transport.GetSessionCount(mainframe), Is.Zero);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out var second),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(second, Is.Not.EqualTo(first));
            entities.DeleteEntity(actor);
            transport.ValidateAllSessions();
            Assert.That(transport.GetSessionCount(mainframe), Is.Zero);

            entities.DeleteEntity(map);
        });
    }

    [Test]
    public async Task MultipleMachinesDeletionAndTopologyChangesCleanSessions()
    {
        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var mapSystem = Server.System<SharedMapSystem>();
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var hardware = Server.System<DwaineHardwareSystem>();
            var map = mapSystem.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            var terminalA = entities.SpawnEntity("WhiskeyDwaineTransportTestTerminal", origin);
            var terminalB = entities.SpawnEntity("WhiskeyDwaineTransportTestTerminal", origin);
            var terminalC = entities.SpawnEntity("WhiskeyDwaineTransportTestTerminal", origin);
            var mainframeA = entities.SpawnEntity("WhiskeyDwaineTransportTestMainframe", origin);
            var mainframeB = entities.SpawnEntity("WhiskeyDwaineTransportTestMainframe", origin);
            var actorA = entities.SpawnEntity("WhiskeyDwaineTransportTestActor", origin);
            var actorB = entities.SpawnEntity("WhiskeyDwaineTransportTestActor", origin);
            var actorC = entities.SpawnEntity("WhiskeyDwaineTransportTestActor", origin);
            var linkB = entities.GetComponent<DwaineTerminalLinkComponent>(terminalB);

            ui.TryOpenUi(terminalA, DwaineTerminalUiKey.Key, actorA);
            ui.TryOpenUi(terminalB, DwaineTerminalUiKey.Key, actorB);
            ui.TryOpenUi(terminalC, DwaineTerminalUiKey.Key, actorC);
            Assert.That(transport.TryConnect(terminalA, mainframeA, actorA, out _),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(transport.TryConnect(terminalB, mainframeA, actorB, out _),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(transport.GetSessionCount(mainframeA), Is.EqualTo(2));
            Assert.That(transport.TryConnect(terminalC, mainframeA, actorC, out _),
                Is.EqualTo(DwaineConnectResult.CapacityReached));
            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out _),
                Is.EqualTo(DwaineConnectResult.TerminalAlreadyConnected));

            entities.DeleteEntity(mainframeA);
            Assert.Multiple(() =>
            {
                Assert.That(linkB.Session, Is.Null);
                Assert.That(linkB.PresentationStatus, Is.EqualTo(DwaineTerminalConnectionStatus.MainframeUnavailable));
            });

            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out var outputSession),
                Is.EqualTo(DwaineConnectResult.Connected));
            var network = entities.GetComponent<DwaineNetworkConnectorComponent>(mainframeB);
            network.NetworkId = "partitioned";
            Assert.That(transport.WriteOutput(mainframeB, outputSession, "blocked"), Is.False);
            Assert.That(transport.GetTerminalSession(terminalA), Is.Null);

            network.NetworkId = "test";
            transport.ValidateAllSessions();
            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out _),
                Is.EqualTo(DwaineConnectResult.Connected));
            hardware.SetPowerEnabled(mainframeB, false);
            Assert.That(transport.GetTerminalSession(terminalA), Is.Null);
            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out _),
                Is.EqualTo(DwaineConnectResult.MainframeUnavailable));
            hardware.SetPowerEnabled(mainframeB, true);
            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out var inputSession),
                Is.EqualTo(DwaineConnectResult.Connected));

            var terminalNetwork = entities.GetComponent<DwaineNetworkConnectorComponent>(terminalA);
            terminalNetwork.NetworkId = "partitioned";
            var blockedInput = new DwaineTerminalInputReceivedEvent(actorA, "blocked");
            entities.EventBus.RaiseLocalEvent(terminalA, ref blockedInput);
            Assert.That(transport.GetTerminalSession(terminalA), Is.Null);
            Assert.That(transport.TryReadInput(mainframeB, inputSession, out _), Is.False);

            terminalNetwork.NetworkId = "test";
            transport.ValidateAllSessions();
            Assert.That(transport.TryConnect(terminalA, mainframeB, actorA, out _),
                Is.EqualTo(DwaineConnectResult.Connected));
            entities.DeleteEntity(terminalA);
            Assert.That(transport.GetSessionCount(mainframeB), Is.Zero);

            entities.DeleteEntity(map);
        });
    }

    [Test]
    public async Task InvalidTargetsRangeAndProductionPrototypeHaveTransportComponents()
    {
        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var mapSystem = Server.System<SharedMapSystem>();
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var map = mapSystem.CreateMap(out var mapId);
            var terminal = entities.SpawnEntity(
                "WhiskeyDwaineTransportTestTerminal",
                new MapCoordinates(Vector2.Zero, mapId));
            var farMainframe = entities.SpawnEntity(
                "WhiskeyDwaineTransportTestMainframe",
                new MapCoordinates(new Vector2(20, 0), mapId));
            var actor = entities.SpawnEntity(
                "WhiskeyDwaineTransportTestActor",
                new MapCoordinates(Vector2.Zero, mapId));
            var production = entities.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);

            ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor);
            Assert.Multiple(() =>
            {
                Assert.That(transport.TryConnect(terminal, EntityUid.Invalid, actor, out _),
                    Is.EqualTo(DwaineConnectResult.InvalidMainframe));
                Assert.That(transport.TryConnect(terminal, farMainframe, actor, out _),
                    Is.EqualTo(DwaineConnectResult.OutOfRange));
                Assert.That(entities.HasComponent<DwaineMainframeComponent>(production), Is.True);
                Assert.That(entities.HasComponent<DwaineMainframeRuntimeComponent>(production), Is.True);
                Assert.That(entities.HasComponent<DwaineKernelComponent>(production), Is.True);
                Assert.That(entities.HasComponent<DwaineKernelRuntimeComponent>(production), Is.True);
                Assert.That(entities.HasComponent<DwaineProcessSchedulerComponent>(production), Is.True);
                Assert.That(entities.HasComponent<DwaineProcessRuntimeComponent>(production), Is.True);
            });

            entities.DeleteEntity(production);
            entities.DeleteEntity(map);
        });
    }
}
