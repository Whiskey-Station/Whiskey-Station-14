// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;
using Content.Trauma.Shared.VendingMachines;

namespace Content.Trauma.Shared._Whiskey.Economy;

/// <summary>
/// Atende as duas perguntas que a máquina de loja faz, pagando com o saldo do
/// crachá.
///
/// Fica em sistema próprio, e não dentro do <c>SharedShopVendorSystem</c> ao
/// lado do adaptador de ponto de mineração, para o próximo upstream do Trauma
/// não dar conflito num arquivo deles por causa de moeda nossa.
/// </summary>
public sealed partial class CreditVendorSystem : EntitySystem
{
    [Dependency] private CreditAccountSystem _contas = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditVendorComponent, ShopVendorBalanceEvent>(OnSaldo);
        SubscribeLocalEvent<CreditVendorComponent, ShopVendorPurchaseEvent>(OnCompra);
    }

    private void OnSaldo(Entity<CreditVendorComponent> ent, ref ShopVendorBalanceEvent args)
    {
        // A interface da máquina conta em uint e a conta guarda int. Saldo
        // nunca fica negativo, mas a conversão fica explícita porque negativo
        // virado em uint aparece como bilhões na tela da máquina.
        var saldo = _contas.GetUserBalance(args.User);
        args.Balance = saldo > 0 ? (uint) saldo : 0;
    }

    private void OnCompra(Entity<CreditVendorComponent> ent, ref ShopVendorPurchaseEvent args)
    {
        if (args.Cost > int.MaxValue)
            return;

        if (!_contas.TryGetAccount(args.User, out var conta))
            return;

        if (_contas.TryWithdraw(conta.Owner, (int) args.Cost))
            args.Paid = true;
    }
}
