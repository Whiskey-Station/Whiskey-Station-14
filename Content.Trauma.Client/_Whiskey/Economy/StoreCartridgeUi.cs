// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Trauma.Shared._Whiskey.Economy.Cartridge;
using Robust.Client.UserInterface;

namespace Content.Trauma.Client._Whiskey.Economy;

public sealed partial class StoreCartridgeUi : UIFragment
{
    private StoreCartridgeUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface ui, EntityUid? fragmentOwner)
    {
        _fragment = new StoreCartridgeUiFragment();

        _fragment.OnComprar += (indice, destinatario) =>
            ui.SendMessage(new CartridgeUiMessage(new StoreCartridgeBuyMessage(indice, destinatario)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is StoreCartridgeUiState loja)
            _fragment?.UpdateState(loja);
    }
}
