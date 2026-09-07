// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Stack;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// O cartão como carteira: encostar cédula deposita, um verbo saca de volta.
///
/// Com saldo virando cédula e cédula virando saldo, pagar outra pessoa é
/// entregar o dinheiro na mão dela, e isso o jogo já sabe fazer.
/// </summary>
public sealed partial class CreditWalletSystem : EntitySystem
{
    [Dependency] private CreditAccountSystem _contas = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;

    /// <summary>
    /// A moeda da estação. Nota falsa carrega <c>SpesosFake</c> e não entra.
    /// </summary>
    private static readonly ProtoId<CurrencyPrototype> Moeda = "Spesos";

    /// <summary>
    /// Saque rápido. O saque do saldo inteiro só aparece quando não coincide
    /// com nenhum destes.
    /// </summary>
    private static readonly int[] SaquesRapidos = [100, 500, 1000];

    /// <summary>
    /// Categoria obrigatória, não enfeite: verbo sem categoria ordena antes de
    /// verbo com categoria, e o alt mais clique executa o primeiro da lista.
    /// Sem isto o saque rouba o atalho de tirar o cartão do PDA.
    /// </summary>
    private static readonly VerbCategory Saque =
        new("credit-account-withdraw-category", "/Textures/Interface/VerbIcons/eject.svg.192dpi.png");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditAccountComponent, InteractUsingEvent>(OnDepositar);
        SubscribeLocalEvent<CreditAccountComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbos);

        // Clique no PDA não chega no cartão de dentro dele.
        SubscribeLocalEvent<PdaComponent, InteractUsingEvent>(OnDepositarNoPda);
        SubscribeLocalEvent<PdaComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbosDoPda);
    }

    private void OnDepositar(Entity<CreditAccountComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var valor = Valor(args.Used);
        if (valor <= 0)
            return;

        args.Handled = true;

        if (!_contas.TryDeposit(ent.Owner, valor))
        {
            _popup.PopupEntity(Loc.GetString("credit-account-deposit-failed"), ent, args.User);
            return;
        }

        // Só some com o dinheiro depois que ele entrou na conta.
        QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("credit-account-deposit", ("valor", valor)), ent, args.User);
    }

    private void OnDepositarNoPda(Entity<PdaComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_idCard.TryGetIdCard(ent.Owner, out var cartao))
            return;

        if (!TryComp<CreditAccountComponent>(cartao.Owner, out var conta))
            return;

        var evento = new InteractUsingEvent(args.User, args.Used, cartao.Owner, args.ClickLocation);
        OnDepositar((cartao.Owner, conta), ref evento);
        args.Handled = evento.Handled;
    }

    private void OnVerbosDoPda(Entity<PdaComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!_idCard.TryGetIdCard(ent.Owner, out var cartao) ||
            !TryComp<CreditAccountComponent>(cartao.Owner, out var conta))
            return;

        OnVerbos((cartao.Owner, conta), ref args);
    }

    private void OnVerbos(Entity<CreditAccountComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var saldo = ent.Comp.Balance;
        if (saldo <= 0)
            return;

        var usuario = args.User;

        foreach (var valor in SaquesRapidos)
        {
            if (saldo < valor)
                continue;

            var pedido = valor;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("credit-account-withdraw-verb", ("valor", pedido)),
                Category = Saque,
                Priority = -1, // cinto e suspensório, junto com a categoria

                Act = () => TrySacar(ent, usuario, pedido),
            });
        }

        if (SaquesRapidos.Contains(saldo))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("credit-account-withdraw-all", ("valor", saldo)),
            Category = Saque,
            Priority = -1,
            Act = () => TrySacar(ent, usuario, saldo),
        });
    }

    public bool TrySacar(Entity<CreditAccountComponent> conta, EntityUid usuario, int valor)
    {
        if (!_proto.TryIndex(Moeda, out var moeda) || moeda.Cash == null || !moeda.CanWithdraw)
            return false;

        if (!_contas.TryWithdraw(conta.Owner, valor))
            return false;

        // Da nota mais alta para a mais baixa, igual ao saque da loja.
        var restante = valor;
        var coordenadas = Transform(usuario).Coordinates;

        foreach (var nota in moeda.Cash.Keys.OrderByDescending(x => x))
        {
            var quantidade = (int) (restante / nota);
            if (quantidade <= 0)
                continue;

            foreach (var pilha in _stack.SpawnMultipleAtPosition(moeda.Cash[nota], quantidade, coordenadas))
                _hands.PickupOrDrop(usuario, pilha);

            restante -= (int) (nota * quantidade);
        }

        _popup.PopupEntity(Loc.GetString("credit-account-withdraw", ("valor", valor)), conta, usuario);
        return true;
    }

    private int Valor(EntityUid dinheiro)
    {
        if (!TryComp<CurrencyComponent>(dinheiro, out var moeda) ||
            !moeda.Price.TryGetValue(Moeda, out var porUnidade))
            return 0;

        // O valor da moeda é por unidade da pilha, não da pilha inteira.
        var quantidade = TryComp<StackComponent>(dinheiro, out var pilha) ? pilha.Count : 1;
        return (porUnidade * quantidade).Int();
    }
}
