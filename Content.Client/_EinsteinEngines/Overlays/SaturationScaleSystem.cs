// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines

using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Overlays;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._EinsteinEngines.Overlays;

// Whiskey: partial e sem readonly nos [Dependency], que é o que os
// analisadores RA0049 e RA0051 deste engine exigem.
public sealed partial class SaturationScaleSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private ISharedPlayerManager _playerMan = default!;
    [Dependency] private IConfigurationManager _cfgMan = default!;

    private SaturationScaleOverlay _overlay = default!;
    private bool _moodEffectsEnabled;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new();
        _moodEffectsEnabled = _cfgMan.GetCVar(CCVars.MoodVisualEffects);
        _cfgMan.OnValueChanged(CCVars.MoodVisualEffects, HandleMoodEffectsUpdated);

        SubscribeLocalEvent<SaturationScaleOverlayComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SaturationScaleOverlayComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<SaturationScaleOverlayComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SaturationScaleOverlayComponent, PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(RoundRestartCleanup);
    }

    private void HandleMoodEffectsUpdated(bool moodEffectsEnabled)
    {
        if (_overlayMan.HasOverlay<SaturationScaleOverlay>() && !moodEffectsEnabled)
            _overlayMan.RemoveOverlay(_overlay);

        _moodEffectsEnabled = moodEffectsEnabled;
    }

    private void RoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        if (!_moodEffectsEnabled)
            return;

        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, SaturationScaleOverlayComponent component, PlayerDetachedEvent args)
    {
        if (!_moodEffectsEnabled)
            return;

        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, SaturationScaleOverlayComponent component, PlayerAttachedEvent args)
    {
        if (!_moodEffectsEnabled)
            return;

        _overlayMan.AddOverlay(_overlay);
    }

    private void OnShutdown(EntityUid uid, SaturationScaleOverlayComponent component, ComponentShutdown args)
    {
        if (uid != _playerMan.LocalEntity || !_moodEffectsEnabled)
            return;

        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnInit(EntityUid uid, SaturationScaleOverlayComponent component, ComponentInit args)
    {
        if (uid != _playerMan.LocalEntity || !_moodEffectsEnabled)
            return;

        _overlayMan.AddOverlay(_overlay);
    }
}
