// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Content.Trauma.Server._Whiskey.Economy;
using Content.Trauma.Shared._Whiskey.Economy.Cartridge;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava a loja do PDA.
///
/// A regra que estes testes prendem: **quem paga é o cartão que está dentro do
/// PDA**, e não um cartão qualquer que a pessoa tenha por perto. É essa regra
/// que faz o app não ter a confusão que a máquina tinha.
/// </summary>
[TestFixture]
public sealed class StoreCartridgeTest : GameTest
{
    private const string Pda = "PassengerPDA";
    private const string Cartucho = "LojaCartridge";

    // Primeira linha da lista da Loja Geral: a rosquinha, que custa 20.
    private const int Rosquinha = 0;
    private const int PrecoDaRosquinha = 20;

    private async Task<(Entity<StoreCartridgeComponent> App, EntityUid Pda, EntityUid Pessoa, EntityUid? Cartao)> Montar(
        int saldo, bool comCartao = true)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid app = default;
        EntityUid pda = default;
        EntityUid pessoa = default;
        EntityUid? cartao = null;

        await server.WaitPost(() =>
        {
            pda = server.EntMan.SpawnAtPosition(Pda, mapa.GridCoords);
            app = server.EntMan.SpawnAtPosition(Cartucho, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);

            if (server.System<SharedIdCardSystem>().TryGetIdCard(pda, out var achado))
            {
                cartao = achado.Owner;
                server.System<CreditAccountSystem>().TryDeposit(achado.Owner, saldo);
            }

            if (!comCartao)
                server.System<ItemSlotsSystem>().TryEject(pda, PdaComponent.PdaIdSlotId, null, out _);
        });
        await Pair.RunTicksSync(2);

        var comp = server.EntMan.GetComponent<StoreCartridgeComponent>(app);
        return ((app, comp), pda, pessoa, cartao);
    }

    /// <summary>
    /// Comprar tira o preço do cartão que está dentro do PDA.
    /// </summary>
    [Test]
    public async Task ComprarTiraDoCartaoDoPda()
    {
        var server = Server;
        var (app, pda, pessoa, cartao) = await Montar(500);

        var comprou = false;
        await server.WaitPost(() =>
            comprou = server.System<StoreCartridgeSystem>().Comprar(app, pda, pessoa, Rosquinha));

        Assert.Multiple(() =>
        {
            Assert.That(comprou, Is.True, "o app recusou uma compra que cabia no saldo");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao!.Value),
                Is.EqualTo(500 - PrecoDaRosquinha));
        });
    }

    /// <summary>
    /// PDA sem cartão dentro não compra nada, nem tira dinheiro de lugar
    /// nenhum.
    /// </summary>
    [Test]
    public async Task PdaSemCartaoNaoCompra()
    {
        var server = Server;
        var (app, pda, pessoa, cartao) = await Montar(500, comCartao: false);

        var comprou = true;
        await server.WaitPost(() =>
            comprou = server.System<StoreCartridgeSystem>().Comprar(app, pda, pessoa, Rosquinha));

        Assert.Multiple(() =>
        {
            Assert.That(comprou, Is.False, "vendeu sem cartão no PDA");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao!.Value), Is.EqualTo(500),
                "tirou dinheiro do cartão que estava fora do PDA");
        });
    }

    /// <summary>
    /// Sem saldo não compra, e a conta não fica devendo.
    /// </summary>
    [Test]
    public async Task SemSaldoNaoCompra()
    {
        var server = Server;
        var (app, pda, pessoa, cartao) = await Montar(5);

        var comprou = true;
        await server.WaitPost(() =>
            comprou = server.System<StoreCartridgeSystem>().Comprar(app, pda, pessoa, Rosquinha));

        Assert.Multiple(() =>
        {
            Assert.That(comprou, Is.False, "vendeu fiado");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao!.Value), Is.EqualTo(5));
        });
    }

    /// <summary>
    /// Índice fora da lista não compra nem estoura. A tela manda o índice, e
    /// tela é coisa do cliente.
    /// </summary>
    [Test]
    public async Task IndiceInvalidoNaoCompra()
    {
        var server = Server;
        var (app, pda, pessoa, cartao) = await Montar(500);

        var comprou = true;
        await server.WaitPost(() =>
            comprou = server.System<StoreCartridgeSystem>().Comprar(app, pda, pessoa, 9999));

        Assert.Multiple(() =>
        {
            Assert.That(comprou, Is.False);
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao!.Value), Is.EqualTo(500));
        });
    }
}
