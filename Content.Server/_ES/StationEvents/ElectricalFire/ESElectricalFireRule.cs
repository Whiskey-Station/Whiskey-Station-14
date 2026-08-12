using Content.Server._ES.StationEvents.ElectricalFire.Components;
using Content.Server._ES.TileFires;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._ES.StationEvents.ElectricalFire;

public sealed class ESElectricalFireRule : StationEventSystem<ESElectricalFireRuleComponent>
{
    [Dependency] private readonly ESTileFireSystem _tileFire = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    protected override void Added(EntityUid uid,
        ESElectricalFireRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        if (TryFindRandomTile(out _, out _, out var grid, out var coords))
        {
            component.TargetCoordinates = coords;

            if (TryComp<StationEventComponent>(uid, out var stationEvent))
            {
                stationEvent.StartAnnouncement = Loc.GetString(
                    "es-station-event-electrical-fire-start-announcement",
                    ("location", Name(grid)));
            }
        }

        base.Added(uid, component, gameRule, args);
    }

    protected override void Started(EntityUid uid,
        ESElectricalFireRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.TargetCoordinates is not { } coords)
            return;

        if (_transform.GetGrid(coords) is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var worldPos = _transform.ToWorldPosition(coords);

        var tiles = _map.GetTilesIntersecting(grid,
            gridComp,
            new Circle(worldPos, component.FireRadius));

        foreach (var tile in tiles)
        {
            var coord = _map.ToCoordinates(tile, gridComp);

            if (RobustRandom.Prob(component.FireChance))
                _tileFire.TryDoTileFire(coord, stage: RobustRandom.Next(1, 3));
        }
    }
}
