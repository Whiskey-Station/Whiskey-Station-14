// SPDX-License-Identifier: AGPL-3.0-or-later
// Blood Cult: ported from WWhiteDreamProject/wwdpublic. See Content.Shared/WhiteDream/BloodCult/ATTRIBUTION.md

using Content.Shared.WhiteDream.BloodCult;
using Content.Shared.WhiteDream.BloodCult.Components;
using Robust.Client.GameObjects;

namespace Content.Client.WhiteDream.BloodCult;

public sealed partial class PylonVisualizerSystem : VisualizerSystem<PylonComponent>
{
    protected override void OnAppearanceChange(EntityUid uid,
        PylonComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !AppearanceSystem.TryGetData<bool>(uid,
                PylonVisuals.Activated,
                out var active,
                args.Component))
        {
            return;
        }

        // Whiskey - the original collector freezes both animated layers while it is disabled.
        SpriteSystem.LayerSetAutoAnimated((uid, args.Sprite), PylonVisuals.BaseLayer, active);
        SpriteSystem.LayerSetAutoAnimated((uid, args.Sprite), PylonVisuals.Layer, active);
    }
}
