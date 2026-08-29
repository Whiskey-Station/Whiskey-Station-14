// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines

using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._EinsteinEngines.Overlays;

namespace Content.Client._EinsteinEngines.Overlays;

// Whiskey: partial e sem readonly nos [Dependency], que é o que os
// analisadores RA0049 e RA0051 deste engine exigem.
public sealed partial class SaturationScaleOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] IEntityManager _entityManager = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _shader;

    // ProtoId em vez de literal: o RA0033 proíbe literal no Index.
    private static readonly ProtoId<ShaderPrototype> Shader = "SaturationScale";
    private float _currentSaturation = 1f;

    public SaturationScaleOverlay()
    {
        IoCManager.InjectDependencies(this);

        _shader = _prototypeManager.Index(Shader).Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { Valid: true } player
            || !_entityManager.HasComponent<SaturationScaleOverlayComponent>(player))
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || _playerManager.LocalEntity is not { Valid: true } player
            || !_entityManager.HasComponent<SaturationScaleOverlayComponent>(player))
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("saturation", _currentSaturation);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        if (ScreenTexture is null || _playerManager.LocalEntity is not { Valid: true } player
            || !_entityManager.TryGetComponent(player, out SaturationScaleOverlayComponent? saturationComp)
            || _currentSaturation == saturationComp.SaturationScale)
            return;

        var deltaTSlower = args.DeltaSeconds * saturationComp.FadeInMultiplier;
        var saturationFadeIn = saturationComp.SaturationScale > _currentSaturation
            ? deltaTSlower : -deltaTSlower;

        _currentSaturation += saturationFadeIn;
        _shader.SetParameter("saturation", _currentSaturation);
    }
}
