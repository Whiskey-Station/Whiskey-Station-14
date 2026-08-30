// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines
// O LEGAL.md deles licencia como AGPL-3.0 tudo que entrou depois do commit
// 87c70a8, de 2024-02-17. O sistema de humor entrou em 2024-08-20.

using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared.Alert;
using Content.Shared.Chat;
// Whiskey: o DamageChangedEvent mudou de lugar neste fork.
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Overlays;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Timer = Robust.Shared.Timing.Timer;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Content.Shared.CCVar;
using Robust.Shared.Utility;

namespace Content.Server._EinsteinEngines.Mood;

// Whiskey: partial porque este engine exige (RA0049), e sem readonly nos
// [Dependency] porque o analisador reprova (RA0051). É a convenção do fork,
// 4578 ocorrências contra 7.
public sealed partial class MoodSystem : EntitySystem
{
    [Dependency] private IChatManager _сhatManager = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedJetpackSystem _jetpack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoodComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<MoodComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MoodComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MoodComponent, MoodEffectEvent>(OnMoodEffect);
        SubscribeLocalEvent<MoodComponent, DamageChangedEvent>(OnDamageChange);
        SubscribeLocalEvent<MoodComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<MoodComponent, MoodRemoveEffectEvent>(OnRemoveEffect);
        SubscribeLocalEvent<MoodComponent, ShowMoodAlertEvent>(OnShowMoodAlert);
    }

    private void OnShowMoodAlert(EntityUid uid, MoodComponent component, ShowMoodAlertEvent args)
    {
        if (!_playerManager.TryGetSessionByEntity(uid, out var session))
            return;

        var msg = $"{Loc.GetString("mood-show-effects-start")}\n";

        foreach (var (_, protoId) in component.CategorisedEffects)
        {
            if (!_prototypeManager.TryIndex<MoodEffectPrototype>(protoId, out var proto)
                || proto.Hidden)
                continue;

            var color = proto.MoodChange > 0 ? "#008000" : "#BA0000";
            msg += $"[font size=10][color={color}]{proto.Description}[/color][/font]\n";
        }

        foreach (var (protoId, _) in component.UncategorisedEffects)
        {
            if (!_prototypeManager.TryIndex<MoodEffectPrototype>(protoId, out var proto)
                || proto.Hidden)
                continue;

            var color = proto.MoodChange > 0 ? "#008000" : "#BA0000";
            msg += $"[font size=10][color={color}]{proto.Description}[/color][/font]\n";
        }

        _сhatManager.ChatMessageToOne(
            ChatChannel.Emotes,
            msg,
            msg,
            EntityUid.Invalid,
            false,
            session.Channel);
    }

    private void OnShutdown(EntityUid uid, MoodComponent component, ComponentShutdown args)
    {
        _alerts.ClearAlertCategory(uid, component.MoodCategory);
        RemComp<SaturationScaleOverlayComponent>(uid);
        RemComp<NetMoodComponent>(uid);
    }

    private void OnRemoveEffect(EntityUid uid, MoodComponent component, MoodRemoveEffectEvent args)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled))
            return;

        if (component.UncategorisedEffects.TryGetValue(args.EffectId, out _))
            RemoveTimedOutEffect(uid, args.EffectId);
        else
        {
            foreach (var (category, id) in component.CategorisedEffects)
                if (id == args.EffectId)
                {
                    RemoveTimedOutEffect(uid, args.EffectId, category);
                    return;
                }
        }
    }

    private void OnRefreshMoveSpeed(EntityUid uid, MoodComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled)
            || component.CurrentMoodThreshold is > MoodThreshold.Meh and < MoodThreshold.Good or MoodThreshold.Dead
            || _jetpack.IsUserFlying(uid))
            return;

        // This ridiculous math serves a purpose making high mood less impactful on movement speed than low mood
        var modifier =
            Math.Clamp(
                (component.CurrentMoodLevel >= component.MoodThresholds[MoodThreshold.Neutral])
                    ? _config.GetCVar(CCVars.MoodIncreasesSpeed)
                        ? MathF.Pow(1.003f, component.CurrentMoodLevel - component.MoodThresholds[MoodThreshold.Neutral])
                        : 1
                    : _config.GetCVar(CCVars.MoodDecreasesSpeed)
                        ? 2 - component.MoodThresholds[MoodThreshold.Neutral] / component.CurrentMoodLevel
                        : 1,
                component.MinimumSpeedModifier,
                component.MaximumSpeedModifier);

        args.ModifySpeed(1, modifier);
    }

    private void OnMoodEffect(EntityUid uid, MoodComponent component, MoodEffectEvent args)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled)
            || !_prototypeManager.TryIndex<MoodEffectPrototype>(args.EffectId, out var prototype) )
            return;

        var ev = new OnMoodEffect(uid, args.EffectId, args.EffectModifier, args.EffectOffset);
        RaiseLocalEvent(uid, ref ev);

        ApplyEffect(uid, component, prototype, ev.EffectModifier, ev.EffectOffset);
    }

    private void ApplyEffect(EntityUid uid, MoodComponent component, MoodEffectPrototype prototype, float eventModifier = 1, float eventOffset = 0)
    {
        // Apply categorised effect
        if (prototype.Category != null)
        {
            if (component.CategorisedEffects.TryGetValue(prototype.Category, out var oldPrototypeId))
            {
                if (!_prototypeManager.TryIndex<MoodEffectPrototype>(oldPrototypeId, out var oldPrototype))
                    return;

                // Don't send the moodlet popup if we already have the moodlet.
                if (!component.CategorisedEffects.ContainsValue(prototype.ID))
                    SendEffectText(uid, prototype);

                if (prototype.ID != oldPrototype.ID)
                    component.CategorisedEffects[prototype.Category] = prototype.ID;
            }
            else
                component.CategorisedEffects.Add(prototype.Category, prototype.ID);

            if (prototype.Timeout != 0)
                Timer.Spawn(TimeSpan.FromSeconds(prototype.Timeout), () => RemoveTimedOutEffect(uid, prototype.ID, prototype.Category));
        }
        // Apply uncategorised effect
        else
        {
            if (component.UncategorisedEffects.TryGetValue(prototype.ID, out _))
                return;

            var moodChange = prototype.MoodChange * eventModifier + eventOffset;
            if (moodChange == 0)
                return;

            // Don't send the moodlet popup if we already have the moodlet.
            if (!component.UncategorisedEffects.ContainsKey(prototype.ID))
                SendEffectText(uid, prototype);

            component.UncategorisedEffects.Add(prototype.ID, moodChange);

            if (prototype.Timeout != 0)
                Timer.Spawn(TimeSpan.FromSeconds(prototype.Timeout), () => RemoveTimedOutEffect(uid, prototype.ID));
        }

        RefreshMood(uid, component);
    }

    private void SendEffectText(EntityUid uid, MoodEffectPrototype prototype)
    {
        if (prototype.Hidden)
            return;

        _popup.PopupEntity(prototype.Description, uid, uid, (prototype.MoodChange > 0) ? PopupType.Medium : PopupType.MediumCaution);
    }

    private void RemoveTimedOutEffect(EntityUid uid, string prototypeId, string? category = null)
    {
        if (!TryComp<MoodComponent>(uid, out var comp))
            return;

        if (category == null)
        {
            if (!comp.UncategorisedEffects.Remove(prototypeId))
                return;
        }
        else
        {
            if (!comp.CategorisedEffects.TryGetValue(category, out var currentProtoId)
                || currentProtoId != prototypeId
                || !_prototypeManager.HasIndex<MoodEffectPrototype>(currentProtoId))
                return;
            comp.CategorisedEffects.Remove(category);
        }

        ReplaceMood(uid, prototypeId);
        RefreshMood(uid, comp);
    }

    /// <summary>
    ///     Some moods specifically create a moodlet upon expiration. This is normally used for "Addiction" type moodlets,
    ///     such as a positive moodlet from an addictive substance that becomes a negative moodlet when a timer ends.
    /// </summary>
    /// <remarks>
    ///     Moodlets that use this should probably also share a category with each other, but this isn't necessarily required.
    ///     Only if you intend that "Re-using the drug" should also remove the negative moodlet.
    /// </remarks>
    private void ReplaceMood(EntityUid uid, string prototypeId)
    {
        if (!_prototypeManager.TryIndex<MoodEffectPrototype>(prototypeId, out var proto)
            || proto.MoodletOnEnd is null)
            return;

        var ev = new MoodEffectEvent(proto.MoodletOnEnd);
        EntityManager.EventBus.RaiseLocalEvent(uid, ev);
    }

    private void OnMobStateChanged(EntityUid uid, MoodComponent component, MobStateChangedEvent args)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled))
            return;

        if (args.NewMobState == MobState.Dead && args.OldMobState != MobState.Dead)
        {
            var ev = new MoodEffectEvent("Dead");
            RaiseLocalEvent(uid, ev);
        }
        else if (args.OldMobState == MobState.Dead && args.NewMobState != MobState.Dead)
        {
            var ev = new MoodRemoveEffectEvent("Dead");
            RaiseLocalEvent(uid, ev);
        }
        RefreshMood(uid, component);
    }

    // <summary>
    //      Recalculate the mood level of an entity by summing up all moodlets.
    // </summary>
    private void RefreshMood(EntityUid uid, MoodComponent component)
    {
        var amount = 0f;

        foreach (var (_, protoId) in component.CategorisedEffects)
        {
            if (!_prototypeManager.TryIndex<MoodEffectPrototype>(protoId, out var prototype))
                continue;

            amount += prototype.MoodChange;
        }

        foreach (var (_, value) in component.UncategorisedEffects)
            amount += value;

        SetMood(uid, amount, component, refresh: true);
    }

    private void OnInit(EntityUid uid, MoodComponent component, ComponentStartup args)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled))
            return;

        if (_config.GetCVar(CCVars.MoodModifiesThresholds)
            && TryComp<MobThresholdsComponent>(uid, out var mobThresholdsComponent)
            && _mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var critThreshold, mobThresholdsComponent))
            component.CritThresholdBeforeModify = critThreshold.Value;

        EnsureComp<NetMoodComponent>(uid);
        RefreshMood(uid, component);
    }

    private void SetMood(EntityUid uid, float amount, MoodComponent? component = null, bool force = false, bool refresh = false)
    {
        if (!_config.GetCVar(CCVars.MoodEnabled)
            || !Resolve(uid, ref component)
            || component.CurrentMoodThreshold == MoodThreshold.Dead && !refresh)
            return;

        var neutral = component.MoodThresholds[MoodThreshold.Neutral];
        var ev = new OnSetMoodEvent(uid, amount, false);
        RaiseLocalEvent(uid, ref ev);

        if (ev.Cancelled)
            return;

        uid = ev.Receiver;
        amount = ev.MoodChangedAmount;

        var newMoodLevel = amount + neutral + ev.MoodOffset;
        if (!force)
        {
            newMoodLevel = Math.Clamp(
                newMoodLevel,
                component.MoodThresholds[MoodThreshold.Dead],
                component.MoodThresholds[MoodThreshold.Perfect]);
        }

        component.CurrentMoodLevel = newMoodLevel;

        if (TryComp<NetMoodComponent>(uid, out var mood))
        {
            mood.CurrentMoodLevel = component.CurrentMoodLevel;
            mood.NeutralMoodThreshold = component.MoodThresholds.GetValueOrDefault(MoodThreshold.Neutral);
            Dirty(uid, mood);
        }

        RefreshShaders(uid, component.CurrentMoodLevel);
        UpdateCurrentThreshold(uid, component);
    }

    private void UpdateCurrentThreshold(EntityUid uid, MoodComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var calculatedThreshold = GetMoodThreshold(component);
        if (calculatedThreshold == component.CurrentMoodThreshold)
            return;

        component.CurrentMoodThreshold = calculatedThreshold;

        DoMoodThresholdsEffects(uid, component);
    }

    private void DoMoodThresholdsEffects(EntityUid uid, MoodComponent? component = null, bool force = false)
    {
        if (!Resolve(uid, ref component)
            || component.CurrentMoodThreshold == component.LastThreshold && !force)
            return;

        var modifier = GetMovementThreshold(component.CurrentMoodThreshold);

        // Modify mob stats
        if (modifier != GetMovementThreshold(component.LastThreshold))
        {
            _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
            SetCritThreshold(uid, component, modifier);
        }

        // Modify interface
        if (component.MoodThresholdsAlerts.TryGetValue(component.CurrentMoodThreshold, out var alertId))
            _alerts.ShowAlert(uid, alertId);
        else
            _alerts.ClearAlertCategory(uid, component.MoodCategory);

        component.LastThreshold = component.CurrentMoodThreshold;
    }

    private void RefreshShaders(EntityUid uid, float mood)
    {
        EnsureComp<SaturationScaleOverlayComponent>(uid, out var comp);
        comp.SaturationScale = mood / 50;
        Dirty(uid, comp);
    }

    private void SetCritThreshold(EntityUid uid, MoodComponent component, int modifier)
    {
        if (!_config.GetCVar(CCVars.MoodModifiesThresholds)
            || !TryComp<MobThresholdsComponent>(uid, out var mobThresholds)
            || !_mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var key))
            return;

        var newKey = modifier switch
        {
            1 => FixedPoint2.New(key.Value.Float() * component.IncreaseCritThreshold),
            -1 => FixedPoint2.New(key.Value.Float() * component.DecreaseCritThreshold),
            _ => component.CritThresholdBeforeModify,
        };

        component.CritThresholdBeforeModify = key.Value;
        _mobThreshold.SetMobStateThreshold(uid, newKey, MobState.Critical, mobThresholds);
    }

    private MoodThreshold GetMoodThreshold(MoodComponent component, float? moodLevel = null)
    {
        moodLevel ??= component.CurrentMoodLevel;
        var result = MoodThreshold.Dead;
        var value = component.MoodThresholds[MoodThreshold.Perfect];

        foreach (var threshold in component.MoodThresholds)
            if (threshold.Value <= value && threshold.Value >= moodLevel)
            {
                result = threshold.Key;
                value = threshold.Value;
            }

        return result;
    }

    private int GetMovementThreshold(MoodThreshold threshold) =>
        threshold switch
        {
            >= MoodThreshold.Good => 1,
            <= MoodThreshold.Meh => -1,
            _ => 0,
        };

    private void OnDamageChange(EntityUid uid, MoodComponent component, DamageChangedEvent args)
    {
        // Whiskey: o TotalDamage é [Access] do DamageableSystem aqui, então vai
        // pela API dele.
        var dano = _damageable.GetTotalDamage((uid, args.Damageable));

        // Whiskey: a referência é o SoftCrit, e não o Critical.
        //
        // O porte veio de um jogo com um estágio de queda só, onde a pessoa
        // desmaia ao chegar no Critical. Lá "0,8 do limiar" queria dizer "80%
        // do caminho até cair no chão", que é uma escala com sentido.
        //
        // O Trauma partiu isso em dois. No humanoide a escada herdada de
        // species_base.yml é 100 SoftCrit, 150 Critical e 200 morte, e quem cai
        // no chão cai no SoftCrit. Medindo contra o Critical, cair valia
        // 100/150, ou seja 0,67, e o modificador pesado só entrava com 120 de
        // dano, que é vinte DEPOIS de a pessoa já estar caída. Na prática o
        // humor parava em Ruim por mais machucada que ela estivesse, e as duas
        // faixas de baixo eram inalcançáveis por dano.
        //
        // A reserva não é decoração: só o species_base.yml declara SoftCrit, e
        // 55 prototypes declaram Critical. Bicho vai direto de vivo para
        // crítico. Sem o segundo tento, o humor morreria calado em tudo que não
        // é humanoide.
        if (!_mobThreshold.TryGetPercentageForState(uid, MobState.SoftCrit, dano, out var damage)
            && !_mobThreshold.TryGetPercentageForState(uid, MobState.Critical, dano, out damage))
            return;

        var protoId = "HealthNoDamage";
        var value = component.HealthMoodEffectsThresholds["HealthNoDamage"];

        foreach (var threshold in component.HealthMoodEffectsThresholds)
            if (threshold.Value <= damage && threshold.Value >= value)
            {
                protoId = threshold.Key;
                value = threshold.Value;
            }

        var ev = new MoodEffectEvent(protoId);
        RaiseLocalEvent(uid, ev);
    }
}
