// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Chat;
using Content.Shared.Weapons.Melee.Events;
using Content.Trauma.Common.CCVar;
using Content.Trauma.Common.Movement;
using Content.Trauma.Shared.Viewcone.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Trauma.Shared.Viewcone;

/// <summary>
/// Handles footsteps creating out-of-vision effects.
/// Provides API for spawning viewcone effects and making sure source
/// gets set correctly + it spawns in the correct pos and shit
/// </summary>
public sealed partial class ViewconeEffectSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private INetManager _net = default!;

    public static readonly EntProtoId TalkEffect = "ViewconeEffectTalk";

    private bool _disabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ViewconeFootstepsEffectComponent, FootStepEvent>(OnFootStep);
        SubscribeLocalEvent<ViewconeMeleeEffectComponent, MeleeAttackEvent>(OnMeleeAttack);
        if (_net.IsServer)
        {
            SubscribeLocalEvent<ViewconeOccludableComponent, EntityTerminatingEvent>(OnEffectTerminating);
            SubscribeLocalEvent<ViewconeOccludableComponent, ComponentShutdown>(OnEffectShutdown);
            SubscribeLocalEvent<ViewconeSourceReferenceComponent, EntityTerminatingEvent>(OnSourceTerminating);
            SubscribeLocalEvent<ViewconeSourceReferenceComponent, ComponentShutdown>(OnSourceShutdown);
        }
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        // TODO: CFG boing

        Subs.CVar(_cfg, TraumaCVars.DisableVisionEffects, x => _disabled = x, true);
    }

    private void OnFootStep(Entity<ViewconeFootstepsEffectComponent> ent, ref FootStepEvent args)
    {
        // Silent shoes suppress the viewcone footstep effect
        var ev = new CanSpawnFootstepsEvent();
        RaiseLocalEvent(ent.Owner, ref ev);
        if (ev.Cancelled)
            return;

        SpawnEffect(ent, ent.Comp.Effect, args.WorldAngle);
    }

    private void OnMeleeAttack(Entity<ViewconeMeleeEffectComponent> ent, ref MeleeAttackEvent args)
    {
        SpawnEffect(ent, ent.Comp.Effect);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        // whispering is too quiet to get a fix on
        if (!args.IsWhisper)
            SpawnEffect(args.Source, TalkEffect);
    }

    /// <summary>
    /// Spawns the given effect entity at the player source, and sets relevant variables
    /// </summary>
    /// <param name="source">The player that originated the effect, or the entity to spawn next to if a relevant player doesn't exist</param>
    /// <param name="effect">The prototype ID of an effect entity to spawn (see viewcone_effects.yml)</param>
    /// <param name="angleOverride">The local rotation to set the effect to, instead of the parent rotation.</param>
    public void SpawnEffect(EntityUid source, [ForbidLiteral] EntProtoId effect, Angle? angleOverride = null)
    {
        if (_disabled || !_timing.IsFirstTimePredicted)
            return;

        var ent = PredictedSpawnNextToOrDrop(effect, source);
        var viewconeEffect = EnsureComp<ViewconeOccludableComponent>(ent);
        viewconeEffect.Inverted = true; // it's always visible
        SetSource((ent, viewconeEffect), source);

        // set rotation
        _xform.SetLocalRotation(ent, angleOverride ?? Transform(source).LocalRotation);

        // also ensure this in case somehow something without it gets here.
        EnsureComp<TimedDespawnComponent>(ent);
    }

    public void SetSource(Entity<ViewconeOccludableComponent> ent, EntityUid? source)
    {
        if (ent.Comp.Source == source)
            return;

        UntrackSource(ent.Owner, ent.Comp.Source);
        ent.Comp.Source = TerminatingOrDeleted(source) ? null : source;
        if (_net.IsServer && ent.Comp.Source is { } sourceUid)
            EnsureComp<ViewconeSourceReferenceComponent>(sourceUid).Effects.Add(ent.Owner);
        Dirty(ent);
    }

    private void UntrackSource(EntityUid effect, EntityUid? source)
    {
        if (_net.IsClient || source is not { } sourceUid ||
            !TryComp(sourceUid, out ViewconeSourceReferenceComponent? references))
            return;

        references.Effects.Remove(effect);
        if (references.Effects.Count == 0)
            RemCompDeferred(sourceUid, references);
    }

    private void OnEffectTerminating(Entity<ViewconeOccludableComponent> ent, ref EntityTerminatingEvent args)
    {
        UntrackSource(ent.Owner, ent.Comp.Source);
    }

    private void OnEffectShutdown(Entity<ViewconeOccludableComponent> ent, ref ComponentShutdown args)
    {
        UntrackSource(ent.Owner, ent.Comp.Source);
    }

    private void OnSourceTerminating(Entity<ViewconeSourceReferenceComponent> ent, ref EntityTerminatingEvent args)
    {
        CleanupSource(ent);
    }

    private void OnSourceShutdown(Entity<ViewconeSourceReferenceComponent> ent, ref ComponentShutdown args)
    {
        CleanupSource(ent);
    }

    private void CleanupSource(Entity<ViewconeSourceReferenceComponent> ent)
    {
        foreach (var effectUid in ent.Comp.Effects.ToArray())
        {
            if (!TryComp(effectUid, out ViewconeOccludableComponent? effect) || effect == null || TerminatingOrDeleted(effectUid) ||
                effect.Source != ent.Owner)
                continue;

            effect.Source = null;
            Dirty(effectUid, effect);
        }

        ent.Comp.Effects.Clear();
    }
}
