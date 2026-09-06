// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Hands.EntitySystems;
using Content.Trauma.Shared.VendingMachines;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava a ponte entre a máquina de loja do Trauma e a conta do crachá. A
/// máquina pergunta o saldo e manda cobrar por evento, então é o evento que
/// precisa ser testado, e não a interface.
/// </summary>
[TestFixture]
public sealed class CreditVendorTest : GameTest
{
    private const string Maquina = "VendingMachineLojaGeral";
    private const string Cracha = "PassengerIDCard";

    private async Task<(EntityUid Maquina, EntityUid Pessoa, EntityUid Cracha)> Montar(int saldo)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid maquina = default;
        EntityUid pessoa = default;
        EntityUid cracha = default;

        await server.WaitPost(() =>
        {
            maquina = server.EntMan.SpawnAtPosition(Maquina, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cracha = server.EntMan.SpawnAtPosition(Cracha, mapa.GridCoords);

            // Na mão de propósito: para comprar basta estar com o cartão, e
            // isso é diferente do salário, que exige o crachá vestido.
            server.System<SharedHandsSystem>().TryPickupAnyHand(pessoa, cracha);
            server.System<CreditAccountSystem>().TryDeposit(cracha, saldo);
        });

        return (maquina, pessoa, cracha);
    }

    /// <summary>
    /// A máquina responde com o saldo do crachá de quem está na frente dela.
    /// </summary>
    [Test]
    public async Task AMaquinaEnxergaOSaldoDoCracha()
    {
        var server = Server;
        var (maquina, pessoa, _) = await Montar(500);

        var ev = new ShopVendorBalanceEvent(pessoa);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.That(ev.Balance, Is.EqualTo(500u));
    }

    /// <summary>
    /// Comprar tira exatamente o preço da conta.
    /// </summary>
    [Test]
    public async Task ComprarTiraOPrecoDaConta()
    {
        var server = Server;
        var (maquina, pessoa, cracha) = await Montar(500);

        var ev = new ShopVendorPurchaseEvent(pessoa, 150);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.Multiple(() =>
        {
            Assert.That(ev.Paid, Is.True, "a máquina não aceitou o pagamento");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cracha), Is.EqualTo(350));
        });
    }

    /// <summary>
    /// Sem saldo não compra, e a conta não fica devendo.
    /// </summary>
    [Test]
    public async Task SemSaldoNaoCompra()
    {
        var server = Server;
        var (maquina, pessoa, cracha) = await Montar(100);

        var ev = new ShopVendorPurchaseEvent(pessoa, 5000);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.Multiple(() =>
        {
            Assert.That(ev.Paid, Is.False, "vendeu fiado");
            Assert.That(server.System<CreditAccountSystem>().GetBalance(cracha), Is.EqualTo(100));
        });
    }

    /// <summary>
    /// Quem não tem crachá nenhum aparece com saldo zero, e não com o inteiro
    /// dando a volta na interface da máquina.
    /// </summary>
    [Test]
    public async Task SemCrachaOSaldoEZero()
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid maquina = default;
        EntityUid pessoa = default;

        await server.WaitPost(() =>
        {
            maquina = server.EntMan.SpawnAtPosition(Maquina, mapa.GridCoords);
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
        });

        var ev = new ShopVendorBalanceEvent(pessoa);
        await server.WaitPost(() => server.EntMan.EventBus.RaiseLocalEvent(maquina, ref ev));

        Assert.That(ev.Balance, Is.Zero);
    }
}
