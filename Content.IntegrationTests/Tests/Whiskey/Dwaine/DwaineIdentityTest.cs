// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Identity;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Numerics;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineIdentityTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineIdentityTestMainframe
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
          - type: DwaineIdentity
            maxAccounts: 8
            maxGroups: 8
            maxSessions: 4
            sessionLifetimeSeconds: 300
          - type: DwaineIdentityRuntime
          - type: DwaineStorageConnector
            slotCount: 1
          - type: DwaineNetworkConnector
            networkId: identity-test
            linkRange: 10

        - type: entity
          id: WhiskeyDwaineIdentityTestTerminal
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
            networkId: identity-test
            linkRange: 10
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineIdentityTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task TransportLifecycleOwnsLoginLogoutReconnectAndRebootSessions()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        EntityUid terminal = EntityUid.Invalid;
        EntityUid actor = EntityUid.Invalid;
        DwaineSessionId transportSession = default;
        DwainePrincipalId accountPrincipal = default;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineIdentityTestMainframe", origin);
            terminal = Server.EntMan.SpawnEntity("WhiskeyDwaineIdentityTestTerminal", origin);
            actor = Server.EntMan.SpawnEntity("WhiskeyDwaineIdentityTestActor", origin);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var ui = Server.System<SharedUserInterfaceSystem>();
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var identities = Server.System<DwaineIdentitySystem>();
            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out transportSession),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(identities.TryGetSession(mainframe, transportSession, out var guest),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(guest.Temporary, Is.True);
            Assert.That(identities.TryGetStore(mainframe, out var store), Is.True);
            Assert.That(store.TryCreateAccount("alex", "safe-password", false, out var account),
                Is.EqualTo(DwaineIdentityResult.Success));
            accountPrincipal = account.Principal;
            Assert.That(identities.TryLogin(mainframe, new DwaineSessionId(ulong.MaxValue), "alex", "safe-password", out _),
                Is.EqualTo(DwaineIdentityResult.SessionNotFound));
            Assert.That(identities.TryLogin(mainframe, transportSession, "alex", "safe-password", out var login),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(login.Principal, Is.EqualTo(accountPrincipal));
                Assert.That(login.Temporary, Is.False);
                Assert.That(store.AccountCount, Is.EqualTo(1));
            });

            Assert.That(transport.TryDisconnect(terminal, actor), Is.True);
            Assert.That(store.SessionCount, Is.Zero);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out transportSession),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(identities.TryGetSession(mainframe, transportSession, out guest),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(guest.Temporary, Is.True);
            Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframe), Is.True);
        });

        await Server.WaitRunTicks(4);
        await Server.WaitAssertion(() =>
        {
            var runtime = Server.EntMan.GetComponent<DwaineIdentityRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Online, Is.False);
                Assert.That(runtime.Store, Is.Not.Null);
                Assert.That(runtime.Store!.SessionCount, Is.Zero);
                Assert.That(runtime.Store.TryGetAccount(accountPrincipal, out _), Is.True);
            });
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var identities = Server.System<DwaineIdentitySystem>();
            Assert.That(identities.TryGetSession(mainframe, transportSession, out var guest),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(guest.Temporary, Is.True);
            Assert.That(identities.TryLogin(mainframe, transportSession, "alex", "safe-password", out var login),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(login.Principal, Is.EqualTo(accountPrincipal));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProductionMainframeContainsIdentityRuntime()
    {
        await Server.WaitAssertion(() =>
        {
            var mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineIdentityComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineIdentityRuntimeComponent>(mainframe), Is.True);
            });
            Server.EntMan.DeleteEntity(mainframe);
        });
    }
}
