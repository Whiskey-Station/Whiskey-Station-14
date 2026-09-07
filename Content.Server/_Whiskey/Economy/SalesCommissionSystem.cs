// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;
using Content.Shared.Popups;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Paga a comissão de venda de carga, ouvindo o evento do console de venda.
/// </summary>
public sealed partial class SalesCommissionSystem : EntitySystem
{
    [Dependency] private CreditAccountSystem _contas = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CargoPalletSoldEvent>(OnVendido);
    }

    private void OnVendido(ref CargoPalletSoldEvent args)
    {
        if (args.Total <= 0 || !TryComp<SalesCommissionComponent>(args.Station, out var regra))
            return;

        var valor = Comissao(regra, args.Total);
        if (valor <= 0)
            return;

        if (!_contas.TryGetAccount(args.Seller, out var conta) || !_contas.TryDeposit(conta.Owner, valor))
            return;

        _popup.PopupEntity(Loc.GetString("cargo-sale-commission", ("valor", valor)), args.Console, args.Seller);
    }

    public int Comissao(SalesCommissionComponent regra, int total)
    {
        return Math.Min((int) (total * regra.Cut), regra.MaxPerSale);
    }
}

/// <summary>
/// A estação vendeu o que estava nos pallets, com total e com quem vendeu.
/// </summary>
[ByRefEvent]
public readonly record struct CargoPalletSoldEvent(EntityUid Station, EntityUid Seller, EntityUid Console, int Total);
