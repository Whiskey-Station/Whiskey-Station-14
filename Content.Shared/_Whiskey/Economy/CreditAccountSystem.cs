// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Access.Systems;
using Content.Shared.Examine;

namespace Content.Shared._Whiskey.Economy;

/// <summary>
/// Mexe no saldo das contas. Todo caminho de dinheiro do jogo passa por aqui,
/// para existir um lugar só onde valor negativo, saldo insuficiente e estouro
/// de inteiro são tratados.
///
/// O componente é <c>Access</c> deste sistema de propósito: quem escrever
/// <c>comp.Balance = x</c> de fora não compila, e é obrigado a usar a API que
/// levanta o <see cref="CreditBalanceChangedEvent"/>. Sem isso a interface
/// mostra um número e a conta tem outro.
/// </summary>
public sealed partial class CreditAccountSystem : EntitySystem
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
    /// Põe dinheiro na conta. Recusa valor zero ou negativo, porque depósito
    /// negativo seria um saque sem a checagem de saldo do saque.
    /// </summary>
    public bool TryDeposit(Entity<CreditAccountComponent?> conta, int valor)
    {
        if (valor <= 0 || !Resolve(conta, ref conta.Comp))
            return false;

        // Estouro de int volta para negativo em silêncio, e o saldo que era
        // grande vira dívida. Conferir antes é mais barato que consertar isso
        // com jogador dentro.
        if (conta.Comp.Balance > int.MaxValue - valor)
            return false;

        AjustarSaldo((conta.Owner, conta.Comp), valor);
        return true;
    }

    /// <summary>
    /// Tira dinheiro da conta, e só quando ele está lá.
    /// </summary>
    public bool TryWithdraw(Entity<CreditAccountComponent?> conta, int valor)
    {
        if (valor <= 0 || !Resolve(conta, ref conta.Comp) || conta.Comp.Balance < valor)
            return false;

        AjustarSaldo((conta.Owner, conta.Comp), -valor);
        return true;
    }

    /// <summary>
    /// Passa dinheiro de uma conta para outra.
    /// </summary>
    public bool TryTransfer(Entity<CreditAccountComponent?> origem, Entity<CreditAccountComponent?> destino, int valor)
    {
        if (valor <= 0 || origem.Owner == destino.Owner)
            return false;

        if (!Resolve(origem, ref origem.Comp) || !Resolve(destino, ref destino.Comp))
            return false;

        // As DUAS pontas são conferidas antes de qualquer escrita. Sacar
        // primeiro e descobrir depois que o depósito não cabe apaga o
        // dinheiro no meio do caminho.
        if (origem.Comp.Balance < valor || destino.Comp.Balance > int.MaxValue - valor)
            return false;

        AjustarSaldo((origem.Owner, origem.Comp), -valor);
        AjustarSaldo((destino.Owner, destino.Comp), valor);
        return true;
    }

    private void AjustarSaldo(Entity<CreditAccountComponent> conta, int delta)
    {
        var anterior = conta.Comp.Balance;
        conta.Comp.Balance += delta;
        Dirty(conta);

        var ev = new CreditBalanceChangedEvent(anterior, conta.Comp.Balance);
        RaiseLocalEvent(conta.Owner, ref ev);
    }
}
