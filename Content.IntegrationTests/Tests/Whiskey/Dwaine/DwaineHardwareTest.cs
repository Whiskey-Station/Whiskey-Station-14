// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineHardwareTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineHardwareTestTerminal
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Terminal
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineTerminal
            maxInputLength: 32
            outputLineLimit: 3
            outputCharacterLimit: 16
          - type: DwaineDisplay
          - type: DwaineKeyboardInput
          - type: DwaineStorageConnector
          - type: DwaineNetworkConnector
          - type: DwaineDeviceBusEndpoint
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineHardwareTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task PrototypeComposesOnlyPhysicalTerminalLayer()
    {
        await Server.WaitAssertion(() =>
        {
            var uid = Server.EntMan.SpawnEntity("WhiskeyDwaineTerminal", MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineComputerHardwareComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineTerminalComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineDisplayComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineKeyboardInputComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineStorageConnectorComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineNetworkConnectorComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineDeviceBusEndpointComponent>(uid), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineHardwareRuntimeComponent>(uid), Is.True);
                Assert.That(typeof(DwaineHardwareRuntimeComponent).GetFields()
                        .Any(field => field.FieldType == typeof(DwaineBootStage)),
                    Is.False,
                    "PR 02 must not pretend that a boot stage exists.");
            });

            Server.EntMan.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task PowerLifecycleAndInvalidEntityAreServerAuthoritative()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<DwaineHardwareSystem>();
            var uid = Server.EntMan.SpawnEntity("WhiskeyDwaineHardwareTestTerminal", MapCoordinates.Nullspace);

            Assert.That(system.GetStatus(uid), Is.EqualTo(DwaineHardwareStatus.HardwareReady));
            Assert.That(system.SetPowerEnabled(uid, false), Is.True);
            Assert.That(system.GetStatus(uid), Is.EqualTo(DwaineHardwareStatus.PoweredOff));
            Assert.That(system.SetPowerEnabled(uid, true), Is.True);
            Assert.That(system.SetPowerSupply(uid, false), Is.True);
            Assert.That(system.GetStatus(uid), Is.EqualTo(DwaineHardwareStatus.PowerUnavailable));
            Assert.That(system.SetPowerSupply(uid, true), Is.True);
            Assert.That(system.GetStatus(uid), Is.EqualTo(DwaineHardwareStatus.HardwareReady));

            Assert.Multiple(() =>
            {
                Assert.That(system.SetPowerSupply(EntityUid.Invalid, true), Is.False);
                Assert.That(system.SetPowerEnabled(EntityUid.Invalid, true), Is.False);
                Assert.That(system.TryTogglePower(EntityUid.Invalid), Is.False);
                Assert.That(system.WriteServerText(EntityUid.Invalid, "ignored"), Is.False);
            });

            Server.EntMan.DeleteEntity(uid);
            Assert.That(system.GetStatus(uid), Is.Null);
        });
    }

    [Test]
    public async Task BuiReconnectIsIdempotentAndDestructionCleansPresentationState()
    {
        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var hardware = Server.System<DwaineHardwareSystem>();
            var terminal = Server.EntMan.SpawnEntity("WhiskeyDwaineHardwareTestTerminal", MapCoordinates.Nullspace);
            var actor = Server.EntMan.SpawnEntity("WhiskeyDwaineHardwareTestActor", MapCoordinates.Nullspace);
            var runtime = Server.EntMan.GetComponent<DwaineHardwareRuntimeComponent>(terminal);

            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(hardware.GetActiveViewerCount(terminal), Is.EqualTo(1));

            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(hardware.GetActiveViewerCount(terminal), Is.EqualTo(1));

            ui.CloseUi(terminal, DwaineTerminalUiKey.Key, actor);
            Assert.That(hardware.GetActiveViewerCount(terminal), Is.Zero);

            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(hardware.GetActiveViewerCount(terminal), Is.EqualTo(1));
            Assert.That(hardware.WriteServerText(terminal, "server output"), Is.True);

            Server.EntMan.DeleteEntity(terminal);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.ActiveViewers, Is.Empty);
                Assert.That(runtime.Output?.Count, Is.Zero);
                Assert.That(hardware.GetActiveViewerCount(terminal), Is.Zero);
            });

            Server.EntMan.DeleteEntity(actor);
        });
    }
}
