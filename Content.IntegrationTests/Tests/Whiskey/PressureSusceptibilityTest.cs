// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Pressure;
using Content.Shared._Whiskey.Pressure;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Uma fobia sente a fonte dela e nenhuma outra.
/// </summary>
/// <remarks>
/// Sem isto não dá para portar as fobias do /tg/station. Lá cada medo é um
/// trauma separado que não conhece os outros: quem tem monofobia sofre de
/// ficar sozinho e não sofre de ver morte. Se todo traço que dá pressão fizesse
/// a pessoa sentir tudo, monofobia seria só o traço Sensível com outro nome.
/// </remarks>
[TestFixture]
public sealed class PressureSusceptibilityTest : GameTest
{
    private static readonly ProtoId<PressureSourcePrototype> Solidao = "WhiskeyPressaoSolidao";
    private static readonly ProtoId<PressureSourcePrototype> Morte = "WhiskeyPressaoMorte";

    /// <summary>
    /// Nasce alguém com a lista de suscetibilidade dada, aplica as duas fontes,
    /// e devolve quanto ficou de cada uma.
    /// </summary>
    private async Task<(float solidao, float morte)> Cenario(params ProtoId<PressureSourcePrototype>[] sente)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid alvo = default;

        await server.WaitPost(() =>
        {
            alvo = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);

            var comp = server.EntMan.AddComponent<MentalPressureComponent>(alvo);
            foreach (var fonte in sente)
                comp.SusceptibleTo.Add(fonte);

            var sistema = server.System<MentalPressureSystem>();
            sistema.Adicionar(alvo, Solidao);
            sistema.Adicionar(alvo, Morte);
        });

        var pressao = server.EntMan.GetComponent<MentalPressureComponent>(alvo);

        return (pressao.Sources.GetValueOrDefault(Solidao, 0f),
                pressao.Sources.GetValueOrDefault(Morte, 0f));
    }

    /// <summary>
    /// Quem tem uma fobia só sente aquilo. É o caso da monofobia do TG.
    /// </summary>
    [Test]
    public async Task QuemTemFobiaNaoSenteAsOutrasFontes()
    {
        var (solidao, morte) = await Cenario(Solidao);

        TestContext.Out.WriteLine($"fóbico de solidão: solidão={solidao:F1} morte={morte:F1}");

        Assert.Multiple(() =>
        {
            Assert.That(solidao, Is.GreaterThan(0f),
                "quem tem medo de ficar sozinho tem que sentir a solidão");

            Assert.That(morte, Is.EqualTo(0f),
                "monofobia não é sensibilidade geral: ver morte não pode pesar em quem "
                + "tem medo de ficar sozinho, senão o traço vira outro Sensível");
        });
    }

    /// <summary>
    /// Lista vazia quer dizer todas, que é o que o traço Sensível precisa.
    /// </summary>
    [Test]
    public async Task ListaVaziaSenteTudo()
    {
        var (solidao, morte) = await Cenario();

        TestContext.Out.WriteLine($"sensível: solidão={solidao:F1} morte={morte:F1}");

        Assert.Multiple(() =>
        {
            Assert.That(solidao, Is.GreaterThan(0f), "o Sensível sente solidão");
            Assert.That(morte, Is.GreaterThan(0f), "o Sensível sente morte");
        });
    }

    /// <summary>
    /// A lista aceita mais de uma fonte, sem virar "todas".
    /// </summary>
    /// <remarks>
    /// Guarda contra a implementação preguiçosa em que qualquer lista com mais
    /// de um item passa a valer como vazia.
    /// </remarks>
    [Test]
    public async Task DuasFontesNaListaNaoViramTodas()
    {
        var (solidao, morte) = await Cenario(Solidao, "WhiskeyPressaoDor");

        TestContext.Out.WriteLine($"solidão e dor: solidão={solidao:F1} morte={morte:F1}");

        Assert.Multiple(() =>
        {
            Assert.That(solidao, Is.GreaterThan(0f), "solidão está na lista");
            Assert.That(morte, Is.EqualTo(0f), "morte não está na lista e não pode entrar");
        });
    }
}
