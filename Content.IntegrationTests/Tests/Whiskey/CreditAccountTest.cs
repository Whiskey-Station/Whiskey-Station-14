// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava as regras de dinheiro da conta pessoal. São as que, se cederem,
/// aparecem como jogador com saldo negativo ou com dinheiro que ninguém pagou,
/// e aí não tem como saber quanto tempo já estava assim.
/// </summary>
[TestFixture]
public sealed class CreditAccountTest : GameTest
{
    private const string Cartao = "PassengerIDCard";

    /// <summary>
    /// Todo cartão de identificação nasce com conta, e ela nasce zerada.
    /// </summary>
    [Test]
    public async Task TodoCartaoNasceComContaZerada()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid cartao = default;

        await server.WaitPost(() => cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords));

        Assert.That(server.EntMan.HasComponent<CreditAccountComponent>(cartao), Is.True,
            "cartão de identificação sem conta");
        Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao), Is.Zero);
    }

    /// <summary>
    /// Depositar soma, sacar subtrai, e o saque maior que o saldo é recusado
    /// sem mexer em nada.
    /// </summary>
    [Test]
    public async Task DepositoESaqueMexemNoSaldo()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid cartao = default;

        await server.WaitPost(() => cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords));

        var contas = server.System<CreditAccountSystem>();

        Assert.Multiple(() =>
        {
            Assert.That(contas.TryDeposit(cartao, 500), Is.True);
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(500));

            Assert.That(contas.TryWithdraw(cartao, 200), Is.True);
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(300));

            Assert.That(contas.TryWithdraw(cartao, 400), Is.False, "sacou mais do que tinha");
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(300), "saque recusado mexeu no saldo");
        });
    }

    /// <summary>
    /// Valor zero ou negativo é recusado nos dois sentidos. Depósito negativo
    /// seria saque sem a checagem de saldo, e saque negativo seria depósito de
    /// graça.
    /// </summary>
    [Test]
    public async Task ValorNaoPositivoERecusado()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid cartao = default;

        await server.WaitPost(() => cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords));

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(cartao, 100));

        Assert.Multiple(() =>
        {
            Assert.That(contas.TryDeposit(cartao, 0), Is.False);
            Assert.That(contas.TryDeposit(cartao, -50), Is.False);
            Assert.That(contas.TryWithdraw(cartao, 0), Is.False);
            Assert.That(contas.TryWithdraw(cartao, -50), Is.False);
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(100), "valor não positivo mexeu no saldo");
        });
    }

    /// <summary>
    /// Transferência move exatamente o valor, e a soma das duas contas é a
    /// mesma antes e depois. Dinheiro não pode nascer nem sumir no caminho.
    /// </summary>
    [Test]
    public async Task TransferenciaConservaOTotal()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid origem = default;
        EntityUid destino = default;

        await server.WaitPost(() =>
        {
            origem = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords);
            destino = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords);
        });

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(origem, 1000));

        var totalAntes = contas.GetBalance(origem) + contas.GetBalance(destino);

        Assert.Multiple(() =>
        {
            Assert.That(contas.TryTransfer(origem, destino, 400), Is.True);
            Assert.That(contas.GetBalance(origem), Is.EqualTo(600));
            Assert.That(contas.GetBalance(destino), Is.EqualTo(400));
            Assert.That(contas.GetBalance(origem) + contas.GetBalance(destino), Is.EqualTo(totalAntes));

            Assert.That(contas.TryTransfer(origem, destino, 5000), Is.False, "transferiu sem ter saldo");
            Assert.That(contas.GetBalance(origem), Is.EqualTo(600), "transferência recusada mexeu na origem");
            Assert.That(contas.GetBalance(destino), Is.EqualTo(400), "transferência recusada mexeu no destino");
        });
    }

    /// <summary>
    /// Depósito que estouraria o inteiro é recusado. Sem esta guarda o saldo
    /// dá a volta e vira negativo, e o dono da conta mais rica da estação
    /// termina devendo.
    /// </summary>
    [Test]
    public async Task SaldoNaoDaAVolta()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid cartao = default;

        await server.WaitPost(() => cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords));

        var contas = server.System<CreditAccountSystem>();

        Assert.Multiple(() =>
        {
            Assert.That(contas.TryDeposit(cartao, int.MaxValue), Is.True);
            Assert.That(contas.TryDeposit(cartao, 1), Is.False, "aceitou depósito que estoura o inteiro");
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(int.MaxValue));
        });
    }

    /// <summary>
    /// O saque devolve quanto sobrou, para quem chamou não precisar consultar
    /// o saldo de novo logo depois de mexer nele.
    /// </summary>
    [Test]
    public async Task OSaqueDevolveOQueSobrou()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid cartao = default;

        await server.WaitPost(() => cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords));

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(cartao, 900));

        var sacou = false;
        var restante = -1;
        await server.WaitPost(() => sacou = contas.TryWithdraw(cartao, 250, out restante));

        Assert.Multiple(() =>
        {
            Assert.That(sacou, Is.True);
            Assert.That(restante, Is.EqualTo(650));
            Assert.That(contas.GetBalance(cartao), Is.EqualTo(restante), "o restante devolvido não é o saldo");
        });
    }

    /// <summary>
    /// A conta é achada pelo cartão que a pessoa está vestindo, e não só pelo
    /// cartão solto. É esse caminho que a máquina de venda e o pagamento vão
    /// usar, então ele precisa estar travado desde já.
    /// </summary>
    [Test]
    public async Task AContaEAchadaPeloCartaoVestido()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;
        EntityUid cartao = default;
        var vestiu = false;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords);
            vestiu = server.System<InventorySystem>().TryEquip(pessoa, cartao, "id", force: true);
        });

        var contas = server.System<CreditAccountSystem>();
        await server.WaitPost(() => contas.TryDeposit(cartao, 250));

        Assert.That(vestiu, Is.True, "não consegui vestir o cartão no teste");
        Assert.That(contas.TryGetAccount(pessoa, out var achada), Is.True, "não achou a conta pelo portador");
        Assert.That(achada.Owner, Is.EqualTo(cartao));
        Assert.That(contas.GetUserBalance(pessoa), Is.EqualTo(250));
    }
}
