using Content.IntegrationTests.Fixtures;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.Throwing;
using Content.Trauma.Shared.Botany.PlantAnalyzer;
using Content.Trauma.Shared.Viewcone;
using Content.Trauma.Shared.Viewcone.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Networking;

[TestFixture]
public sealed class ReplicatedEntityReferenceLifecycleTest : GameTest
{
    [Test]
    public async Task ReferencesAreClearedBeforeTargetsTerminate()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var map = await Pair.CreateTestMap();
        var coordinates = map.GridCoords;
        var thrownSystem = entMan.System<ThrownItemSystem>();
        var viewconeSystem = entMan.System<ViewconeEffectSystem>();
        var deviceListSystem = entMan.System<DeviceListSystem>();
        var configuratorSystem = entMan.System<NetworkConfiguratorSystem>();

        EntityUid source = default;
        EntityUid thrownUid = default;
        EntityUid analyzerUid = default;
        EntityUid effectUid = default;
        EntityUid deviceUid = default;
        EntityUid listUid = default;
        EntityUid activeListUid = default;
        EntityUid configuratorUid = default;
        ThrownItemComponent thrown = default!;
        PlantAnalyzerComponent analyzer = default!;
        ViewconeOccludableComponent effect = default!;
        DeviceListComponent list = default!;
        NetworkConfiguratorComponent configurator = default!;

        await server.WaitAssertion(() =>
        {
            source = entMan.SpawnEntity(null, coordinates);

            thrownUid = entMan.SpawnEntity(null, coordinates);
            thrown = entMan.AddComponent<ThrownItemComponent>(thrownUid);
            thrownSystem.SetThrower((thrownUid, thrown), source);

            analyzerUid = entMan.SpawnEntity(null, coordinates);
            analyzer = new PlantAnalyzerComponent
            {
                Scanned = source,
                Plant = source,
            };
            entMan.AddComponent(analyzerUid, analyzer);

            effectUid = entMan.SpawnEntity("ViewconeEffectTalk", coordinates);
            effect = entMan.GetComponent<ViewconeOccludableComponent>(effectUid);
            viewconeSystem.SetSource((effectUid, effect), source);

            deviceUid = entMan.SpawnEntity(null, coordinates);
            entMan.AddComponent<DeviceNetworkComponent>(deviceUid);
            listUid = entMan.SpawnEntity(null, coordinates);
            list = entMan.AddComponent<DeviceListComponent>(listUid);
            Assert.That(deviceListSystem.UpdateDeviceList(listUid, new[] { deviceUid }),
                Is.EqualTo(DeviceListUpdateResult.UpdateOk));

            activeListUid = entMan.SpawnEntity(null, coordinates);
            entMan.AddComponent<DeviceListComponent>(activeListUid);
            configuratorUid = entMan.SpawnEntity(null, coordinates);
            configurator = entMan.AddComponent<NetworkConfiguratorComponent>(configuratorUid);
            configuratorSystem.SetActiveDeviceList(configuratorUid, configurator, activeListUid);

            entMan.DeleteEntity(source);
            entMan.DeleteEntity(deviceUid);
            entMan.DeleteEntity(activeListUid);

            Assert.Multiple(() =>
            {
                Assert.That(thrown.Thrower, Is.Null);
                Assert.That(analyzer.Scanned, Is.Null);
                Assert.That(analyzer.Plant, Is.Null);
                Assert.That(effect.Source, Is.Null);
                Assert.That(list.Devices, Does.Not.Contain(deviceUid));
                Assert.That(configurator.ActiveDeviceList, Is.Null);
            });
        });

        // Exercise two replicated state collections after the delete messages have been emitted. Any stale EntityUid
        // converted to a NetEntity here is surfaced by the integration harness as an unhandled server/client error.
        await Pair.RunTicksSync(2);
        await Pair.ReallyBeIdle(3);

        Assert.Multiple(() =>
        {
            Assert.That(entMan.Deleted(source), Is.True);
            Assert.That(entMan.Deleted(deviceUid), Is.True);
            Assert.That(entMan.Deleted(activeListUid), Is.True);
            Assert.That(entMan.Deleted(thrownUid), Is.False);
            Assert.That(entMan.Deleted(analyzerUid), Is.False);
            Assert.That(entMan.Deleted(effectUid), Is.False);
            Assert.That(entMan.Deleted(listUid), Is.False);
            Assert.That(entMan.Deleted(configuratorUid), Is.False);
        });
    }
}
