// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines

using Robust.Shared.GameStates;

namespace Content.Shared._EinsteinEngines.Overlays;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SaturationScaleOverlayComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SaturationScale = 1f;

    /// <summary>
    ///     Modifies how quickly the saturation "fades in", normally at a rate of 1% per second times this multiplier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FadeInMultiplier = 0.1f;
}
