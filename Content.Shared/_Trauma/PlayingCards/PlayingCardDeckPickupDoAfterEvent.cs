using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Trauma.PlayingCards;

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
