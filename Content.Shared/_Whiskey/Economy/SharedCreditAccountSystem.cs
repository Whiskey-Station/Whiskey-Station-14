// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Access.Systems;
using Content.Shared.Examine;

namespace Content.Shared._Whiskey.Economy;

/// <summary>
/// A parte da conta que os dois lados precisam: achar a conta de alguém, ler o
/// saldo, e mostrar o valor ao examinar.
///
/// Mexer no saldo NÃO mora aqui. A escrita fica no sistema de servidor, e essa
/// separação é o que impede código de cliente de chamar depósito ou saque: sem
/// o tipo, não compila. Antes disto a classe era concreta e em shared, e só não
/// acontecia porque ninguém tinha tentado.
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
    /// Acha a conta de alguém: a da própria entidade quando ela mesma tem uma,
    /// senão a do cartão que ela carrega, veste ou tem dentro do PDA.
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
    /// Saldo da conta, ou zero quando não existe conta nenhuma. Zero é a
    /// resposta certa para quem não tem cartão: quem não tem conta não tem
    /// dinheiro, e não é caso de erro.
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
    /// Escreve o saldo novo e avisa quem estiver ouvindo. É protegido porque
    /// só o sistema de servidor pode chegar aqui, e é o único ponto do jogo
    /// que altera dinheiro.
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
