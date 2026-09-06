// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Trauma.Shared._Whiskey.Economy;

/// <summary>
/// Faz um <c>ShopVendorComponent</c> cobrar em spesos da conta do crachá, do
/// mesmo jeito que o <c>PointsVendorComponent</c> cobra em ponto de mineração.
///
/// A máquina de loja do Trauma já pergunta quanto a pessoa tem e já manda
/// cobrar, por evento. Quem responde é o componente da moeda. Então moeda
/// nova não é máquina nova: é só alguém a mais atendendo essas duas
/// perguntas.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CreditVendorComponent : Component;
