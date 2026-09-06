// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.NanoXp;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Whiskey.NanoXp;

[UsedImplicitly]
public sealed class NanoXpBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private NanoXpWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<NanoXpWindow>();
        _window.OnRefreshRequested += () => SendMessage(new NanoXpRefreshMessage());
        _window.OnEnrollRequested += password => SendMessage(new NanoXpEnrollMessage(password));
        _window.OnLoginRequested += (address, password) => SendMessage(new NanoXpLoginMessage(address, password));
        _window.OnLogoutRequested += () => SendMessage(new NanoXpLogoutMessage());
        _window.OnMailRequested += (recipient, subject, body) =>
            SendMessage(new NanoXpSendMailMessage(recipient, subject, body));
        _window.OnDwaineRequested += () => SendMessage(new NanoXpLaunchDwaineMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is NanoXpStateMessage state)
            _window?.UpdateState(state.State);
    }
}
