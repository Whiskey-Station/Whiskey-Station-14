// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Network;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineNetworkTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineNetworkTestRadioA
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: network-test
            address: radio-a
            adapter: Radio
            tags: [source]
            frequency: 1459
            channel: station
            linkRange: 10
          - type: DwaineNetworkEndpoint
            maxPayloadCharacters: 64
            maxPendingRequests: 2
            maxDiscoveryResults: 8
            maxCaptureEntries: 8
            discoveryCooldownSeconds: 0.1
            requestTimeoutSeconds: 0.1

        - type: entity
          id: WhiskeyDwaineNetworkTestRadioB
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: network-test
            address: radio-b
            adapter: Radio
            tags: [service, target]
            frequency: 1459
            channel: station
            linkRange: 10
          - type: DwaineNetworkEndpoint
            maxPayloadCharacters: 64

        - type: entity
          id: WhiskeyDwaineNetworkTestOtherNetwork
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: isolated
            address: isolated
            adapter: Radio
            frequency: 1459
            channel: station
            linkRange: 10
          - type: DwaineNetworkEndpoint

        - type: entity
          id: WhiskeyDwaineNetworkTestDuplicate
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: network-test
            address: radio-b
            adapter: Radio
            frequency: 1459
            channel: station
            linkRange: 10
          - type: DwaineNetworkEndpoint

        - type: entity
          id: WhiskeyDwaineNetworkTestFarRadio
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: network-test
            address: far-radio
            adapter: Radio
            frequency: 1459
            channel: station
            linkRange: 10
          - type: DwaineNetworkEndpoint

        - type: entity
          id: WhiskeyDwaineNetworkTestWiredA
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: wired-segment
            address: wired-a
            adapter: Wired
            frequency: 0
            channel: ""
            linkRange: 0
          - type: DwaineNetworkEndpoint

        - type: entity
          id: WhiskeyDwaineNetworkTestWiredB
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: wired-segment
            address: wired-b
            adapter: Wired
            frequency: 0
            channel: ""
            linkRange: 0
          - type: DwaineNetworkEndpoint

        - type: entity
          id: WhiskeyDwaineNetworkTestJammer
          components:
          - type: Transform
          - type: DwaineNetworkJammer
            networkId: network-test
            frequency: 1459
            channel: station
            range: 5

        - type: entity
          id: WhiskeyDwaineNetworkTestRecoveryProvider
          components:
          - type: Transform
          - type: DwaineNetworkConnector
            networkId: recovery-test
            address: recovery
            adapter: Wired
          - type: DwaineNetworkEndpoint
          - type: DwaineNetworkBootProvider
            recoveryProfile: whiskey-recovery-v1

        - type: entity
          id: WhiskeyDwaineNetworkTestRecoveryClient
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
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineNetworkConnector
            networkId: recovery-test
            address: recovery-client
            adapter: Wired
          - type: DwaineNetworkEndpoint
          - type: DwaineNetworkBootClient
            providerAddress: recovery
            recoveryProfile: whiskey-recovery-v1

        - type: entity
          id: WhiskeyDwaineNetworkTestMainframeA
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
            requireStorageConnector: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineIdentity
            maxAccounts: 8
            maxGroups: 8
            maxSessions: 8
          - type: DwaineIdentityRuntime
          - type: DwaineFileSystem
            maxNodes: 128
          - type: DwaineFileSystemRuntime
          - type: DwaineNetworkConnector
            networkId: communications-test
            address: mainframe-a
            adapter: Wired
            tags: [mainframe]
          - type: DwaineNetworkEndpoint
          - type: DwaineCommunicationService
            maxMessages: 4
            maxMessagesPerUser: 2
            maxMessageCharacters: 64
          - type: DwaineCommunicationRuntime

        - type: entity
          id: WhiskeyDwaineNetworkTestMainframeB
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
            requireStorageConnector: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineIdentity
            maxAccounts: 8
            maxGroups: 8
            maxSessions: 8
          - type: DwaineIdentityRuntime
          - type: DwaineFileSystem
            maxNodes: 128
          - type: DwaineFileSystemRuntime
          - type: DwaineNetworkConnector
            networkId: communications-test
            address: mainframe-b
            adapter: Wired
            tags: [mainframe]
          - type: DwaineNetworkEndpoint
          - type: DwaineCommunicationService
            maxMessages: 4
            maxMessagesPerUser: 2
            maxMessageCharacters: 64
          - type: DwaineCommunicationRuntime
        """;

    [Test]
    public async Task TopologyDiscoveryRoutingTimeoutsAndCleanupAreBounded()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid radioA = EntityUid.Invalid;
        EntityUid radioB = EntityUid.Invalid;
        EntityUid otherNetwork = EntityUid.Invalid;
        EntityUid duplicate = EntityUid.Invalid;
        EntityUid farRadio = EntityUid.Invalid;
        EntityUid wiredA = EntityUid.Invalid;
        EntityUid wiredB = EntityUid.Invalid;
        EntityUid jammer = EntityUid.Invalid;
        EntityUid recoveryClient = EntityUid.Invalid;
        DwaineNetworkCorrelationId pending = default;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            radioA = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestRadioA",
                new MapCoordinates(Vector2.Zero, mapId));
            radioB = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestRadioB",
                new MapCoordinates(new Vector2(5, 0), mapId));
            otherNetwork = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestOtherNetwork",
                new MapCoordinates(Vector2.Zero, mapId));
            duplicate = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestDuplicate",
                new MapCoordinates(Vector2.Zero, mapId));
            farRadio = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestFarRadio",
                new MapCoordinates(new Vector2(20, 0), mapId));
            wiredA = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestWiredA",
                new MapCoordinates(Vector2.Zero, mapId));
            wiredB = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestWiredB",
                new MapCoordinates(new Vector2(100, 0), mapId));
            Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestRecoveryProvider", origin);
            recoveryClient = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestRecoveryClient", origin);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(recoveryClient), Is.True);

            var network = Server.System<DwaineNetworkSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(network.GetNode(radioA, out var source), Is.EqualTo(DwaineNetworkResult.Success));
                Assert.That(source.Address.Value, Is.EqualTo("radio-a"));
                Assert.That(network.GetNode(duplicate, out _), Is.EqualTo(DwaineNetworkResult.DuplicateAddress));
                Assert.That(network.CanReach(radioA, radioB), Is.EqualTo(DwaineNetworkResult.Success));
                Assert.That(network.CanReach(radioA, otherNetwork), Is.EqualTo(DwaineNetworkResult.CrossNetwork));
                Assert.That(network.CanReach(radioA, farRadio), Is.EqualTo(DwaineNetworkResult.OutOfRange));
                Assert.That(network.CanReach(wiredA, wiredB), Is.EqualTo(DwaineNetworkResult.Success));
            });

            Assert.That(network.Discover(radioA, "service", out var discovered),
                Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(discovered.Select(entry => entry.Address.Value), Is.EqualTo(new[] { "radio-b" }));
            Assert.That(network.Discover(radioA, null, out _), Is.EqualTo(DwaineNetworkResult.RateLimited));
            Assert.That(network.TryRequest(radioA, "radio-b", "dwaine.ping", string.Empty, out var ping),
                Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(network.TryTakeReply(radioB, ping, out _), Is.EqualTo(DwaineNetworkResult.NotFound));
            Assert.That(network.TryTakeReply(radioA, ping, out var pong), Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(pong, Is.EqualTo("pong"));
            Assert.That(network.TryTakeReply(radioA, ping, out _), Is.EqualTo(DwaineNetworkResult.NotFound));
            Assert.That(network.TrySend(radioA, "radio-b", "unknown.protocol", "ignored"),
                Is.EqualTo(DwaineNetworkResult.Unsupported));
            Assert.That(network.TryRequest(radioA, "radio-b", "unknown.protocol", "pending", out pending),
                Is.EqualTo(DwaineNetworkResult.Pending));
            Assert.That(network.TrySend(radioA, "radio-b", "unknown.protocol", new string('x', 65)),
                Is.EqualTo(DwaineNetworkResult.PayloadTooLarge));

            jammer = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestJammer",
                new MapCoordinates(Vector2.Zero, mapId));
            Assert.That(network.CanReach(radioA, radioB), Is.EqualTo(DwaineNetworkResult.Interfered));
            Server.EntMan.DeleteEntity(jammer);
            Assert.That(network.CanReach(radioA, radioB), Is.EqualTo(DwaineNetworkResult.Success));

            var destinationConnector = Server.EntMan.GetComponent<DwaineNetworkConnectorComponent>(radioB);
            destinationConnector.NetworkId = "partitioned";
            Assert.That(network.CanReach(radioA, radioB), Is.EqualTo(DwaineNetworkResult.CrossNetwork));
            destinationConnector.NetworkId = "network-test";
            destinationConnector.Enabled = false;
            Assert.That(network.CanReach(radioA, radioB), Is.EqualTo(DwaineNetworkResult.Disabled));
            destinationConnector.Enabled = true;
        });

        await Server.WaitRunTicks(10);
        await Server.WaitAssertion(() =>
        {
            var network = Server.System<DwaineNetworkSystem>();
            Assert.That(Server.System<DwaineKernelSystem>().GetState(recoveryClient),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(network.TryTakeReply(radioA, pending, out _), Is.EqualTo(DwaineNetworkResult.Timeout));
            var metrics = network.GetMetrics(radioA);
            Assert.Multiple(() =>
            {
                Assert.That(metrics.Sent, Is.GreaterThanOrEqualTo(3));
                Assert.That(metrics.Dropped, Is.GreaterThanOrEqualTo(1));
                Assert.That(metrics.PendingRequests, Is.Zero);
                Assert.That(metrics.CapturedEntries, Is.LessThanOrEqualTo(8));
                Assert.That(network.GetCapture(radioA).All(entry => entry.PayloadCharacters >= 0), Is.True);
            });

            Assert.That(network.TryRequest(radioA, "radio-b", "unknown.protocol", "disconnect", out var disconnect),
                Is.EqualTo(DwaineNetworkResult.Pending));
            Server.EntMan.DeleteEntity(radioB);
            Assert.That(network.TryTakeReply(radioA, disconnect, out _), Is.EqualTo(DwaineNetworkResult.Disconnected));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task CommunicationsDeriveIdentityEnforceMailboxesAndStopWithKernel()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframeA = EntityUid.Invalid;
        EntityUid mainframeB = EntityUid.Invalid;
        DwainePrincipalId alice = default;
        DwainePrincipalId bob = default;
        DwainePrincipalId charlie = default;

        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            mainframeA = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestMainframeA", origin);
            mainframeB = Server.EntMan.SpawnEntity("WhiskeyDwaineNetworkTestMainframeB", origin);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframeA), Is.True);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframeB), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var identities = Server.System<DwaineIdentitySystem>();
            Assert.That(identities.TryGetStore(mainframeA, out var storeA), Is.True);
            Assert.That(identities.TryGetStore(mainframeB, out var storeB), Is.True);
            Assert.That(storeA.TryCreateAccount("alice", "safe-password", false, out var aliceAccount),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(storeB.TryCreateAccount("bob", "safe-password", false, out var bobAccount),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(storeB.TryCreateAccount("charlie", "safe-password", false, out var charlieAccount),
                Is.EqualTo(DwaineIdentityResult.Success));
            alice = aliceAccount.Principal;
            bob = bobAccount.Principal;
            charlie = charlieAccount.Principal;

            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframeA, out var fileSystemA), Is.True);
            Assert.That(fileSystems.TryGetFileSystem(mainframeB, out var fileSystemB), Is.True);
            Assert.That(fileSystemA.TryCreateDirectory("/home/alice", fileSystemA.Root, TimeSpan.Zero, out var aliceHome),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(fileSystemA.TrySetMetadata(aliceHome, alice.Value, DwaineGroupId.Users.Value,
                DwaineVfsMode.OwnerAll, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(fileSystemB.TryCreateDirectory("/home/bob", fileSystemB.Root, TimeSpan.Zero, out var bobHome),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(fileSystemB.TrySetMetadata(bobHome, bob.Value, DwaineGroupId.Users.Value,
                DwaineVfsMode.OwnerAll, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
            var filesA = new DwaineAuthorizedFileSystem(fileSystemA, storeA);
            Assert.That(filesA.TryCreateText(alice, "/home/alice/report.txt", fileSystemA.Root,
                "bounded report", null, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));

            var communications = Server.System<DwaineCommunicationSystem>();
            var network = Server.System<DwaineNetworkSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(communications.TrySend(mainframeA, new DwainePrincipalId(9999),
                    "mainframe-b", "bob", "forged"), Is.EqualTo(DwaineNetworkResult.Disabled));
                Assert.That(communications.TrySend(mainframeA, alice,
                    "mainframe-b", "missing", "hello"), Is.EqualTo(DwaineNetworkResult.NotFound));
                Assert.That(communications.TrySend(mainframeA, alice,
                    "mainframe-b", "bob", "first"), Is.EqualTo(DwaineNetworkResult.Success));
                Assert.That(communications.TrySend(mainframeA, alice,
                    "mainframe-b", "bob", "second"), Is.EqualTo(DwaineNetworkResult.Success));
                Assert.That(communications.TrySend(mainframeA, alice,
                    "mainframe-b", "bob", "third"), Is.EqualTo(DwaineNetworkResult.CapacityReached));
            });
            Assert.That(network.TryRequest(mainframeA, "mainframe-b", "dwaine.message",
                "mallory\nbob\nforged", out var forged), Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(network.TryTakeReply(mainframeA, forged, out var forgedReply),
                Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(forgedReply, Is.EqualTo("invalid-sender"));
            Assert.That(communications.TrySendFile(mainframeA, alice, "mainframe-b", "bob",
                "/home/alice/report.txt", fileSystemA.Root, out var receivedPath),
                Is.EqualTo(DwaineNetworkResult.Success));
            Assert.That(receivedPath, Is.EqualTo("/home/bob/inbox/report.txt"));
            var filesB = new DwaineAuthorizedFileSystem(fileSystemB, storeB);
            Assert.That(filesB.TryReadText(bob, receivedPath, fileSystemB.Root, out var receivedText),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(receivedText, Is.EqualTo("bounded report"));

            Assert.That(communications.TryReceive(mainframeB, bob, out var first),
                Is.EqualTo(DwaineNetworkResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(first.SourceAddress, Is.EqualTo("mainframe-a"));
                Assert.That(first.Sender, Is.EqualTo("alice"));
                Assert.That(first.Message, Is.EqualTo("first"));
            });
            Assert.That(communications.TryReceive(mainframeB, charlie, out _),
                Is.EqualTo(DwaineNetworkResult.NotFound));
            Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframeB), Is.True);
        });

        await Server.WaitRunTicks(4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineCommunicationSystem>().TryReceive(mainframeB, bob, out _),
                Is.EqualTo(DwaineNetworkResult.Disabled));
            Server.EntMan.DeleteEntity(map);
        });
    }
}
