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
/// Faz o crachá funcionar como carteira: encostar cédula nele deposita, e um
/// verbo saca de volta em dinheiro vivo.
///
/// É isto que torna a economia social sem uma única janela nova. Com saldo que
/// vira cédula e cédula que vira saldo, pagar outra pessoa já é entregar o
/// dinheiro na mão dela, roubar já é tomar o maço, e o barman já pode cobrar
/// pela bebida. Tudo isso o jogo faz desde sempre, e nenhuma dessas coisas
/// precisou de código novo.
///
/// Fica no servidor porque dinheiro não se prevê no cliente.
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
    /// A moeda da estação. Nota falsa carrega <c>SpesosFake</c> e por isso não
    /// entra na conta: o banco não é trouxa, e a falsificação continua servindo
    /// para enganar gente, que é a graça dela.
    /// </summary>
    private static readonly ProtoId<CurrencyPrototype> Moeda = "Spesos";

    /// <summary>
    /// Valores de saque rápido. Existe também um saque do saldo inteiro, que
    /// só aparece quando ele não coincide com nenhum destes.
    /// </summary>
    private static readonly int[] SaquesRapidos = [100, 500, 1000];

    /// <summary>
    /// Os saques ficam num submenu próprio, e não soltos no menu.
    ///
    /// Isto NÃO é enfeite. Verbo sem categoria ordena antes de verbo com
    /// categoria, está escrito no CompareTo do Verb: "uncategorized verbs
    /// always appear first". Como o alt mais clique executa o primeiro verbo
    /// alternativo da lista, os saques soltos roubaram o atalho de tirar o
    /// cartão do PDA, e quem apertava alt para pegar o ID sacava dinheiro.
    /// </summary>
    private static readonly VerbCategory Saque =
        new("credit-account-withdraw-category", "/Textures/Interface/VerbIcons/eject.svg.192dpi.png");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditAccountComponent, InteractUsingEvent>(OnDepositar);
        SubscribeLocalEvent<CreditAccountComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbos);

        // O cartão quase sempre está DENTRO do PDA, e um clique no PDA não
        // chega no cartão. Sem isto, guardar dinheiro exigia ejetar o cartão,
        // depositar e guardar de volta, três passos para uma coisa que é uma.
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
                // Prioridade negativa por cima da categoria: mesmo que alguém
                // mude a categoria um dia, o saque nunca volta a ser o primeiro
                // da lista e o alt mais clique continua sendo do cartão.
                Priority = -1,
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

    /// <summary>
    /// Tira o valor da conta e põe a cédula na mão de quem sacou.
    /// </summary>
    public bool TrySacar(Entity<CreditAccountComponent> conta, EntityUid usuario, int valor)
    {
        if (!_proto.TryIndex(Moeda, out var moeda) || moeda.Cash == null || !moeda.CanWithdraw)
            return false;

        if (!_contas.TryWithdraw(conta.Owner, valor))
            return false;

        // Da nota mais alta para a mais baixa, igual ao saque da loja. Hoje só
        // existe a de 1, mas quem criar a de 100 amanhã não precisa voltar aqui.
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

    /// <summary>
    /// Quanto vale, em spesos, o que a pessoa encostou no crachá.
    /// </summary>
    private int Valor(EntityUid dinheiro)
    {
        if (!TryComp<CurrencyComponent>(dinheiro, out var moeda) ||
            !moeda.Price.TryGetValue(Moeda, out var porUnidade))
            return 0;

        // O valor da moeda é POR UNIDADE da pilha, e não da pilha inteira.
        var quantidade = TryComp<StackComponent>(dinheiro, out var pilha) ? pilha.Count : 1;
        return (porUnidade * quantidade).Int();
    }
}
