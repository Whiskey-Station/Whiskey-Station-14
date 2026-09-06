// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava a comissão de venda, que é a única coisa que põe dinheiro novo na
/// estação. Se ela ceder para os dois lados o estrago é grande: sem teto vira
/// impressora, e sem fatia a economia seca no meio da rodada.
/// </summary>
[TestFixture]
public sealed class SalesCommissionTest : GameTest
{
    private const string Cracha = "PassengerIDCard";

    /// <summary>
    /// A comissão é a fatia da venda, e para de crescer no teto.
    /// </summary>
    [Test]
    public async Task AFatiaEOTetoValem()
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        SalesCommissionComponent regra = default!;
        await server.WaitPost(() =>
        {
            var estacao = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            regra = server.EntMan.AddComponent<SalesCommissionComponent>(estacao);
        });

        var comissoes = server.System<SalesCommissionSystem>();

        Assert.Multiple(() =>
        {
            Assert.That(comissoes.Comissao(regra, 1000), Is.EqualTo((int) (1000 * regra.Cut)));
            Assert.That(comissoes.Comissao(regra, 100000), Is.EqualTo(regra.MaxPerSale), "a comissão passou do teto");
            Assert.That(comissoes.Comissao(regra, 1), Is.Zero, "venda de um speso pagou comissão");
        });
    }

    /// <summary>
    /// Vender põe a comissão no crachá de quem apertou o botão.
    /// </summary>
    [Test]
    public async Task AVendaPagaOCrachaDeQuemVendeu()
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid estacao = default;
        EntityUid pessoa = default;
        EntityUid cracha = default;
        EntityUid console = default;
        SalesCommissionComponent regra = default!;

        await server.WaitPost(() =>
        {
            estacao = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            regra = server.EntMan.AddComponent<SalesCommissionComponent>(estacao);

            console = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cracha = server.EntMan.SpawnAtPosition(Cracha, mapa.GridCoords);
            server.System<SharedHandsSystem>().TryPickupAnyHand(pessoa, cracha);
        });

        var ev = new CargoPalletSoldEvent(estacao, pessoa, console, 2000);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev));

        Assert.That(server.System<CreditAccountSystem>().GetBalance(cracha),
            Is.EqualTo((int) (2000 * regra.Cut)));
    }

    /// <summary>
    /// Quem vende sem crachá não recebe, e a venda continua acontecendo. Não é
    /// para a estação travar por causa de comissão.
    /// </summary>
    [Test]
    public async Task SemCrachaNinguemRecebeENadaQuebra()
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid estacao = default;
        EntityUid pessoa = default;
        EntityUid console = default;

        await server.WaitPost(() =>
        {
            estacao = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            server.EntMan.AddComponent<SalesCommissionComponent>(estacao);
            console = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
        });

        var ev = new CargoPalletSoldEvent(estacao, pessoa, console, 2000);

        Assert.DoesNotThrowAsync(async () =>
            await server.WaitPost(() => server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev)));
    }
}
