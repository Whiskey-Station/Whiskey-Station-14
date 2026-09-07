// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Economy;
using Content.Shared.Access.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Trauma.Shared._Whiskey.Economy.Cartridge;
using Robust.Shared.Prototypes;

namespace Content.Trauma.Server._Whiskey.Economy;

/// <summary>
/// A loja do PDA. Mostra o saldo do cartão que está dentro do aparelho e vende
/// para ele.
///
/// Roda só no servidor porque cobrar é escrita de saldo, e porque a lista de
/// preço não pode ser decidida pelo cliente: a tela manda o índice da linha, e
/// quem lê o preço daquele índice é aqui.
/// </summary>
public sealed partial class StoreCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartucho = default!;
    [Dependency] private CreditAccountSystem _contas = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedHandsSystem _maos = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StoreCartridgeComponent, CartridgeUiReadyEvent>(OnAbriu);
        SubscribeLocalEvent<StoreCartridgeComponent, CartridgeMessageEvent>(OnMensagem);
    }

    private void OnAbriu(Entity<StoreCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        Atualizar(ent, args.Loader);
    }

    private void OnMensagem(Entity<StoreCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not StoreCartridgeBuyMessage compra)
            return;

        Comprar(ent, GetEntity(args.LoaderUid), args.Actor, compra.Index);
    }

    /// <summary>
    /// Compra a linha pedida, tirando o dinheiro do cartão do PDA.
    /// </summary>
    public bool Comprar(Entity<StoreCartridgeComponent> ent, EntityUid pda, EntityUid comprador, int indice)
    {
        if (!_proto.TryIndex(ent.Comp.Pack, out var lista) || indice < 0 || indice >= lista.Listings.Count)
            return false;

        var linha = lista.Listings[indice];

        // Quem paga é o cartão que está DENTRO do PDA. Sem cartão, não vende,
        // e diz por quê.
        if (!_idCard.TryGetIdCard(pda, out var cartao))
        {
            _popup.PopupEntity(Loc.GetString("store-cartridge-no-card"), pda, comprador);
            return false;
        }

        if (linha.Cost > int.MaxValue || !_contas.TryWithdraw(cartao.Owner, (int) linha.Cost, out var restante))
        {
            _popup.PopupEntity(Loc.GetString("store-cartridge-no-funds"), pda, comprador);
            return false;
        }

        // A entrega é na mão de quem comprou, por enquanto. O pod de entrega
        // vem em PR própria, e é ele que devolve o risco: entrega instantânea
        // e segura é o que faz economia virar menu.
        var item = Spawn(linha.Id, _transform.GetMapCoordinates(pda));
        _maos.PickupOrDrop(comprador, item);

        _popup.PopupEntity(Loc.GetString("store-cartridge-bought", ("saldo", restante)), pda, comprador);
        Atualizar(ent, pda);
        return true;
    }

    private void Atualizar(Entity<StoreCartridgeComponent> ent, EntityUid pda)
    {
        var linhas = new List<StoreCartridgeEntry>();
        var temCartao = _idCard.TryGetIdCard(pda, out var cartao);

        if (_proto.TryIndex(ent.Comp.Pack, out var lista))
        {
            for (var i = 0; i < lista.Listings.Count; i++)
            {
                var linha = lista.Listings[i];
                var nome = linha.OverrideName is { } loc
                    ? Loc.GetString(loc)
                    : _proto.Index(linha.Id).Name;

                linhas.Add(new StoreCartridgeEntry(i, nome, linha.Cost));
            }
        }

        var saldo = temCartao ? _contas.GetBalance(cartao.Owner) : 0;
        _cartucho.UpdateCartridgeUiState(pda, new StoreCartridgeUiState(saldo, linhas, temCartao));
    }
}
