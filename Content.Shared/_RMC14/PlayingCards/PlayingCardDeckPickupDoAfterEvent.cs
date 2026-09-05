// SPDX-License-Identifier: MIT
// Portado de https://github.com/RMC-14/RMC-14 (PR #9173)
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.PlayingCards;

/// <summary>
/// Evento disparado ao pegar cartas soltas do chão e adicioná-las ao baralho.
/// Portado do RMC-14 (PR #9173).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class PlayingCardDeckPickupDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public IReadOnlyList<NetEntity> Entities = default!;

    private PlayingCardDeckPickupDoAfterEvent()
    {
    }

    public PlayingCardDeckPickupDoAfterEvent(List<NetEntity> entities)
    {
        Entities = entities;
    }

    public override DoAfterEvent Clone() => this;
}
