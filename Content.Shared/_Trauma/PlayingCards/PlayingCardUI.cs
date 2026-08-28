using Robust.Shared.Serialization;

namespace Content.Shared._Trauma.PlayingCards;

/// <summary>
/// Chaves de UI e mensagens de rede para mãos de cartas.
/// Portado do RMC-14 (PR #9173).
/// </summary>
[Serializable, NetSerializable]
public enum PlayingCardHandUi
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PlayingCardHandBuiMsg(int cardIndex) : BoundUserInterfaceMessage
{
    public readonly int CardIndex = cardIndex;
}
