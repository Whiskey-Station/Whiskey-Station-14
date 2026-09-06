// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.GameTicking;
using Content.Shared.Random.Helpers;
using Content.Trauma.Common.CCVar;
using Content.Trauma.Common.LinkAccount;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Trauma.Client.RoundEndCredits;

public sealed partial class RoundEndCreditsSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private ILinkAccountManager _linkAccount = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private float _timer;
    private EndRoundCreditsControl? _creditsContainer;
    private BoxContainer? _exitContainer;
    private bool _showCredits = true;
    private bool Debug = false; // Set this to true if you want a bunch of dummy characters to spawn

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RoundEndMessageEvent>(OnRoundEnd);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundCleanup);

        Subs.CVar(_cfg, TraumaCVars.PlayMovieEndCredits, x => _showCredits = x, true);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        if (!_showCredits)
            return;

        CloseCredits();
    }

    private void OnRoundEnd(RoundEndMessageEvent message)
    {
        if (!_showCredits)
            return;

        var shoutout = "John Nanotrasen";
        var patrons = _linkAccount.GetPatrons();
        if (patrons.Count != 0)
            shoutout = _random.Pick(patrons).Name;

        var credits = new EndRoundCreditsControl();
        // <Whiskey> - a escala tem que ser a resolvida, não o valor cru do CVar.
        // O display.uiScale nasce em 0, que para o engine quer dizer "herdar a
        // escala do sistema" e não "divisor zero". Quem nunca mexeu nessa opção
        // ficava com a tela de créditos em tamanho infinito, ou seja invisível.
        // O RootControl.UIScale é exatamente o que o engine usa para arranjar a
        // raiz da janela, então a conta bate com a do resto da interface.
        var uiScale = _ui.RootControl.UIScale;
        credits.SetSize = _clyde.MainWindow.Size / (uiScale > 0f ? uiScale : 1f);
        // </Whiskey>
        credits.Populate(message, _cache, ProtoMan, shoutout, Debug);

        var rand = new RobustRandom();
        rand.SetSeed(message.RoundId);

        if (rand.Prob(0.01f)) // Kojima is god...?
            credits.AddKojimaBox(_cache);

        _creditsContainer = credits;

        _ui.WindowRoot.AddChild(credits);
        _ui.WindowRoot.AddChild(AddExitCreditsButton());
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_creditsContainer is null)
            return;

        base.FrameUpdate(frameTime);

        var clampedTime = Math.Min(frameTime, 0.1f);
        _timer += clampedTime;

        var scroll = _creditsContainer.GetScrollValue();
        var scrollSpeed = GetScrollingSpeed(TimeSpan.FromSeconds(_timer));
        _creditsContainer.SetScrollValue(scroll + new Vector2(0f, scrollSpeed * clampedTime));
    }

    public float GetScrollingSpeed(TimeSpan time)
    {
        var normalSpeed = 200f;
        var speedUpDuration = 10f;
        var easing = Easings.InSine;
        return easing(Math.Min((float)time.TotalSeconds / speedUpDuration, 1f)) * normalSpeed;
    }

    private void CloseCredits()
    {
        if (_creditsContainer != null)
            _ui.WindowRoot.RemoveChild(_creditsContainer);

        if (_exitContainer != null)
            _ui.WindowRoot.RemoveChild(_exitContainer);

        _creditsContainer = null;
        _exitContainer = null;
    }

    private BoxContainer AddExitCreditsButton()
    {
        var buttonBox = new BoxContainer
        {
            HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Top,
        };

        var button = new Button
        {
            Text = Loc.GetString("round-end-credits-trauma-close"),
            HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Top,
        };
        button.OnPressed += _ => CloseCredits();

        buttonBox.AddChild(button);
        _exitContainer = buttonBox;

        return buttonBox;
    }
}
