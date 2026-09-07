// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Trauma.Shared.VendingMachines;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Trauma.Shared._Whiskey.Economy.Cartridge;

/// <summary>
/// Loja dentro do PDA. Paga com o cartão que está no próprio PDA.
///
/// É isso que resolve de graça a confusão que a máquina tinha: no app não
/// existe escolha de cartão. O PDA guarda um cartão só, então é sempre o
/// dinheiro de quem está segurando o PDA, e nunca o de um cartão solto que a
/// pessoa por acaso tinha na mão.
///
/// Cada cartucho aponta para a própria lista, então dá para existir uma loja
/// por cargo sem código novo: só um cartucho a mais e uma lista a mais.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StoreCartridgeComponent : Component
{
    /// <summary>
    /// A lista do que este app vende.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ShopInventoryPrototype> Pack;
}
