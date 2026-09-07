// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Economy;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Delivery;
using Content.Shared.FingerprintReader;
using Content.Shared.Forensics.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Containers;
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
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private FingerprintReaderSystem _leitorDigital = default!;
    [Dependency] private LabelSystem _label = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationRecordsSystem _registros = default!;

    /// <summary>
    /// A caixa em que a compra chega.
    /// </summary>
    private static readonly EntProtoId Pacote = "LojaPacote";

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

        Comprar(ent, GetEntity(args.LoaderUid), args.Actor, compra.Index, compra.Recipient);
    }

    /// <summary>
    /// Compra a linha pedida, tirando o dinheiro do cartão do PDA.
    /// </summary>
    public bool Comprar(Entity<StoreCartridgeComponent> ent, EntityUid pda, EntityUid comprador, int indice, uint? destinatario = null)
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

        Entregar(linha.Id, pda, comprador, cartao, destinatario);

        _popup.PopupEntity(Loc.GetString("store-cartridge-bought", ("saldo", restante)), pda, comprador);
        Atualizar(ent, pda);
        return true;
    }

    /// <summary>
    /// Manda o que foi comprado dentro de uma encomenda embalada e trancada,
    /// endereçada a quem comprou.
    ///
    /// Entregar o item direto na mão não custava nada a ninguém, e economia
    /// sem risco vira menu. Assim a compra vira um objeto no mundo: dá para
    /// tomar a caixa de alguém, e quem tomou não consegue abrir, porque a
    /// trava é a digital do destinatário.
    /// </summary>
    private void Entregar(EntProtoId item,
        EntityUid pda,
        EntityUid comprador,
        Entity<IdCardComponent> cartao,
        uint? destinatario)
    {
        var pacote = Spawn(Pacote, _transform.GetMapCoordinates(pda));

        if (TryComp<DeliveryComponent>(pacote, out var entrega))
        {
            var estacao = _station.GetOwningStation(comprador);
            string? digital;

            // O correio sorteia um destinatário no arranque. Aqui quem manda é
            // a escolha de quem comprou: sem escolha, a encomenda é para o dono
            // do cartão que pagou.
            if (destinatario is { } fichaId &&
                estacao is { } daEstacao &&
                _registros.TryGetRecord<GeneralStationRecord>(new StationRecordKey(fichaId, daEstacao), out var ficha))
            {
                entrega.RecipientName = ficha.Name;
                entrega.RecipientJobTitle = ficha.JobTitle;
                digital = ficha.Fingerprint;
            }
            else
            {
                entrega.RecipientName = cartao.Comp.FullName ?? Name(comprador);
                entrega.RecipientJobTitle = cartao.Comp.LocalizedJobTitle;
                digital = CompOrNull<FingerprintComponent>(comprador)?.Fingerprint;
            }

            entrega.RecipientStation = estacao;
            Dirty(pacote, entrega);

            _label.Label(pacote, entrega.RecipientName);
            var conteudo = _container.EnsureContainer<Container>(pacote, entrega.Container);
            _container.Insert(Spawn(item), conteudo);

            // A digital é a tranca. Presente para outra pessoa nasce trancado
            // na digital DELA, então nem quem pagou consegue abrir: é o que
            // torna presente diferente de comprar e entregar na mão.
            if (TryComp<FingerprintReaderComponent>(pacote, out var leitor) && digital is { } marca)
                _leitorDigital.AddAllowedFingerprint((pacote, leitor), marca);
        }

        _maos.PickupOrDrop(comprador, pacote);
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

                linhas.Add(new StoreCartridgeEntry(i, nome, linha.Cost, linha.Id));
            }
        }

        var saldo = temCartao ? _contas.GetBalance(cartao.Owner) : 0;
        _cartucho.UpdateCartridgeUiState(pda, new StoreCartridgeUiState(saldo, linhas, Tripulacao(pda), temCartao));
    }

    /// <summary>
    /// Quem dá para presentear: a mesma lista de fichas que o correio usa para
    /// endereçar carta, então quem não tem ficha na estação não aparece.
    /// </summary>
    private List<StoreCartridgeRecipient> Tripulacao(EntityUid pda)
    {
        var gente = new List<StoreCartridgeRecipient>();

        if (_station.GetOwningStation(pda) is not { } estacao)
            return gente;

        foreach (var (id, ficha) in _registros.GetRecordsOfType<GeneralStationRecord>(estacao))
        {
            gente.Add(new StoreCartridgeRecipient(id, ficha.Name, ficha.JobTitle));
        }

        gente.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return gente;
    }
}
