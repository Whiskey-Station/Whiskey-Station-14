using System.Numerics;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests.Explosion;

[TestFixture]
[TestOf(typeof(ExplosionSystem))]
public sealed class ExplosionSchedulingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task ZeroResistanceAirtightAcrossGridsWithSimultaneousExplosions()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var explosion = entMan.System<ExplosionSystem>();
        var cfg = server.CfgMan;
        var oldTilesPerTick = cfg.GetCVar(CCVars.ExplosionTilesPerTick);
        var oldMaxTime = cfg.GetCVar(CCVars.ExplosionMaxProcessingTime);
        var completedBefore = explosion.CompletedExplosions;
        var walls = new List<EntityUid>();

        try
        {
            await server.WaitAssertion(() =>
            {
                cfg.SetCVar(CCVars.ExplosionTilesPerTick, 16);
                cfg.SetCVar(CCVars.ExplosionMaxProcessingTime, 4f);

                var map = mapSystem.CreateMap(out var mapId);
                for (var gridIndex = 0; gridIndex < 3; gridIndex++)
                {
                    var grid = mapSystem.CreateGridEntity(mapId);
                    transform.SetLocalPosition(grid.Owner, new Vector2(gridIndex * 32, 0));

                    List<(Vector2i Index, Tile Tile)> tiles = new();
                    for (var x = -4; x <= 4; x++)
                    for (var y = -4; y <= 4; y++)
                        tiles.Add((new Vector2i(x, y), new Tile(1)));
                    mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

                    var wall = entMan.SpawnAttachedTo("WallSolid", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
                    var destructible = entMan.GetComponent<DestructibleComponent>(wall);
                    ((DamageTrigger) destructible.Thresholds[0].Trigger).Damage = FixedPoint2.Zero;
                    explosion.UpdateAirtightMap(grid.Owner, grid.Comp, Vector2i.Zero);
                    walls.Add(wall);

                    if (gridIndex < 2)
                    {
                        var epicenter = transform.ToMapCoordinates(new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
                        explosion.QueueExplosion(
                            epicenter,
                            "Default",
                            2_000f,
                            5f,
                            50f,
                            null,
                            tileBreakScale: 0f,
                            maxTileBreak: 0,
                            canCreateVacuum: false,
                            addLog: false);
                    }
                }
            });

            await RunUntilComplete(server, explosion, completedBefore + 2, 500);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(explosion.CompletedExplosions, Is.EqualTo(completedBefore + 2));
                    Assert.That(explosion.HasPendingExplosionWork, Is.False);
                    Assert.That(explosion.LastTickWork, Is.LessThanOrEqualTo(16));
                    Assert.That(walls.Take(2).All(entMan.Deleted), Is.True);
                });
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                cfg.SetCVar(CCVars.ExplosionTilesPerTick, oldTilesPerTick);
                cfg.SetCVar(CCVars.ExplosionMaxProcessingTime, oldMaxTime);
            });
        }
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task ThirtyThousandTileExplosionIsIncremental()
    {
        const int targetArea = 30_000;
        var server = Pair.Server;
        var entMan = server.EntMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var explosion = entMan.System<ExplosionSystem>();
        var cfg = server.CfgMan;
        var oldTilesPerTick = cfg.GetCVar(CCVars.ExplosionTilesPerTick);
        var oldMaxTime = cfg.GetCVar(CCVars.ExplosionMaxProcessingTime);
        var oldMaxArea = cfg.GetCVar(CCVars.ExplosionMaxArea);
        var completedBefore = explosion.CompletedExplosions;

        try
        {
            await server.WaitAssertion(() =>
            {
                cfg.SetCVar(CCVars.ExplosionTilesPerTick, 512);
                cfg.SetCVar(CCVars.ExplosionMaxProcessingTime, 4f);
                cfg.SetCVar(CCVars.ExplosionMaxArea, targetArea);

                var map = mapSystem.CreateMap(out var mapId);
                var grid = mapSystem.CreateGridEntity(mapId);
                List<(Vector2i Index, Tile Tile)> tiles = new(targetArea);
                for (var x = -100; x < 100; x++)
                for (var y = -75; y < 75; y++)
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

                var epicenter = transform.ToMapCoordinates(new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
                explosion.QueueExplosion(
                    epicenter,
                    "Default",
                    10_000_000f,
                    1f,
                    100f,
                    null,
                    tileBreakScale: 0f,
                    maxTileBreak: 0,
                    canCreateVacuum: false,
                    addLog: false);
            });

            await server.WaitRunTicks(1);
            Assert.That(explosion.HasPendingExplosionWork, Is.True,
                "A 30k-tile flood must yield instead of completing synchronously in its first tick.");

            await RunUntilComplete(server, explosion, completedBefore + 1, 2_000);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(explosion.CompletedExplosions, Is.EqualTo(completedBefore + 1));
                    Assert.That(explosion.HasPendingExplosionWork, Is.False);
                    Assert.That(explosion.LastGeneratedArea, Is.InRange(targetArea, targetArea + 2_000));
                    Assert.That(explosion.LastTickWork, Is.LessThanOrEqualTo(512));
                });
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                cfg.SetCVar(CCVars.ExplosionTilesPerTick, oldTilesPerTick);
                cfg.SetCVar(CCVars.ExplosionMaxProcessingTime, oldMaxTime);
                cfg.SetCVar(CCVars.ExplosionMaxArea, oldMaxArea);
            });
        }
    }

    private static async Task RunUntilComplete(
        Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server,
        ExplosionSystem explosion,
        int expectedCompleted,
        int maxTicks)
    {
        for (var tick = 0; tick < maxTicks && explosion.CompletedExplosions < expectedCompleted; tick++)
            await server.WaitRunTicks(1);

        Assert.That(explosion.CompletedExplosions, Is.GreaterThanOrEqualTo(expectedCompleted));
    }
}
