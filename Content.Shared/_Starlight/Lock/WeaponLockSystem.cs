// SPDX-FileCopyrightText: 2024-2026 Starlight
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: MIT
//
// Portado de https://github.com/ss14Starlight/space-station-14

using Content.Shared.Popups;
using Content.Shared.Lock;
using Content.Shared.Hands;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared.Starlight.Lock;

public sealed partial class WeaponLockSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private LockSystem _lock = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LockComponent, AttemptShootEvent>(OnShootAttempt);
        SubscribeLocalEvent<LockComponent, GotUnequippedHandEvent>(OnUnequipHand);
        SubscribeLocalEvent<LockComponent, GotEquippedHandEvent>(OnEquipHand);
    }

    private void OnShootAttempt(EntityUid uid, LockComponent component, ref AttemptShootEvent args)
    {
        if (!_lock.IsLocked((uid, component)))
            return;

        args.Cancelled = true;
        _popup.PopupPredicted(Loc.GetString("lock-comp-weapon-locked"), uid, args.User, PopupType.MediumCaution);
    }

    private void OnUnequipHand(EntityUid uid, LockComponent component, GotUnequippedHandEvent args)
    {
        // <Whiskey> - não trava o que está sendo destruído.
        //
        // Este evento também dispara quando a entidade sai da mão porque está
        // sendo deletada, e não porque alguém a soltou. Travar aí não serve para
        // nada, e o Lock toca som: o resultado é áudio numa entidade que já está
        // terminando.
        //
        // Aparecia como "Tried to play coordinates audio on a terminating /
        // deleted entity", e derrubava o AllItemsHaveSpritesTest, que cria e
        // destrói todo item do jogo. O defeito já existia e ficou visível quando
        // o engine passou para 289.0.2.
        if (TerminatingOrDeleted(uid))
            return;
        // </Whiskey>

        if (component.AutoLock)
            _lock.Lock(uid, null, component);
    }

    private void OnEquipHand(EntityUid uid, LockComponent component, GotEquippedHandEvent args)
    {
        if (component.AutoUnlock)
            _lock.TryUnlock(uid, args.User, component);
    }
}
