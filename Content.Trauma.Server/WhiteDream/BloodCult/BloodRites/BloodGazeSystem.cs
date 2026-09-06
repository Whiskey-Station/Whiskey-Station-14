// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.Fluids.EntitySystems;
using Content.Server.WhiteDream.BloodCult.Commune;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Stunnable;
using Content.Shared.Wall;
using Content.Shared.WhiteDream.BloodCult.BloodCultist;
using Content.Shared.WhiteDream.BloodCult.Spells;
using Content.Trauma.Shared.Physics.ComplexJoint;
using Content.Trauma.Shared.WhiteDream.BloodCult.BloodRites;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Trauma.Server.WhiteDream.BloodCult.BloodRites;

// Whiskey - handles the balanced blood-cult version of Star Gaze.
public sealed partial class BloodGazeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BloodCultCommuneSystem _commune = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private SharedContinuousBeamSystem _beam = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private TileSystem _tile = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodGazeComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<BloodGazeComponent, BloodGazeChargeDoAfterEvent>(OnChargeFinished);
        SubscribeLocalEvent<BloodGazeComponent, AfterContinuousBeamDamagedEvent>(OnAfterDamage);
        SubscribeLocalEvent<BloodGazeComponent, BeforeContinuousBeamDamageTickEvent>(OnDamageTick);
        SubscribeLocalEvent<BloodGazeComponent, ContinuousBeamStoppedFiringEvent>(OnStopped);
        SubscribeLocalEvent<BloodGazeComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAfterInteract(Entity<BloodGazeComponent> gaze, ref AfterInteractEvent args)
    {
        if (args.Handled || gaze.Comp.Charging || gaze.Comp.Fired)
            return;

        if (!HasComp<BloodCultistComponent>(args.User))
        {
            QueueDel(gaze);
            return;
        }

        var userPosition = _transform.GetMapCoordinates(args.User).Position;
        var targetPosition = _transform.ToMapCoordinates(args.ClickLocation).Position;
        if ((targetPosition - userPosition).LengthSquared() < 0.01f)
            return;

        var ev = new BloodGazeChargeDoAfterEvent
        {
            AimCoordinates = GetNetCoordinates(args.ClickLocation)
        };
        var doAfter = new DoAfterArgs(EntityManager, args.User, gaze.Comp.ChargeTime, ev,
            gaze.Owner, gaze.Owner, gaze.Owner)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            BreakOnDropItem = true,
            NeedHand = true
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        gaze.Comp.Charging = true;
        gaze.Comp.User = args.User;
        gaze.Comp.ChargeEffectEntity = SpawnAttachedTo(gaze.Comp.ChargeEffect,
            new EntityCoordinates(args.User, Vector2.Zero)); // Whiskey - the Star Gaze casting vortex.
        Chant(gaze, args.User, false);
        gaze.Comp.NextChant = _timing.CurTime + gaze.Comp.ChantInterval;
        _audio.PlayPvs(gaze.Comp.ChargeSound, args.User);
        args.Handled = true;
    }

    private void OnChargeFinished(Entity<BloodGazeComponent> gaze, ref BloodGazeChargeDoAfterEvent args)
    {
        gaze.Comp.Charging = false;
        ClearChargeEffect(gaze.Comp);
        if (args.Handled || args.Cancelled || gaze.Comp.Fired ||
            !HasComp<BloodCultistComponent>(args.User) || !_hands.IsHolding(args.User, gaze) ||
            !TryComp(gaze, out ContinuousBeamGunComponent? gun))
            return;

        var coordinates = GetCoordinates(args.AimCoordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        gun.ShootCoordinates = coordinates;
        if (_beam.ShootLaser(args.User, (gaze.Owner, gun)) == null)
            return;

        gaze.Comp.Fired = true;
        gaze.Comp.User = args.User;
        args.Handled = true;
    }

    private void OnAfterDamage(Entity<BloodGazeComponent> gaze, ref AfterContinuousBeamDamagedEvent args)
    {
        // Whiskey - knock victims down in place; unlike Star Gaze, this never pulls or throws them.
        _stun.TryKnockdown(args.Target, gaze.Comp.KnockdownTime, drop: false, force: true);

        // Whiskey - guarantee blood under anything actually struck by the beam.
        TrySpillBlood(gaze, _transform.GetMapCoordinates(args.Target));
    }

    private void OnDamageTick(Entity<BloodGazeComponent> gaze, ref BeforeContinuousBeamDamageTickEvent args)
    {
        if (_timing.CurTime >= gaze.Comp.NextBloodTrail)
        {
            PaintBloodTrail(gaze, args.Ent);
            gaze.Comp.NextBloodTrail = _timing.CurTime + gaze.Comp.BloodTrailInterval;
        }

        if (gaze.Comp.User is not { } user || TerminatingOrDeleted(user) || _timing.CurTime < gaze.Comp.NextChant)
            return;

        Chant(gaze, user, true);
        gaze.Comp.NextChant = _timing.CurTime + gaze.Comp.ChantInterval;
    }

    private void PaintBloodTrail(Entity<BloodGazeComponent> gaze,
        Entity<ContinuousBeamGunComponent, ComplexJointVisualsComponent> beam)
    {
        if (_beam.CalculateBeamDamageData(beam) is not { } data)
            return;

        var (_, _, length, direction, _, origin, _) = data;
        var mapId = Transform(gaze).MapID;

        // Whiskey - one visible puddle per crossed tile; repeated ticks reuse the same puddles.
        for (var distance = 0.5f; distance <= length; distance += 0.5f)
        {
            var position = origin + direction * distance;
            TrySpillBlood(gaze, new MapCoordinates(position, mapId));
        }
    }

    private void TrySpillBlood(Entity<BloodGazeComponent> gaze, MapCoordinates coordinates)
    {
        if (!_map.TryFindGridAt(coordinates, out var gridUid, out var grid))
            return;

        var tile = _map.GetTileRef(gridUid, grid, coordinates);
        CorruptTile(gaze, gridUid, grid, tile);

        var key = (gridUid, tile.GridIndices);
        if (gaze.Comp.BloodiedTiles.Contains(key))
            return;

        var blood = new Solution(gaze.Comp.BloodReagent, gaze.Comp.BloodPerTile);
        if (_puddle.TrySpillAt(tile, blood, out _, sound: false))
            gaze.Comp.BloodiedTiles.Add(key);
    }

    private void CorruptTile(Entity<BloodGazeComponent> gaze,
        EntityUid gridUid,
        MapGridComponent grid,
        TileRef tile)
    {
        // Whiskey - reproduce the 300-blood beam's floor, wall, and airlock conversion.
        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile.GridIndices).ToList())
            TryConvertStructure(gaze.Comp, anchored);

        if (tile.Tile.IsEmpty)
            return;

        var cultTile = (ContentTileDefinition) _tiles[gaze.Comp.CultTile];
        if (tile.Tile.TypeId == cultTile.TileId)
            return;

        _tile.ReplaceTile(tile, cultTile);
        Spawn(gaze.Comp.TileEffect, _map.GridTileToLocal(gridUid, grid, tile.GridIndices));
    }

    private bool TryConvertStructure(BloodGazeComponent gaze, EntityUid target)
    {
        if (IsCultStructure(gaze, target))
            return true;

        EntProtoId? replacement = null;
        if (HasComp<AirlockComponent>(target))
            replacement = gaze.CultDoor;
        else if (HasComp<WallComponent>(target))
            replacement = gaze.CultWall;

        if (replacement is not { } prototype || !gaze.ConvertedStructures.Add(target))
            return replacement != null;

        var xform = Transform(target);
        Spawn(prototype,
            _transform.GetMapCoordinates(target, xform),
            rotation: _transform.GetWorldRotation(xform));
        Spawn(gaze.TileEffect, xform.Coordinates);
        QueueDel(target);
        return true;
    }

    private bool IsCultStructure(BloodGazeComponent gaze, EntityUid target)
    {
        var prototype = MetaData(target).EntityPrototype?.ID;
        return prototype == gaze.CultWall.Id || prototype == gaze.CultDoor.Id;
    }

    private void OnStopped(Entity<BloodGazeComponent> gaze, ref ContinuousBeamStoppedFiringEvent args)
    {
        QueueDel(gaze);
    }

    private void OnShutdown(Entity<BloodGazeComponent> gaze, ref ComponentShutdown args)
    {
        ClearChargeEffect(gaze.Comp);
    }

    private void ClearChargeEffect(BloodGazeComponent gaze)
    {
        if (gaze.ChargeEffectEntity is { } effect && Exists(effect))
            QueueDel(effect);

        gaze.ChargeEffectEntity = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var gazes = EntityQueryEnumerator<BloodGazeComponent>();
        while (gazes.MoveNext(out var uid, out var gaze))
        {
            if (!gaze.Charging || _timing.CurTime < gaze.NextChant ||
                gaze.User is not { } user || TerminatingOrDeleted(user) || !_hands.IsHolding(user, uid))
                continue;

            Chant((uid, gaze), user, false);
            gaze.NextChant = _timing.CurTime + gaze.ChantInterval;
        }
    }

    private void Chant(Entity<BloodGazeComponent> gaze, EntityUid user, bool emphatic)
    {
        var chant = _commune.GenerateChant(gaze.Comp.ChantWords);
        if (emphatic)
            chant = $"{chant.TrimEnd('!', ' ')}!!!";

        _chat.TrySendInGameICMessage(user,
            chant,
            InGameICChatType.Speak,
            ChatTransmitRange.Normal);
    }
}
