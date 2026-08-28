using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Trauma.PlayingCards;

/// <summary>
/// Component for a deck of playing cards.
/// Ported from RMC-14 (PR #9173).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedPlayingCardSystem))]
public sealed partial class PlayingCardDeckComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<int> CardOrder = new();

    [DataField]
    public EntProtoId CardPrototype = "TraumaPlayingCard";

    [DataField]
    public SoundSpecifier? DrawSound = new SoundPathSpecifier("/Audio/_Trauma/Handling/paper_pickup.ogg");

    [DataField]
    public SoundSpecifier? ShuffleSound = new SoundPathSpecifier("/Audio/_Trauma/Handling/paper_drop.ogg");

    [DataField, AutoNetworkedField]
    public int MaxCards = 52;
}
