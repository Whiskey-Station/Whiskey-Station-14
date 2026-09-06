// SPDX-License-Identifier: MIT
// Portado de https://github.com/RMC-14/RMC-14 (PR #9173)
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.PlayingCards;

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
    public SoundSpecifier? ShuffleSound = new SoundPathSpecifier("/Audio/_RMC14/Handling/paper_drop.ogg");

    [DataField]
    public float PopupCooldown = 2f;

    [ViewVariables]
    public TimeSpan LastPopupTime;
}
