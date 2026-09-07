// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// O lado que mexe em dinheiro. Todo caminho de saldo do jogo passa por aqui,
/// para existir um lugar só onde valor negativo, saldo insuficiente e estouro
/// de inteiro são tratados.
///
/// Está no servidor porque saldo não se prevê no cliente: o valor errado
/// aparecendo por meio segundo já é motivo de reclamação, e o valor errado
/// vindo do cliente é motivo de exploit.
/// </summary>
public sealed class CreditAccountSystem : SharedCreditAccountSystem
{
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

        SetBalance((conta.Owner, conta.Comp), conta.Comp.Balance + valor);
        return true;
    }

    /// <summary>
    /// Tira dinheiro da conta, e só quando ele está lá. Devolve por
    /// <paramref name="restante"/> o que sobrou depois do saque.
    /// </summary>
    public bool TryWithdraw(Entity<CreditAccountComponent?> conta, int valor, out int restante)
    {
        restante = 0;

        if (valor <= 0 || !Resolve(conta, ref conta.Comp) || conta.Comp.Balance < valor)
            return false;

        restante = conta.Comp.Balance - valor;
        SetBalance((conta.Owner, conta.Comp), restante);
        return true;
    }

    /// <inheritdoc cref="TryWithdraw(Entity{CreditAccountComponent?}, int, out int)"/>
    public bool TryWithdraw(Entity<CreditAccountComponent?> conta, int valor)
    {
        return TryWithdraw(conta, valor, out _);
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

        SetBalance((origem.Owner, origem.Comp), origem.Comp.Balance - valor);
        SetBalance((destino.Owner, destino.Comp), destino.Comp.Balance + valor);
        return true;
    }
}
