// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Containers.ItemSlots;
using Content.Trauma.Shared.VendingMachines;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava a ponte entre a máquina de loja e a conta.
///
/// A regra que estes testes existem para prender: **quem paga é o cartão que
/// está dentro da máquina**. Antes disso ela lia o cartão mais perto da mão, e
/// quem tinha um cartão na mão e outro no PDA via dois saldos diferentes na
/// mesma máquina.
/// </summary>
[TestFixture]
public sealed class CreditVendorTest : GameTest
{
    private const string Maquina = "VendingMachineLojaGeral";
    private const string Cartao = "PassengerIDCard";
    private const string Slot = "card_slot";

    private async Task<(EntityUid Maquina, EntityUid Pessoa, EntityUid Cartao)> Montar(int saldo, bool inserir = true)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid maquina = default;
        EntityUid pessoa = default;
        EntityUid cartao = default;

        await server.WaitPost(() =>
        {
            maquina = server.EntMan.SpawnAtPosition(Maquina, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cartao = server.EntMan.SpawnAtPosition(Cartao, mapa.GridCoords);

            server.System<CreditAccountSystem>().TryDeposit(cartao, saldo);

            if (inserir)
                server.System<ItemSlotsSystem>().TryInsert(maquina, Slot, cartao, pessoa);
        });
        await Pair.RunTicksSync(2);

        return (maquina, pessoa, cartao);
    }

    /// <summary>
    /// Com o cartão dentro, a máquina mostra o saldo dele.
    /// </summary>
    [Test]
    public async Task ComOCartaoDentroAMaquinaMostraOSaldo()
    {
        var server = Server;
        var (maquina, pessoa, _) = await Montar(500);

        var ev = new ShopVendorBalanceEvent(pessoa);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.That(ev.Balance, Is.EqualTo(500u));
    }

    /// <summary>
    /// Sem cartão dentro, a máquina não mostra saldo nenhum, mesmo com a
    /// pessoa carregando um cartão cheio na mão.
    /// </summary>
    [Test]
    public async Task SemCartaoDentroNaoMostraSaldo()
    {
        var server = Server;
        var (maquina, pessoa, _) = await Montar(500, inserir: false);

        var ev = new ShopVendorBalanceEvent(pessoa);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.That(ev.Balance, Is.Zero, "a máquina leu um cartão que não estava dentro dela");
    }

    /// <summary>
    /// Comprar tira o preço do cartão inserido.
    /// </summary>
    [Test]
    public async Task ComprarTiraOPrecoDoCartaoInserido()
    {
        var server = Server;
        var (maquina, pessoa, cartao) = await Montar(500);

        var ev = new ShopVendorPurchaseEvent(pessoa, 150);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.Multiple(() =>
        {
            Assert.That(ev.Paid, Is.True, "a máquina não aceitou o pagamento");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao), Is.EqualTo(350));
        });
    }

    /// <summary>
    /// Sem cartão dentro não compra, mesmo com a pessoa tendo dinheiro no
    /// cartão que está com ela.
    /// </summary>
    [Test]
    public async Task SemCartaoDentroNaoCompra()
    {
        var server = Server;
        var (maquina, pessoa, cartao) = await Montar(500, inserir: false);

        var ev = new ShopVendorPurchaseEvent(pessoa, 150);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.Multiple(() =>
        {
            Assert.That(ev.Paid, Is.False, "vendeu sem cartão na máquina");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao), Is.EqualTo(500));
        });
    }

    /// <summary>
    /// Sem saldo não compra, e a conta não fica devendo.
    /// </summary>
    [Test]
    public async Task SemSaldoNaoCompra()
    {
        var server = Server;
        var (maquina, pessoa, cartao) = await Montar(100);

        var ev = new ShopVendorPurchaseEvent(pessoa, 5000);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.Multiple(() =>
        {
            Assert.That(ev.Paid, Is.False, "vendeu fiado");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cartao), Is.EqualTo(100));
        });
    }
}
