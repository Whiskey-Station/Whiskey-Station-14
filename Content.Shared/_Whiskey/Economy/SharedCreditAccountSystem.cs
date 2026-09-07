// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Access.Systems;
using Content.Shared.Examine;

namespace Content.Shared._Whiskey.Economy;

/// <summary>
/// Leitura de saldo, dos dois lados. A escrita mora no sistema de servidor, e
/// é essa separação que impede o cliente de chamar depósito ou saque.
/// </summary>
public abstract partial class SharedCreditAccountSystem : EntitySystem
{
    [Dependency] private SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditAccountComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<CreditAccountComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("credit-account-examine", ("saldo", ent.Comp.Balance)));
    }

    /// <summary>
    /// A conta da entidade, ou a do cartão que ela carrega, veste ou tem no PDA.
    /// </summary>
    public bool TryGetAccount(EntityUid portador, out Entity<CreditAccountComponent> conta)
    {
        if (TryComp<CreditAccountComponent>(portador, out var propria))
        {
            conta = (portador, propria);
            return true;
        }

        if (_idCard.TryFindIdCard(portador, out var cartao) &&
            TryComp<CreditAccountComponent>(cartao.Owner, out var doCartao))
        {
            conta = (cartao.Owner, doCartao);
            return true;
        }

        conta = default;
        return false;
    }

    /// <summary>
    /// Saldo da conta, ou zero sem conta. Sem cartão não é erro, é zero.
    /// </summary>
    public int GetBalance(Entity<CreditAccountComponent?> conta)
    {
        return Resolve(conta, ref conta.Comp, logMissing: false) ? conta.Comp.Balance : 0;
    }

    /// <inheritdoc cref="GetBalance"/>
    public int GetUserBalance(EntityUid portador)
    {
        return TryGetAccount(portador, out var conta) ? conta.Comp.Balance : 0;
    }

    /// <summary>
    /// Único ponto do jogo que altera saldo. Protegido: só o servidor chega aqui.
    /// </summary>
    protected void SetBalance(Entity<CreditAccountComponent> conta, int novo)
    {
        var anterior = conta.Comp.Balance;
        if (anterior == novo)
            return;

        conta.Comp.Balance = novo;
        Dirty(conta);

        var ev = new CreditBalanceChangedEvent(anterior, novo);
        RaiseLocalEvent(conta.Owner, ref ev);
    }
}
