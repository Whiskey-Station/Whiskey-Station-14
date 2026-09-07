// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Economy;

namespace Content.Client._Whiskey.Economy;

/// <summary>
/// O lado do cliente da conta. Vazio de propósito: ele existe para o cliente
/// conseguir ler saldo e desenhar o examinar, e para não conseguir mais nada.
/// </summary>
public sealed class CreditAccountSystem : SharedCreditAccountSystem;
