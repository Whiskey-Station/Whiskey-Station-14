using System.Linq;
using Content.Shared.Gravity;
using Content.Shared.Inventory; // Goobstation
using Content.Shared.StepTrigger.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared.StepTrigger.Systems;

public sealed partial class StepTriggerSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _physicsquery = default!;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<StepTriggerComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<StepTriggerComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<StepTriggerComponent, EntityTerminatingEvent>(OnTriggerTerminating);
        SubscribeLocalEvent<StepTriggerCleanupComponent, EntityTerminatingEvent>(OnTerminating); // Goobstation - Fix
#if DEBUG
        SubscribeLocalEvent<StepTriggerComponent, ComponentStartup>(OnStartup);
    }
    private void OnStartup(EntityUid uid, StepTriggerComponent component, ComponentStartup args)
    {
        if (!component.Active)
            return;

        if (!TryComp(uid, out FixturesComponent? fixtures) || fixtures.FixtureCount == 0)
            Log.Warning($"{ToPrettyString(uid)} has an active step trigger without any fixtures.");
#endif
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var enumerator = EntityQueryEnumerator<StepTriggerActiveComponent, StepTriggerComponent, TransformComponent>();

        while (enumerator.MoveNext(out var uid, out var active, out var trigger, out var transform))
        {
            if (!Update(uid, trigger, transform))
            {
                continue;
            }

            RemCompDeferred(uid, active);
        }
    }

    private bool Update(EntityUid uid, StepTriggerComponent component, TransformComponent transform)
    {
        if (!component.Active ||
            component.Colliding.Count == 0)
        {
            return true;
        }

        if (component.Blacklist != null && TryComp<MapGridComponent>(transform.GridUid, out var grid))
        {
            var positon = _map.LocalToTile(transform.GridUid.Value, grid, transform.Coordinates);
            var anch = _map.GetAnchoredEntities(uid, grid, positon);

            while (anch.MoveNext(out var ent))
            {
                if (ent == uid)
                    continue;

                if (_whitelistSystem.IsWhitelistPass(component.Blacklist, ent.Value))
                {
                    return false;
                }
            }
        }

        foreach (var otherUid in component.Colliding)
        {
            UpdateColliding(uid, component, transform, otherUid);
        }

        return false;
    }

    private void UpdateColliding(EntityUid uid, StepTriggerComponent component, TransformComponent ownerXform, EntityUid otherUid)
    {
        if (!_physicsquery.TryComp(otherUid, out var otherPhysics))
            return;

        var otherXform = Transform(otherUid);
        // TODO: This shouldn't be calculating based on world AABBs.
        var ourAabb = _entityLookup.GetAABBNoContainer(uid, ownerXform.LocalPosition, ownerXform.LocalRotation);
        var otherAabb = _entityLookup.GetAABBNoContainer(otherUid, otherXform.LocalPosition, otherXform.LocalRotation);

        if (!ourAabb.Intersects(otherAabb))
        {
            component.CurrentlySteppedOn.Remove(otherUid);
            return;
        }

        // max 'area of enclosure' between the two aabbs
        // this is hard to explain
        var intersect = Box2.Area(otherAabb.Intersect(ourAabb));
        var ratio = Math.Max(intersect / Box2.Area(otherAabb), intersect / Box2.Area(ourAabb));
        if (otherPhysics.LinearVelocity.Length() < component.RequiredTriggeredSpeed
            || component.CurrentlySteppedOn.Contains(otherUid)
            || ratio < component.IntersectRatio
            || !CanTrigger(uid, otherUid, component))
        {
            return;
        }

        if (component.StepOn)
        {
            var evStep = new StepTriggeredOnEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStep);
        }
        else
        {
            var evStep = new StepTriggeredOffEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStep);
        }

        component.CurrentlySteppedOn.Add(otherUid);
    }

    private bool CanTrigger(EntityUid uid, EntityUid otherUid, StepTriggerComponent component)
    {
        if (!component.Active || component.CurrentlySteppedOn.Contains(otherUid))
            return false;

        // Goobstation Change Start: Immunity checks
        if (TryComp<StepTriggerImmuneComponent>(otherUid, out var stepTriggerImmuneComponent)
            && component.TriggerGroups != null
            && component.TriggerGroups.IsValid(stepTriggerImmuneComponent))
            return false;
        // Goobstation Change End

        // Can't trigger if we don't ignore weightless entities
        // and the entity is flying or currently weightless
        // Makes sense simulation wise to have this be part of steptrigger directly IMO
        if (!component.IgnoreWeightless && TryComp<PhysicsComponent>(otherUid, out var physics) &&
            (physics.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(otherUid)))
            return false;

        var msg = new StepTriggerAttemptEvent { Source = uid, Tripper = otherUid };

        RaiseLocalEvent(uid, ref msg);
        RaiseLocalEvent(otherUid, ref msg); // Goobstation - let enchants handle it too

        return msg.Continue && !msg.Cancelled;
    }

    private void OnStartCollide(EntityUid uid, StepTriggerComponent component, ref StartCollideEvent args)
    {
        if (_net.IsClient)
            return;

        var otherUid = args.OtherEntity;

        if (!args.OtherFixture.Hard)
            return;

        if (!CanTrigger(uid, otherUid, component))
            return;

        EnsureComp<StepTriggerActiveComponent>(uid);

        if (component.Colliding.Add(otherUid))
        {
            var cleanup = EnsureComp<StepTriggerCleanupComponent>(otherUid); // Goobstation - Fix
            cleanup.StepTriggers.Add(uid);
        }
    }

    private void OnEndCollide(EntityUid uid, StepTriggerComponent component, ref EndCollideEvent args)
    {
        if (_net.IsClient)
            return;

        var otherUid = args.OtherEntity;

        if (!component.Colliding.Remove(otherUid))
            return;

        component.CurrentlySteppedOn.Remove(otherUid);
        if (TryComp(otherUid, out StepTriggerCleanupComponent? cleanup)) // Goobstation - Fix
        {
            cleanup.StepTriggers.Remove(uid);
            if (cleanup.StepTriggers.Count == 0)
                RemCompDeferred(otherUid, cleanup);
        }

        if (component.StepOn)
        {
            var evStepOff = new StepTriggeredOffEvent(uid, otherUid);
            RaiseLocalEvent(uid, ref evStepOff);
        }

        if (component.Colliding.Count == 0)
        {
            RemCompDeferred<StepTriggerActiveComponent>(uid);
        }
    }

    public void SetIntersectRatio(EntityUid uid, float ratio, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (MathHelper.CloseToPercent(component.IntersectRatio, ratio))
            return;

        component.IntersectRatio = ratio;
        Dirty(uid, component);
    }

    public void SetRequiredTriggerSpeed(EntityUid uid, float speed, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (MathHelper.CloseToPercent(component.RequiredTriggeredSpeed, speed))
            return;

        component.RequiredTriggeredSpeed = speed;
        Dirty(uid, component);
    }

    public void SetActive(EntityUid uid, bool active, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (active == component.Active)
            return;

        component.Active = active;
        Dirty(uid, component);
    }

    // Goobstation
    public void SetIgnoreWeightless(EntityUid uid, bool ignore, StepTriggerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (ignore == component.IgnoreWeightless)
            return;

        component.IgnoreWeightless = ignore;
        Dirty(uid, component);
    }

    private void OnTerminating(EntityUid uid, StepTriggerCleanupComponent component, ref EntityTerminatingEvent args) // Goobstation - Fix
    {
        foreach (var triggerUid in component.StepTriggers.ToArray())
        {
            if (!TryComp<StepTriggerComponent>(triggerUid, out var step))
                continue;

            step.Colliding.Remove(uid);
            step.CurrentlySteppedOn.Remove(uid);
        }

        component.StepTriggers.Clear();
    }

    private void OnTriggerTerminating(EntityUid uid, StepTriggerComponent component, ref EntityTerminatingEvent args)
    {
        foreach (var otherUid in component.Colliding.ToArray())
        {
            if (!TryComp(otherUid, out StepTriggerCleanupComponent? cleanup) || cleanup == null)
                continue;

            cleanup.StepTriggers.Remove(uid);
            if (cleanup.StepTriggers.Count == 0)
                RemCompDeferred(otherUid, cleanup);
        }

        component.Colliding.Clear();
        component.CurrentlySteppedOn.Clear();
    }

}

[ByRefEvent]
public struct StepTriggerAttemptEvent : IInventoryRelayEvent // Goobstation
{
    SlotFlags IInventoryRelayEvent.TargetSlots => SlotFlags.FEET | SlotFlags.OUTERCLOTHING; // Goobstation
    public EntityUid Source;
    public EntityUid Tripper;
    public bool Continue;
    /// <summary>
    ///     Set by systems which wish to cancel the step trigger event, regardless of event ordering.
    /// </summary>
    public bool Cancelled;
}

/// <summary>
/// Raised when an entity stands on a steptrigger initially (assuming it has both on and off states).
/// </summary>
[ByRefEvent]
public readonly record struct StepTriggeredOnEvent(EntityUid Source, EntityUid Tripper);

/// <summary>
/// Raised when an entity leaves a steptrigger if it has on and off states OR when an entity intersects a steptrigger.
/// </summary>
[ByRefEvent]
public readonly record struct StepTriggeredOffEvent(EntityUid Source, EntityUid Tripper);
