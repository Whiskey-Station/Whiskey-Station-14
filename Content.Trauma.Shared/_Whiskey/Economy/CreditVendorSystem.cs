// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;
using Content.Shared.Containers.ItemSlots;
using Content.Trauma.Shared.VendingMachines;

namespace Content.Trauma.Shared._Whiskey.Economy;

/// <summary>
/// Responde à máquina de loja quanto a pessoa na frente dela tem em spesos.
///
/// Só leitura, e por isso pode viver em shared: a interface da máquina precisa
/// desse número dos dois lados para desenhar a lista com o que dá e o que não
/// dá para comprar. Quem tira dinheiro é o
/// <c>CreditVendorPaymentSystem</c>, no servidor.
/// </summary>
public sealed partial class CreditVendorSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedCreditAccountSystem _contas = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditVendorComponent, ShopVendorBalanceEvent>(OnSaldo);
    }

    private void OnSaldo(Entity<CreditVendorComponent> ent, ref ShopVendorBalanceEvent args)
    {
        if (GetCartao(ent) is not { } cartao)
            return;

        // A interface da máquina conta em uint e a conta guarda int. Saldo
        // nunca fica negativo, mas a conversão fica explícita porque negativo
        // virado em uint aparece como bilhões na tela da máquina.
        var saldo = _contas.GetBalance(cartao);
        args.Balance = saldo > 0 ? (uint) saldo : 0;
    }

    /// <summary>
    /// O cartão que está dentro da máquina, ou nada.
    /// </summary>
    public EntityUid? GetCartao(Entity<CreditVendorComponent> ent)
    {
        return _slots.GetItemOrNull(ent.Owner, ent.Comp.SlotId);
    }
}
