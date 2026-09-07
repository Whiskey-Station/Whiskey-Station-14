// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Escrita de saldo. Fica no servidor: dinheiro não se prevê no cliente.
/// </summary>
public sealed class CreditAccountSystem : SharedCreditAccountSystem
{
    /// <summary>
    /// Põe dinheiro na conta. Valor zero ou negativo é recusado: depósito
    /// negativo seria saque sem a checagem de saldo.
    /// </summary>
    public bool TryDeposit(Entity<CreditAccountComponent?> conta, int valor)
    {
        if (valor <= 0 || !Resolve(conta, ref conta.Comp))
            return false;

        // Estouro de int vira dívida em silêncio.
        if (conta.Comp.Balance > int.MaxValue - valor)
            return false;

        SetBalance((conta.Owner, conta.Comp), conta.Comp.Balance + valor);
        return true;
    }

    /// <summary>
    /// Tira dinheiro da conta. <paramref name="restante"/> devolve o que sobrou.
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

    public bool TryTransfer(Entity<CreditAccountComponent?> origem, Entity<CreditAccountComponent?> destino, int valor)
    {
        if (valor <= 0 || origem.Owner == destino.Owner)
            return false;

        if (!Resolve(origem, ref origem.Comp) || !Resolve(destino, ref destino.Comp))
            return false;

        // As duas pontas antes de qualquer escrita: sacar e só depois descobrir
        // que o depósito não cabe apaga o dinheiro no caminho.
        if (origem.Comp.Balance < valor || destino.Comp.Balance > int.MaxValue - valor)
            return false;

        SetBalance((origem.Owner, origem.Comp), origem.Comp.Balance - valor);
        SetBalance((destino.Owner, destino.Comp), destino.Comp.Balance + valor);
        return true;
    }
}
