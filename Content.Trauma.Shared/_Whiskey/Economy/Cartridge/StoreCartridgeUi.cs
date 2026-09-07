// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Trauma.Shared._Whiskey.Economy.Cartridge;

/// <summary>
/// O que o app manda para a tela: quanto o cartão do PDA tem, e o que está à
/// venda.
/// </summary>
[Serializable, NetSerializable]
public sealed class StoreCartridgeUiState : BoundUserInterfaceState
{
    public readonly int Balance;
    public readonly List<StoreCartridgeEntry> Listings;
    public readonly bool HasCard;

    public StoreCartridgeUiState(int balance, List<StoreCartridgeEntry> listings, bool hasCard)
    {
        Balance = balance;
        Listings = listings;
        HasCard = hasCard;
    }
}

/// <summary>
/// Uma linha da loja. O índice é a posição na lista do prototype, e é ele que
/// volta na compra: mandar o nome de volta deixaria o cliente escolher o que
/// comprar por texto.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct StoreCartridgeEntry(int Index, string Name, uint Cost);

/// <summary>
/// A tela pedindo para comprar a linha de índice tal.
/// </summary>
[Serializable, NetSerializable]
public sealed class StoreCartridgeBuyMessage : CartridgeMessageEvent
{
    public readonly int Index;

    public StoreCartridgeBuyMessage(int index)
    {
        Index = index;
    }
}
