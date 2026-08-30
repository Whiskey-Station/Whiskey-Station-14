// SPDX-FileCopyrightText: 2026 Whiskey Station contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Tests.Atmos;
using Content.Server._ES.TileFires;
using Content.Server.Spreader;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._ES.TileFires;
using Content.Shared.FixedPoint;
using Content.Trauma.Common.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._ES.TileFires;

[TestFixture]
[TestOf(typeof(ESTileFireSystem))]
public sealed class ESTileFireTest : AtmosTest
{
    protected override ResPath? TestMapPath => new("Maps/Test/Atmospherics/load_atmos_test_room.yml");

    [Test]
    public async Task EventStageFireIgnitesAndSpreadsAtThreshold()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = MapData.Grid.Owner;

        EntityUid source = default;
        await server.WaitPost(() =>
        {
            var fire = server.System<ESTileFireSystem>();
            // Use the center of a sealed, oxygenated 5x5 room. The previous
            // 3x3 fixture had to remove its perimeter walls, allowing the new
            // atmosphere solver to vent the room before the spreader tick.
            var coordinates = new EntityCoordinates(grid, 2.5f, 2.5f);
            Assert.That(fire.TryDoTileFire(coordinates, stage: 2), Is.True);

            var query = entMan.EntityQueryEnumerator<ESTileFireComponent, FlammableComponent>();
            var found = false;
            while (query.MoveNext(out var candidate, out var tileFire, out var flammable))
            {
                if (entMan.GetComponent<TransformComponent>(candidate).GridUid != grid)
                    continue;

                source = candidate;
                found = true;
                Assert.Multiple(() =>
                {
                    Assert.That(flammable.FireStacks, Is.EqualTo(7f));
                    Assert.That(entMan.HasComponent<OnFireComponent>(source), Is.True);
                });

                // Drive the component to its production spread threshold, then raise
                // the same edge-spreader event used by the simulation update loop.
                tileFire.BaseSpreadChance = 1f;
                flammable.FireStacks = tileFire.MinFirestacksToSpread;
                break;
            }

            Assert.That(found, Is.True, "The event fire must be found on the fixture grid.");

            var spreader = server.System<SpreaderSystem>();
            var transform = entMan.GetComponent<TransformComponent>(source);
            var edge = entMan.GetComponent<EdgeSpreaderComponent>(source);
            spreader.GetNeighbors(source, transform, edge.Id, out var freeTiles, out _, out var neighbors);
            Assert.That(freeTiles, Is.Not.Empty, "The fixture must expose a sustainable neighboring tile.");

            var spread = new SpreadNeighborsEvent
            {
                NeighborFreeTiles = freeTiles,
                Neighbors = neighbors,
                Updates = 1,
            };
            entMan.EventBus.RaiseLocalEvent(source, ref spread);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(source), Is.False);
            Assert.That(entMan.HasComponent<OnFireComponent>(source), Is.True);

            var count = 0;
            var query = entMan.EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var transform))
            {
                if (transform.ParentUid == grid)
                    count++;
            }

            Assert.That(count, Is.GreaterThan(1));
        });
    }

    [Test]
    public async Task OneUnitOfWaterExtinguishesStageOneFire()
    {
        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var grid = MapData.Grid.Owner;
        EntityUid fireUid = default;
        await server.WaitPost(() =>
        {
            var fire = server.System<ESTileFireSystem>();
            Assert.That(fire.TryDoTileFire(new EntityCoordinates(grid, 2.5f, 2.5f)), Is.True);

            var query = entMan.EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
            var found = false;
            while (query.MoveNext(out var candidate, out _, out var transform))
            {
                if (transform.GridUid != grid)
                    continue;

                fireUid = candidate;
                found = true;
                break;
            }

            Assert.That(found, Is.True, "The stage-one fire must be found on the fixture grid.");
            Assert.That(entMan.HasComponent<OnFireComponent>(fireUid), Is.True);

            var reactive = entMan.System<ReactiveSystem>();
            reactive.ReactionEntity(
                fireUid,
                ReactionMethod.Touch,
                new ReagentQuantity("Water", FixedPoint2.New(1)));
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(fireUid), Is.True,
                "A direct extinguisher spray should remove a stage-one tile fire.");
        });
    }
}
