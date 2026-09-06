// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Server.Stack;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava a ponte entre saldo e dinheiro vivo. É ela que faz pagar outra pessoa
/// ser possível sem nenhuma janela nova: quem tem saldo vira cédula, e a
/// cédula o jogo já sabe entregar, guardar e roubar.
/// </summary>
[TestFixture]
public sealed class CreditWalletTest : GameTest
{
    private const string Cracha = "PassengerIDCard";

    private async Task<(EntityUid Pessoa, EntityUid Cracha, EntityCoordinates Onde)> Montar()
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid pessoa = default;
        EntityUid cracha = default;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cracha = server.EntMan.SpawnAtPosition(Cracha, mapa.GridCoords);
        });

        return (pessoa, cracha, mapa.GridCoords);
    }

    private int TotalDeCedulaNoMapa(string prototipo = "SpaceCash")
    {
        var server = Server;
        var total = 0;
        var query = server.EntMan.EntityQueryEnumerator<StackComponent>();
        while (query.MoveNext(out var uid, out var pilha))
        {
            if (server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == prototipo)
                total += pilha.Count;
        }

        return total;
    }

    /// <summary>
    /// Encostar um maço de spesos no crachá joga o valor na conta e some com o
    /// maço.
    /// </summary>
    [Test]
    public async Task EncostarCedulaNoCrachaDeposita()
    {
        var server = Server;
        var (pessoa, cracha, onde) = await Montar();

        EntityUid maco = default;
        await server.WaitPost(() =>
            maco = server.System<StackSystem>().SpawnMultipleAtPosition(new EntProtoId("SpaceCash"), 100, onde).First());

        await server.WaitPost(() =>
            server.EntMan.EventBus.RaiseLocalEvent(cracha, new InteractUsingEvent(pessoa, maco, cracha, onde)));
        await Pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cracha), Is.EqualTo(100));
            Assert.That(server.EntMan.Deleted(maco), Is.True, "a cédula depositada continuou existindo");
        });
    }

    /// <summary>
    /// Nota falsa não entra na conta. Ela continua servindo para enganar
    /// gente, que é a graça dela, mas não engana o banco.
    /// </summary>
    [Test]
    public async Task DinheiroFalsoNaoEntraNaConta()
    {
        var server = Server;
        var (pessoa, cracha, onde) = await Montar();

        EntityUid falso = default;
        await server.WaitPost(() =>
            falso = server.System<StackSystem>().SpawnMultipleAtPosition(new EntProtoId("SpaceCashCounterfeit"), 100, onde).First());

        await server.WaitPost(() =>
            server.EntMan.EventBus.RaiseLocalEvent(cracha, new InteractUsingEvent(pessoa, falso, cracha, onde)));
        await Pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cracha), Is.Zero, "a nota falsa entrou na conta");
            Assert.That(server.EntMan.Deleted(falso), Is.False, "a nota falsa sumiu sem pagar nada");
        });
    }

    /// <summary>
    /// Sacar tira da conta e devolve exatamente o mesmo valor em cédula.
    /// </summary>
    [Test]
    public async Task SacarVoltaAVirarCedula()
    {
        var server = Server;
        var (pessoa, cracha, _) = await Montar();

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(cracha, 300));

        var antes = TotalDeCedulaNoMapa();
        var sacou = false;

        await server.WaitPost(() =>
        {
            var conta = (cracha, server.EntMan.GetComponent<CreditAccountComponent>(cracha));
            sacou = server.System<CreditWalletSystem>().TrySacar(conta, pessoa, 100);
        });
        await Pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(sacou, Is.True);
            Assert.That(contas.GetBalance(cracha), Is.EqualTo(200));
            Assert.That(TotalDeCedulaNoMapa() - antes, Is.EqualTo(100), "o saque não virou a mesma quantia em cédula");
        });
    }

    /// <summary>
    /// Saque maior que o saldo é recusado sem gerar cédula nenhuma. Sem esta
    /// guarda o crachá vira impressora de dinheiro.
    /// </summary>
    [Test]
    public async Task SacarMaisQueOSaldoNaoImprimeDinheiro()
    {
        var server = Server;
        var (pessoa, cracha, _) = await Montar();

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(cracha, 50));

        var antes = TotalDeCedulaNoMapa();
        var sacou = true;

        await server.WaitPost(() =>
        {
            var conta = (cracha, server.EntMan.GetComponent<CreditAccountComponent>(cracha));
            sacou = server.System<CreditWalletSystem>().TrySacar(conta, pessoa, 5000);
        });
        await Pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(sacou, Is.False);
            Assert.That(contas.GetBalance(cracha), Is.EqualTo(50));
            Assert.That(TotalDeCedulaNoMapa(), Is.EqualTo(antes), "saque recusado ainda assim cuspiu cédula");
        });
    }
}
