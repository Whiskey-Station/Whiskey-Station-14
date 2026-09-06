// SPDX-License-Identifier: AGPL-3.0-or-later
// Blood Cult: ported from WWhiteDreamProject/wwdpublic. See Content.Shared/WhiteDream/BloodCult/ATTRIBUTION.md

using Content.Server.Actions;
using Content.Server.Cuffs;
using Content.Server.DoAfter;
using Content.Server.Emp;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.WhiteDream.BloodCult.Gamerule; // Whiskey
using Content.Shared.Actions.Components;
using Content.Shared.Stunnable;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Clothing.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Mindshield;
using Content.Shared.Popups;
using Content.Goobstation.Shared.ListViewSelector;
using Content.Trauma.Common.RadialSelector;
using Content.Shared.Speech.Muting;
using Content.Shared.Tag;
using Content.Shared.WhiteDream.BloodCult.Spells;
using Content.Shared.WhiteDream.BloodCult;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio; // WhiteDream - AudioParams
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using StatusEffectsNewSystem = Content.Shared.StatusEffectNew.StatusEffectsSystem; // Trauma

using Content.Shared.Damage.Systems;

namespace Content.Server.WhiteDream.BloodCult.Spells;

public sealed partial class BloodCultSpellsSystem : EntitySystem
{
    // Trauma - muting moved to the new status effect system
    private static readonly EntProtoId MutedEffect = "StatusEffectMuted";
    private static readonly ProtoId<TagPrototype> BoundBloodRiteTag = "BloodRiteBoundItem"; // Whiskey

    [Dependency] private IPrototypeManager _proto = default!;

    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private CuffableSystem _cuffable = default!;
    [Dependency] private EmpSystem _empSystem = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private MindShieldSystem _mindShield = default!;
    [Dependency] private BloodCultRuleSystem _cultRule = default!; // Whiskey
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StatusEffectsNewSystem _statusEffectsNew = default!; // Trauma
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private TagSystem _tag = default!; // Whiskey
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DamageableSystem _damageable = default!; // WhiteDream
    [Dependency] private AudioSystem _audio = default!; // WhiteDream

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BaseCultSpellComponent, ActionAttemptEvent>(OnCultActionAttempt);
        SubscribeLocalEvent<BaseCultSpellComponent, EntityTargetActionEvent>(OnCultTargetEvent);
        SubscribeLocalEvent<BaseCultSpellComponent, ActionGettingDisabledEvent>(OnActionGettingDisabled);

        SubscribeLocalEvent<BloodCultSpellsHolderComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<BloodCultSpellsHolderComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<BloodCultSpellsHolderComponent, BloodCultSelectSpellsEvent>(OnSelectSpellsAction);
        SubscribeLocalEvent<BloodCultSpellsHolderComponent, BloodCultRemoveSpellsEvent>(OnRemoveSpellsAction);
        SubscribeLocalEvent<BloodCultSpellsHolderComponent, RadialSelectorSelectedMessage>(OnSpellSelected);
        SubscribeLocalEvent<BloodCultSpellsHolderComponent, CreateSpeellDoAfterEvent>(OnSpellCreated);

        SubscribeLocalEvent<BloodCultStunEvent>(OnStun);
        SubscribeLocalEvent<BloodCultEmpEvent>(OnEmp);
        SubscribeLocalEvent<BloodCultShacklesEvent>(OnShackles);
        SubscribeLocalEvent<CuffableComponent, BloodCultShacklesDoAfterEvent>(OnShacklesDoAfter);
        SubscribeLocalEvent<SummonEquipmentEvent>(OnSummonEquipment);
    }

    #region BaseHandlers

    private void OnCultActionAttempt(Entity<BaseCultSpellComponent> spell, ref ActionAttemptEvent args)
    {
        if (_statusEffectsNew.HasStatusEffect(args.User, MutedEffect))
            args.Cancelled = true;
    }

    private void OnCultTargetEvent(Entity<BaseCultSpellComponent> spell, ref EntityTargetActionEvent args)
    {
        if (spell.Comp.BypassProtection)
            return;

        // WhiteDream - same story as the offering rune: ask the system, don't look for the component.
        if (_mindShield.IsShielded(args.Target))
            args.Handled = true;
    }

    private void OnActionGettingDisabled(Entity<BaseCultSpellComponent> spell, ref ActionGettingDisabledEvent args)
    {
        if (TryComp(args.Performer, out BloodCultSpellsHolderComponent? spellsHolder))
            spellsHolder.SelectedSpells.Remove(spell);

        if (TryComp<ActionComponent>(spell, out var actionComp))
            _actions.RemoveAction(args.Performer, (spell, actionComp));
    }

    private void OnComponentStartup(Entity<BloodCultSpellsHolderComponent> cultist, ref ComponentStartup args)
    {
        EnsureCultUi(cultist);

        cultist.Comp.MaxSpells = cultist.Comp.DefaultMaxSpells;

        foreach (var actionId in cultist.Comp.ManagementActions)
        {
            var action = _actions.AddAction(cultist, actionId);
            if (action.HasValue)
                cultist.Comp.ManagementActionEnts.Add(action.Value);
        }
    }

    private void OnComponentShutdown(Entity<BloodCultSpellsHolderComponent> cultist, ref ComponentShutdown args)
    {
        foreach (var actionUid in cultist.Comp.ManagementActionEnts)
        {
            if (TryComp<ActionComponent>(actionUid, out var actionComp))
                _actions.RemoveAction(cultist.Owner, (actionUid, actionComp));
        }

        cultist.Comp.ManagementActionEnts.Clear();
    }

    private void OnSelectSpellsAction(Entity<BloodCultSpellsHolderComponent> cultist, ref BloodCultSelectSpellsEvent args)
    {
        if (args.Handled)
            return;

        SelectBloodSpells(cultist);
        args.Handled = true;
    }

    private void OnRemoveSpellsAction(Entity<BloodCultSpellsHolderComponent> cultist, ref BloodCultRemoveSpellsEvent args)
    {
        if (args.Handled)
            return;

        // Whiskey - temporary rites become held items after their one-use action disappears.
        // Dismiss those items here so the removal action can free the hand as expected.
        var removedBoundRite = RemoveBoundBloodRites(cultist);
        if (!removedBoundRite || cultist.Comp.SelectedSpells.Count > 0)
            RemoveBloodSpells(cultist);

        args.Handled = true;
    }

    private bool RemoveBoundBloodRites(Entity<BloodCultSpellsHolderComponent> cultist)
    {
        if (!TryComp<HandsComponent>(cultist, out var hands))
            return false;

        var removed = false;
        foreach (var hand in _hands.EnumerateHands((cultist.Owner, hands)))
        {
            var held = _hands.GetHeldItem((cultist.Owner, hands), hand);
            if (held == null || !_tag.HasTag(held.Value, BoundBloodRiteTag))
                continue;

            // Whiskey - explicit spell removal is allowed to bypass the accidental-drop guard.
            QueueDel(held.Value);
            removed = true;
        }

        return removed;
    }

    private void OnSpellSelected(Entity<BloodCultSpellsHolderComponent> cultist, ref RadialSelectorSelectedMessage args)
    {
        if (!cultist.Comp.AddSpellsMode)
        {
            if (EntityUid.TryParse(args.SelectedItem, out var actionUid))
            {
                if (TryComp<ActionComponent>(actionUid, out var actionComp))
                    _actions.RemoveAction(cultist.Owner, (actionUid, actionComp));
                cultist.Comp.SelectedSpells.Remove(actionUid);
            }

            CloseSpellSelector(cultist);
            return;
        }

        if (cultist.Comp.SelectedSpells.Count >= cultist.Comp.MaxSpells)
        {
            _popup.PopupEntity(Loc.GetString("blood-cult-spells-too-many"), cultist, cultist, PopupType.Medium);
            CloseSpellSelector(cultist);
            return;
        }

        var createSpellEvent = new CreateSpeellDoAfterEvent
        {
            ActionProtoId = args.SelectedItem
        };

        var doAfter = new DoAfterArgs(
            EntityManager,
            cultist.Owner,
            cultist.Comp.SpellCreationTime,
            createSpellEvent,
            cultist.Owner)
        {
            BreakOnMove = true
        };

        CloseSpellSelector(cultist);

        if (!_doAfter.TryStartDoAfter(doAfter, out var doAfterId))
            return;

        cultist.Comp.DoAfterId = doAfterId;
        // WhiteDream - same stab sound as drawing a rune
        _audio.PlayPvs(cultist.Comp.SpellCreationStartSound, cultist.Owner, AudioParams.Default.WithMaxDistance(2f));
    }

    private void OnSpellCreated(Entity<BloodCultSpellsHolderComponent> cultist, ref CreateSpeellDoAfterEvent args)
    {
        cultist.Comp.DoAfterId = null;
        if (args.Handled || args.Cancelled)
            return;

        var action = _actions.AddAction(cultist, args.ActionProtoId);
        if (!action.HasValue)
            return;

        cultist.Comp.SelectedSpells.Add(action.Value);

        // Whiskey - preparing a spell still costs blood, but ritual costs must not fracture body parts.
        var creationDamage = BloodCultDamage.WithoutWounds(cultist.Comp.SpellCreationDamage);
        _damageable.TryChangeDamage(cultist.Owner, creationDamage, true, origin: cultist.Owner);
        _audio.PlayPvs(cultist.Comp.SpellCreationEndSound, cultist.Owner, AudioParams.Default.WithMaxDistance(2f));
    }

    #endregion

    #region SpellsHandlers

    // Whiskey - the stun decays as the cult grows, so it does not turn into a stunlock late in
    // the round, and a mindshield takes half of whatever is left instead of blocking it outright.
    private void OnStun(BloodCultStunEvent ev)
    {
        if (ev.Handled)
            return;

        var decay = GetCultDecay(ev.DecayShare);
        var paralyze = Interpolate(ev.ParalyzeDuration, ev.MinParalyzeDuration, decay);
        var mute = Interpolate(ev.MuteDuration, ev.MinMuteDuration, decay);

        // WhiteDream - the mindshield lives on the implant, never on the person, so ask the system.
        if (_mindShield.IsShielded(ev.Target))
        {
            paralyze *= ev.MindShieldMultiplier;
            mute *= ev.MindShieldMultiplier;
        }

        _statusEffectsNew.TryUpdateStatusEffectDuration(ev.Target, MutedEffect, mute); // Trauma
        _stun.TryAddParalyzeDuration(ev.Target, paralyze);
        ev.Handled = true;
    }

    /// <summary>
    ///     Whiskey - how far along the cult is towards <paramref name="decayShare"/> of the crew.
    ///     0 means the stun is at full strength, 1 means it has bottomed out at the minimum.
    /// </summary>
    private float GetCultDecay(float decayShare)
    {
        var crew = _cultRule.GetProgressionCrewCount();
        if (crew <= 0 || decayShare <= 0f)
            return 0f;

        return Math.Clamp(_cultRule.GetTotalCultists() / (float) crew / decayShare, 0f, 1f);
    }

    // Whiskey
    private static TimeSpan Interpolate(TimeSpan from, TimeSpan to, float amount)
    {
        return from + (to - from) * amount;
    }

    private void OnEmp(BloodCultEmpEvent ev)
    {
        if (ev.Handled)
            return;

        _empSystem.EmpPulse(_transform.GetMapCoordinates(ev.Performer), ev.Range, ev.EnergyConsumption, TimeSpan.FromSeconds(ev.Duration));
        ev.Handled = true;
    }

    private void OnShackles(BloodCultShacklesEvent ev)
    {
        if (ev.Handled)
            return;

        if (!TryComp<CuffableComponent>(ev.Target, out _))
            return;

        var shackles = Spawn(ev.ShacklesProto, Transform(ev.Performer).Coordinates);

        if (!_hands.TryPickupAnyHand(ev.Performer, shackles))
        {
            QueueDel(shackles);
            return;
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            ev.Performer,
            ev.CuffDuration,
            new BloodCultShacklesDoAfterEvent(),
            ev.Target,
            ev.Target,
            shackles)
        {
            BreakOnMove = true,
            NeedHand = true,
            DistanceThreshold = 3f
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            _hands.TryDrop(ev.Performer, shackles);
            QueueDel(shackles);
            return;
        }

        ev.Handled = true;
    }

    private void OnShacklesDoAfter(Entity<CuffableComponent> target, ref BloodCultShacklesDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var user = args.Args.User;
        var shackles = args.Args.Used;

        if (args.Cancelled || shackles == null)
        {
            if (shackles != null)
            {
                _hands.TryDrop(user, shackles.Value);
                QueueDel(shackles.Value);
            }

            return;
        }

        if (!_cuffable.TryAddNewCuffs(target, user, shackles.Value, target))
        {
            _hands.TryDrop(user, shackles.Value);
            QueueDel(shackles.Value);
            return;
        }

        _stun.TryKnockdown(target.Owner, TimeSpan.FromSeconds(1), true);
        _statusEffectsNew.TryUpdateStatusEffectDuration(target.Owner, MutedEffect, TimeSpan.FromSeconds(5)); // Trauma
    }

    private void OnSummonEquipment(SummonEquipmentEvent ev)
    {
        if (ev.Handled)
            return;

        foreach (var (slot, protoId) in ev.Prototypes)
        {
            var entity = Spawn(protoId, _transform.GetMapCoordinates(ev.Performer));
            _hands.TryPickupAnyHand(ev.Performer, entity);
            if (!TryComp(entity, out ClothingComponent? _))
                continue;

            _inventory.TryUnequip(ev.Performer, slot);
            _inventory.TryEquip(ev.Performer, entity, slot, force: true);
        }

        ev.Handled = true;
    }

    #endregion

    #region Helpers

    private void SelectBloodSpells(Entity<BloodCultSpellsHolderComponent> cultist)
    {
        if (!_proto.TryIndex(cultist.Comp.PowersPoolPrototype, out var pool))
            return;

        if (cultist.Comp.SelectedSpells.Count >= cultist.Comp.MaxSpells)
        {
            _popup.PopupEntity(Loc.GetString("blood-cult-spells-too-many"), cultist, cultist, PopupType.Medium);
            return;
        }

        var radialList = new List<RadialSelectorEntry>();
        foreach (var spellId in pool.Powers)
        {
            var entry = new RadialSelectorEntry
            {
                Prototype = spellId,
                Icon = GetActionPrototypeIcon(spellId)
            };

            radialList.Add(entry);
        }

        ShowSpellSelector(cultist, true, new RadialSelectorState(radialList, true));
    }

    private void RemoveBloodSpells(Entity<BloodCultSpellsHolderComponent> cultist)
    {
        if (cultist.Comp.SelectedSpells.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("blood-cult-no-spells"), cultist, cultist, PopupType.Medium);
            return;
        }

        var radialList = new List<RadialSelectorEntry>();
        foreach (var spell in cultist.Comp.SelectedSpells)
        {
            var entry = new RadialSelectorEntry
            {
                Prototype = spell.ToString(),
                Name = Name(spell),
                IconEntity = GetNetEntity(spell) // Trauma - actions have no SpriteSpecifier here
            };

            radialList.Add(entry);
        }

        ShowSpellSelector(cultist, false, new RadialSelectorState(radialList, true));
    }

    /// <summary>
    ///     Whiskey - both menus share one UI key, and the old code called TryToggleUi, which only
    ///     ever flips. Going straight from one menu to the other closed the window instead of swapping
    ///     what was in it, so it took two clicks. Toggle now only closes when it is already this menu.
    /// </summary>
    private void ShowSpellSelector(
        Entity<BloodCultSpellsHolderComponent> cultist,
        bool addSpellsMode,
        RadialSelectorState state
    )
    {
        var alreadyOpen = _ui.IsUiOpen(cultist.Owner, RadialSelectorUiKey.Key, cultist.Owner);
        var sameMenu = cultist.Comp.AddSpellsMode == addSpellsMode;

        cultist.Comp.AddSpellsMode = addSpellsMode;

        if (alreadyOpen && sameMenu)
        {
            CloseSpellSelector(cultist);
            return;
        }

        _ui.SetUiState(cultist.Owner, RadialSelectorUiKey.Key, state);
        _ui.OpenUi(cultist.Owner, RadialSelectorUiKey.Key, cultist.Owner);
    }

    private void EnsureCultUi(EntityUid uid)
    {
        _ui.SetUi(uid, RadialSelectorUiKey.Key, new InterfaceData("RadialSelectorMenuBUI"));
        _ui.SetUi(uid, ListViewSelectorUiKey.Key, new InterfaceData("ListViewSelectorBUI"));
    }

    private void CloseSpellSelector(Entity<BloodCultSpellsHolderComponent> cultist)
    {
        _ui.CloseUi(cultist.Owner, RadialSelectorUiKey.Key, cultist.Owner);
    }

    // <Trauma>
    // Action icons are no longer a field on ActionComponent: they come from the action entity's own
    // Sprite component. Returning null makes the radial menu resolve the icon from the prototype itself.
    private SpriteSpecifier? GetActionPrototypeIcon(string protoId) => null;
    // </Trauma>

    #endregion
}
