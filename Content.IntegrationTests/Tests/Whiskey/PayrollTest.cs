// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Economy;
using Content.Server.Cargo.Systems;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava as regras da folha de pagamento. As duas que importam: o dinheiro sai
/// do orçamento do departamento em vez de nascer do nada, e crachá que não
/// está sendo usado não recebe.
/// </summary>
[TestFixture]
public sealed class PayrollTest : GameTest
{
    private const string Cracha = "PassengerIDCard";

    // O Passageiro é do departamento Civilian, que a folha paga pela conta de
    // Serviço. Serve de prova do caminho cargo -> departamento -> conta.
    private const string Cargo = "Passenger";
    private const string Conta = "Service";

    // O analisador proíbe literal no Index, e com razão: assim um id errado
    // reprova no build em vez de estourar em jogo.
    private static readonly EntProtoId Estacao = "StandardNanotrasenStation";

    private async Task<(EntityUid Estacao, PayrollComponent Folha, StationBankAccountComponent Banco, EntityUid Pessoa, EntityUid Cracha)> Montar(bool vestir = true)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid estacao = default;
        EntityUid pessoa = default;
        EntityUid cracha = default;

        await server.WaitPost(() =>
        {
            estacao = server.EntMan.SpawnAtPosition(null, mapa.GridCoords);
            server.EntMan.AddComponent<StationBankAccountComponent>(estacao);
            server.EntMan.AddComponent<PayrollComponent>(estacao);

            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cracha = server.EntMan.SpawnAtPosition(Cracha, mapa.GridCoords);
            server.EntMan.GetComponent<IdCardComponent>(cracha).JobPrototype = Cargo;

            if (vestir)
                server.System<InventorySystem>().TryEquip(pessoa, cracha, "id", force: true);
            else
                server.System<SharedHandsSystem>().TryPickupAnyHand(pessoa, cracha);
        });

        return (estacao,
            server.EntMan.GetComponent<PayrollComponent>(estacao),
            server.EntMan.GetComponent<StationBankAccountComponent>(estacao),
            pessoa,
            cracha);
    }

    /// <summary>
    /// O salário sai do orçamento do departamento e entra no crachá. A conta
    /// da estação perde exatamente o que a pessoa ganhou.
    /// </summary>
    [Test]
    public async Task OSalarioSaiDoOrcamentoEVaiParaOCracha()
    {
        var server = Server;
        var (estacao, folha, banco, pessoa, cracha) = await Montar();

        var contas = server.System<CreditAccountSystem>();
        var cargas = server.System<CargoSystem>();
        var antes = cargas.GetBalanceFromAccount((estacao, banco), Conta);

        var pago = 0;
        await server.WaitPost(() => pago = server.System<PayrollSystem>().Pagar((estacao, folha, banco), pessoa));

        Assert.Multiple(() =>
        {
            Assert.That(pago, Is.EqualTo(folha.DefaultSalary));
            Assert.That(contas.GetBalance(cracha), Is.EqualTo(folha.DefaultSalary));
            Assert.That(cargas.GetBalanceFromAccount((estacao, banco), Conta),
                Is.EqualTo(antes - folha.DefaultSalary), "o orçamento não pagou a conta");
        });
    }

    /// <summary>
    /// A tabela de salário da estação paga chefe mais que aprendiz.
    ///
    /// O teste não trava o valor, trava a ordem: se um dia alguém inverter a
    /// tabela sem querer, o aprendiz passa a ganhar mais que o Capitão e
    /// ninguém percebe olhando YAML.
    /// </summary>
    [Test]
    public async Task AEscadaDeSalarioSobeDoAprendizAoCapitao()
    {
        var server = Server;

        // Ler do PROTOTYPE da estação, e não de um componente criado por
        // código: a tabela mora no YAML, e componente feito na mão nasce com
        // ela vazia. A primeira versão deste teste caiu nessa e comparava 40
        // com 40.
        var estacao = server.ProtoMan.Index(Estacao);

        Assert.That(estacao.TryComp<PayrollComponent>(out var folha, server.EntMan.ComponentFactory),
            Is.True, "a estação não tem folha de pagamento no prototype");

        int Salario(string cargo) => folha!.Salaries.GetValueOrDefault(cargo, folha.DefaultSalary);

        Assert.Multiple(() =>
        {
            Assert.That(Salario("Passenger"), Is.LessThan(Salario("CargoTechnician")),
                "passageiro não pode ganhar o mesmo que quem tem função");
            Assert.That(Salario("CargoTechnician"), Is.LessThan(Salario("Quartermaster")),
                "chefe de setor tem que ganhar mais que a equipe dele");
            Assert.That(Salario("Quartermaster"), Is.LessThan(Salario("Captain")),
                "o Capitão tem que ganhar mais que chefe de setor");
        });
    }

    /// <summary>
    /// Crachá na mão não recebe. É esta regra que impede a fábrica de
    /// identidades: imprimir vinte cartões não paga vinte salários.
    /// </summary>
    [Test]
    public async Task CrachaNaMaoNaoRecebe()
    {
        var server = Server;
        var (estacao, folha, banco, pessoa, cracha) = await Montar(vestir: false);

        var contas = server.System<CreditAccountSystem>();
        var cargas = server.System<CargoSystem>();
        var antes = cargas.GetBalanceFromAccount((estacao, banco), Conta);

        var pago = 0;
        await server.WaitPost(() => pago = server.System<PayrollSystem>().Pagar((estacao, folha, banco), pessoa));

        Assert.Multiple(() =>
        {
            Assert.That(pago, Is.Zero, "pagou quem não estava usando o crachá");
            Assert.That(contas.GetBalance(cracha), Is.Zero);
            Assert.That(cargas.GetBalanceFromAccount((estacao, banco), Conta), Is.EqualTo(antes));
        });
    }

    /// <summary>
    /// Departamento sem dinheiro não paga, e não fica devendo. Conta de
    /// departamento negativa travaria pedido de carga sem ninguém entender por
    /// quê.
    /// </summary>
    [Test]
    public async Task DepartamentoSemDinheiroNaoPaga()
    {
        var server = Server;
        var (estacao, folha, banco, pessoa, cracha) = await Montar();

        var contas = server.System<CreditAccountSystem>();
        var cargas = server.System<CargoSystem>();

        await server.WaitPost(() =>
            cargas.UpdateBankAccount((estacao, banco), -cargas.GetBalanceFromAccount((estacao, banco), Conta), Conta));

        var pago = 0;
        await server.WaitPost(() => pago = server.System<PayrollSystem>().Pagar((estacao, folha, banco), pessoa));

        Assert.Multiple(() =>
        {
            Assert.That(pago, Is.Zero, "pagou com a conta vazia");
            Assert.That(contas.GetBalance(cracha), Is.Zero);
            Assert.That(cargas.GetBalanceFromAccount((estacao, banco), Conta), Is.Zero, "a conta ficou devendo");
        });
    }

    /// <summary>
    /// Valor escrito para o cargo vence o valor padrão.
    /// </summary>
    [Test]
    public async Task SalarioDoCargoVenceOPadrao()
    {
        var server = Server;
        var (estacao, folha, banco, pessoa, cracha) = await Montar();

        var contas = server.System<CreditAccountSystem>();

        var pago = 0;
        await server.WaitPost(() =>
        {
            folha.Salaries[Cargo] = 250;
            pago = server.System<PayrollSystem>().Pagar((estacao, folha, banco), pessoa);
        });

        Assert.That(pago, Is.EqualTo(250));
        Assert.That(contas.GetBalance(cracha), Is.EqualTo(250));
    }
}
