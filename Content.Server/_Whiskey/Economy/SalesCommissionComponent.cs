// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Comissão de venda: quanto do que a estação vende vai para o crachá de quem
/// apertou o botão.
///
/// É a única torneira da economia, e é de propósito que ela seja essa. Salário
/// só move dinheiro que já existia dentro da estação, e a loja consome. Sem
/// nada entrando de fora, o dinheiro acaba no meio da rodada e o sistema morre
/// sozinho.
///
/// Quem paga a comissão é o comprador, e não a estação: o valor vem por cima
/// da venda, do mesmo jeito que corretor recebe do negócio e não do vendedor.
/// Isso põe dinheiro novo no mundo, e por isso vem com teto.
///
/// O teto é o que segura o abuso óbvio, que é comprar caixa com dinheiro da
/// estação e revender para embolsar a fatia. Esse laço existe e não é bug: é
/// desvio de verba, dá para auditar pelo console de finanças, e o orçamento
/// esvaziando denuncia sozinho. O teto só impede que ele escale.
/// </summary>
[RegisterComponent]
public sealed partial class SalesCommissionComponent : Component
{
    /// <summary>
    /// Fatia da venda que vira comissão.
    /// </summary>
    [DataField]
    public double Cut = 0.05;

    /// <summary>
    /// Teto por venda. Vender o dobro não paga o dobro depois daqui, então o
    /// jeito de ganhar mais é vender mais vezes, e cada vez custa trabalho.
    /// </summary>
    [DataField]
    public int MaxPerSale = 250;
}
