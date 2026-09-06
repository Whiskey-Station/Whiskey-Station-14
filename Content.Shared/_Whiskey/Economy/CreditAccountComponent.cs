// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Whiskey.Economy;

/// <summary>
/// Guarda o dinheiro de uma pessoa. Fica no cartão de identificação, e não na
/// pessoa, por três motivos:
///
/// 1. O cartão já é a identidade neste jogo: é ele que abre porta, que assina
///    registro e que o roubo já mira. Pendurar o saldo nele faz o roubo de ID
///    passar a valer alguma coisa, em vez de criar um alvo novo.
/// 2. É o que a estação já faz com ponto de mineração, no
///    <c>MiningPointsComponent</c>, que mora no mesmo cartão. Dois saldos no
///    mesmo lugar é uma regra; um em cada lugar é uma exceção para explicar.
/// 3. Cyborg e silício não têm cartão, então dar conta a eles vira decisão
///    explícita de pôr o componente no chassi, e não um efeito colateral.
///
/// O saldo não persiste entre rodadas. A rodada acaba, o cartão é apagado com
/// o resto do mapa, e a estação seguinte começa do zero. Isso é escolha, não
/// limitação: economia que atravessa rodada precisa de banco de dados, e faz
/// quem joga todo dia acumular vantagem sobre quem entrou hoje.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(CreditAccountSystem))]
[AutoGenerateComponentState]
public sealed partial class CreditAccountComponent : Component
{
    /// <summary>
    /// Quanto a conta tem. Nunca fica negativo: não existe dívida, e a compra
    /// que não cabe no saldo simplesmente é recusada.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Balance;
}

/// <summary>
/// Levantado na própria conta toda vez que o saldo muda, com o valor de antes
/// e o de depois. Serve para interface, para registro de admin e para quem
/// quiser reagir a pagamento sem consultar em laço.
/// </summary>
[ByRefEvent]
public readonly record struct CreditBalanceChangedEvent(int Anterior, int Atual);
