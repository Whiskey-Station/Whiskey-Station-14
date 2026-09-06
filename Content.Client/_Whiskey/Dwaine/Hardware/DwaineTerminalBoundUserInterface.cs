// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Hardware;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Whiskey.Dwaine.Hardware;

[UsedImplicitly]
public sealed class DwaineTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DwaineTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DwaineTerminalWindow>();
        _window.OnPowerRequested += () => SendMessage(new DwaineTerminalTogglePowerMessage());
        _window.OnInputSubmitted += text => SendMessage(new DwaineTerminalInputMessage(text));
        _window.OnConnectRequested += target => SendMessage(new DwaineTerminalConnectMessage(target));
        _window.OnDisconnectRequested += () => SendMessage(new DwaineTerminalDisconnectMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DwaineTerminalBoundUserInterfaceState terminalState)
            _window?.UpdateState(terminalState);
    }
}
