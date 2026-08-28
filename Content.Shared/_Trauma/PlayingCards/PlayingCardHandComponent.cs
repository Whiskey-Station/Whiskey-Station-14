using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Trauma.PlayingCards;

/// <summary>
/// Componente para uma mão ou pilha de cartas.
/// Portado do RMC-14 (PR #9173).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedPlayingCardSystem))]
public sealed partial class PlayingCardHandComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<int> Cards = new();

    [DataField]
    public int MaxCards = 52;

    [DataField, AutoNetworkedField]
    public bool FaceUp;

    [DataField]
    public SoundSpecifier? ShuffleSound = new SoundPathSpecifier("/Audio/_Trauma/Handling/paper_drop.ogg");

    [DataField]
    public float PopupCooldown = 2f;

    [ViewVariables]
    public TimeSpan LastPopupTime;
}
