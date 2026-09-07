// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Economy;
using Content.Trauma.Shared._Whiskey.Economy;
using Content.Shared.Popups;
using Content.Trauma.Shared.VendingMachines;

namespace Content.Trauma.Server._Whiskey.Economy;

/// <summary>
/// Cobra da conta do crachá quando alguém compra na máquina de loja.
///
/// Fica no servidor porque tirar dinheiro é escrita, e escrita de saldo é
/// exclusiva do servidor. A máquina levanta o evento de compra e espera
/// alguém marcar como pago; se ninguém marcar, ela recusa sozinha.
/// </summary>
public sealed partial class CreditVendorPaymentSystem : EntitySystem
{
    [Dependency] private CreditAccountSystem _contas = default!;
    [Dependency] private CreditVendorSystem _leitor = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditVendorComponent, ShopVendorPurchaseEvent>(OnCompra);
    }

    private void OnCompra(Entity<CreditVendorComponent> ent, ref ShopVendorPurchaseEvent args)
    {
        if (args.Cost > int.MaxValue)
            return;

        // Quem paga é o cartão que está DENTRO da máquina, e não o que a
        // pessoa tem por perto. Sem cartão, a máquina recusa e diz por quê,
        // senão vira negativa muda e ninguém descobre que faltava inserir.
        if (_leitor.GetCartao(ent) is not { } cartao)
        {
            _popup.PopupEntity(Loc.GetString("credit-vendor-no-card"), ent, args.User);
            return;
        }

        if (_contas.TryWithdraw(cartao, (int) args.Cost))
            args.Paid = true;
    }
}
