// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Content.Shared.WhiteDream.BloodCult.BloodCultist;
using Content.Trauma.Shared.Physics.ComplexJoint;
using Content.Trauma.Shared.WhiteDream.BloodCult.BloodRites;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey;

// Whiskey - regression coverage for the Blood Bolt Barrage hand and range fixes.
public sealed class BloodBoltBarrageTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task BarrageKeepsHandAndHitsAtRange()
    {
        await AddAtmosphere();

        TargetCoords = SEntMan.GetNetCoordinates(
            new EntityCoordinates(MapData.MapUid, new Vector2(6.5f, 0.5f)));
        var target = await SpawnTarget("MobHuman", TargetCoords);
        var barrage = await PlaceInHands("BloodBoltBarrage");

        await Pair.RunSeconds(2f); // Guns start with a pickup cooldown.

        var activeHand = HandSys.GetActiveHand((SPlayer, Hands));
        Assert.That(activeHand, Is.Not.Null);

        await AttemptShoot(target);
        await Pair.RunSeconds(0.5f);

        Assert.That(HandSys.GetActiveHand((SPlayer, Hands)), Is.EqualTo(activeHand));
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.EqualTo(ToServer(barrage)));

        var damageable = SEntMan.System<DamageableSystem>();
        var slash = damageable.GetAllDamage(ToServer(target)).DamageDict["Slash"];
        Assert.That(slash, Is.GreaterThan(FixedPoint2.Zero));
    }

    [Test]
    public async Task BloodGazeDamagesAndLeavesBlood()
    {
        await AddAtmosphere();
        TargetCoords = SEntMan.GetNetCoordinates(
            new EntityCoordinates(MapData.MapUid, new Vector2(6.5f, 0.5f))); // Whiskey - cover real combat range.
        await SetTile(Plating, TargetCoords, MapData.Grid);

        await Server.WaitPost(() => SEntMan.EnsureComponent<BloodCultistComponent>(SPlayer));
        var target = await SpawnTarget("MobHuman");
        await Server.WaitPost(() =>
            SEntMan.EnsureComponent<BloodCultistComponent>(ToServer(target))); // Whiskey - allies are valid targets.
        var gaze = await PlaceInHands("BloodGazeAura");

        await Interact(target, TargetCoords);
        await Pair.RunSeconds(0.5f);

        var serverGaze = ToServer(gaze);
        var gazeComponent = SEntMan.GetComponent<BloodGazeComponent>(serverGaze);
        var gun = SEntMan.GetComponent<ContinuousBeamGunComponent>(serverGaze);
        Assert.That(gazeComponent.Fired, Is.True, "The blood gaze should fire after its invocation.");
        Assert.That(SEntMan.EntityExists(gun.Endpoint), Is.True, "The continuous beam endpoint should exist.");

        var damageable = SEntMan.System<DamageableSystem>();
        damageable.GetAllDamage(ToServer(target)).DamageDict.TryGetValue("Blunt", out var blunt);
        Assert.That(blunt, Is.GreaterThan(FixedPoint2.Zero));
        Assert.That(SEntMan.HasComponent<KnockedDownComponent>(ToServer(target)), Is.True,
            "The blood gaze should knock victims down without carrying them.");

        await AssertTile("CultFloor", TargetCoords);

        var puddles = SEntMan.EntityQueryEnumerator<PuddleComponent>();
        Assert.That(puddles.MoveNext(out _), "The blood gaze should leave blood on crossed floor tiles.");

        await Pair.RunSeconds(10f);
        var mobState = SEntMan.GetComponent<MobStateComponent>(ToServer(target));
        Assert.That(mobState.CurrentState, Is.Not.EqualTo(MobState.Alive),
            "A target exposed to the complete blood gaze should no longer be standing alive.");
    }
}
