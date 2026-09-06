// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.NanoXp;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.NanoXp;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey.NanoXp;

[TestFixture]
public sealed class NanoXpSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          parent: BasePDA
          id: WhiskeyNanoXpTestPda
          components:
          - type: Pda
            id: PassengerIDCard

        - type: entity
          id: WhiskeyNanoXpTestComputerAllowed
          components:
          - type: Transform
          - type: NanoXpDevice
          - type: AccessReader
            access:
            - [ Engineering ]

        - type: entity
          id: WhiskeyNanoXpTestComputerDenied
          components:
          - type: Transform
          - type: NanoXpDevice
          - type: AccessReader
            access:
            - [ Security ]

        - type: entity
          id: WhiskeyNanoXpTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task NanoXpUiKeySynchronizesToConnectedClient()
    {
        var map = await Pair.CreateTestMap();
        var pda = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            pda = SSpawnAtPosition("PassengerPDA", map.GridCoords);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var clientPda = ToClientUid(pda);
            var ui = Client.System<SharedUserInterfaceSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(CEntMan.HasComponent<NanoXpDeviceComponent>(clientPda), Is.True);
                Assert.That(ui.HasUi(clientPda, NanoXpUiKey.Key), Is.True);
                Assert.That(ui.HasUi(clientPda, PdaUiKey.Key), Is.True);
            });
        });
    }

    [Test]
    public async Task BaseDevicesGainSecondaryDesktopWithoutReplacingExistingInterfaces()
    {
        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var pda = Server.EntMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            var computer = Server.EntMan.SpawnEntity("WhiskeyDwaineTerminal", MapCoordinates.Nullspace);
            var handheld = Server.EntMan.SpawnEntity("WhiskeyDwainePortableTerminal", MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<NanoXpDeviceComponent>(pda), Is.True);
                Assert.That(Server.EntMan.GetComponent<NanoXpDeviceComponent>(pda).Kind, Is.EqualTo(NanoXpDeviceKind.Pda));
                Assert.That(ui.HasUi(pda, NanoXpUiKey.Key), Is.True);
                Assert.That(ui.HasUi(pda, PdaUiKey.Key), Is.True);

                Assert.That(Server.EntMan.HasComponent<NanoXpDeviceComponent>(computer), Is.True);
                Assert.That(ui.HasUi(computer, NanoXpUiKey.Key), Is.True);
                Assert.That(ui.HasUi(computer, DwaineTerminalUiKey.Key), Is.True);

                Assert.That(Server.EntMan.HasComponent<NanoXpDeviceComponent>(handheld), Is.True);
                Assert.That(ui.HasUi(handheld, NanoXpUiKey.Key), Is.True);
                Assert.That(ui.HasUi(handheld, DwaineTerminalUiKey.Key), Is.True);
            });

            Server.EntMan.DeleteEntity(pda);
            Server.EntMan.DeleteEntity(computer);
            Server.EntMan.DeleteEntity(handheld);
        });
    }

    [Test]
    public async Task PdaEnrollmentAndComputerLoginUseAuthoritativeIdAccess()
    {
        EntityUid map = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(System.Numerics.Vector2.Zero, mapId);
            var pdaUid = Server.EntMan.SpawnEntity("WhiskeyNanoXpTestPda", coordinates);
            var allowedUid = Server.EntMan.SpawnEntity("WhiskeyNanoXpTestComputerAllowed", coordinates);
            var deniedUid = Server.EntMan.SpawnEntity("WhiskeyNanoXpTestComputerDenied", coordinates);
            var actorUid = Server.EntMan.SpawnEntity("WhiskeyNanoXpTestActor", coordinates);
            var ui = Server.System<SharedUserInterfaceSystem>();

            // This test drives local BUI messages and does not need a network session or input validation fixture.
            ui.SetUi(pdaUid, NanoXpUiKey.Key, new InterfaceData("NanoXpBoundUserInterface", requireInputValidation: false));
            ui.SetUi(allowedUid, NanoXpUiKey.Key, new InterfaceData("NanoXpBoundUserInterface", requireInputValidation: false));
            ui.SetUi(deniedUid, NanoXpUiKey.Key, new InterfaceData("NanoXpBoundUserInterface", requireInputValidation: false));

            var pda = Server.EntMan.GetComponent<PdaComponent>(pdaUid);
            Assert.That(pda.ContainedId, Is.Not.Null);
            var idUid = pda.ContainedId!.Value;
            var id = Server.EntMan.GetComponent<IdCardComponent>(idUid);
            id.FullName = "Alex Engineer";
            id.LocalizedJobTitle = "Station Engineer";
            var jobDepartments = id.JobDepartments;
            jobDepartments.Add(new ProtoId<DepartmentPrototype>("Engineering"));
            var access = Server.EntMan.GetComponent<AccessComponent>(idUid);
            access.Tags.Add(new ProtoId<AccessLevelPrototype>("Engineering"));

            Assert.That(ui.TryOpenUi(pdaUid, NanoXpUiKey.Key, actorUid), Is.True);
            Raise(ui, pdaUid, actorUid, new NanoXpEnrollMessage("safe-password"));

            var network = Server.EntMan.GetComponent<NanoXpNetworkRuntimeComponent>(map);
            var pdaRuntime = Server.EntMan.GetComponent<NanoXpDeviceRuntimeComponent>(pdaUid);
            Assert.Multiple(() =>
            {
                Assert.That(network.Store.AccountCount, Is.EqualTo(1));
                Assert.That(network.Store.GetDirectory()[0].Address, Is.EqualTo("alex-engineer@gmail.nano"));
                Assert.That(pdaRuntime.Sessions.ContainsKey(actorUid), Is.True);
            });

            ui.CloseUi(pdaUid, NanoXpUiKey.Key, actorUid);
            Assert.That(network.Store.SessionCount, Is.Zero);

            Assert.That(ui.TryOpenUi(allowedUid, NanoXpUiKey.Key, actorUid), Is.True);
            Raise(ui, allowedUid, actorUid, new NanoXpLoginMessage("alex-engineer@gmail.nano", "safe-password"));
            Assert.That(
                Server.EntMan.GetComponent<NanoXpDeviceRuntimeComponent>(allowedUid).Sessions.ContainsKey(actorUid),
                Is.True);
            ui.CloseUi(allowedUid, NanoXpUiKey.Key, actorUid);

            Assert.That(ui.TryOpenUi(deniedUid, NanoXpUiKey.Key, actorUid), Is.True);
            Raise(ui, deniedUid, actorUid, new NanoXpLoginMessage("alex-engineer@gmail.nano", "safe-password"));
            Assert.Multiple(() =>
            {
                Assert.That(
                    Server.EntMan.GetComponent<NanoXpDeviceRuntimeComponent>(deniedUid).Sessions.ContainsKey(actorUid),
                    Is.False);
                Assert.That(network.Store.SessionCount, Is.Zero);
            });

            Server.EntMan.DeleteEntity(map);
        });
    }

    private static void Raise(
        SharedUserInterfaceSystem ui,
        EntityUid device,
        EntityUid actor,
        BoundUserInterfaceMessage message)
    {
        message.Actor = actor;
        message.UiKey = NanoXpUiKey.Key;
        ui.RaiseUiMessage(device, NanoXpUiKey.Key, message);
    }
}
