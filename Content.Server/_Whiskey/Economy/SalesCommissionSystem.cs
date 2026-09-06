// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;
using Content.Shared.Popups;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Paga a comissão de venda de carga. Ouve o evento que o console de venda
/// levanta, para a lógica de dinheiro nosso não morar dentro do arquivo deles.
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

    /// <summary>
    /// Quanto sai de comissão numa venda daquele tamanho.
    /// </summary>
    public int Comissao(SalesCommissionComponent regra, int total)
    {
        return Math.Min((int) (total * regra.Cut), regra.MaxPerSale);
    }
}

/// <summary>
/// Levantado quando a estação vende o que estava nos pallets, com o total e
/// com quem apertou o botão.
/// </summary>
[ByRefEvent]
public readonly record struct CargoPalletSoldEvent(EntityUid Station, EntityUid Seller, EntityUid Console, int Total);
