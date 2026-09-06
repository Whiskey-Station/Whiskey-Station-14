// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Stack;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Verbs;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CreditAccountComponent, InteractUsingEvent>(OnDepositar);
        SubscribeLocalEvent<CreditAccountComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbos);
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
                Act = () => TrySacar(ent, usuario, pedido),
            });
        }

        if (SaquesRapidos.Contains(saldo))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("credit-account-withdraw-all", ("valor", saldo)),
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
