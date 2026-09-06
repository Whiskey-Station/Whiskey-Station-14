// SPDX-License-Identifier: AGPL-3.0-or-later


namespace Content.Trauma.Shared.Wizard.ArcaneBarrage;

[RegisterComponent, NetworkedComponent]
public sealed partial class ArcaneBarrageComponent : Component
{
    [DataField]
    public bool SwapHandsOnShot = true; // Whiskey

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Unremoveable = true;
}
