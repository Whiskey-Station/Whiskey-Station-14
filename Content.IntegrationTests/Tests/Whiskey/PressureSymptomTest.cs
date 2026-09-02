// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Pressure;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// As regras do motor de sintomas, valendo para qualquer fonte que exista hoje
/// ou venha a existir. O sintoma de cada fonte é conteúdo e vive na PR dela.
/// </summary>
[TestFixture]
public sealed class PressureSymptomTest : GameTest
{
    /// <summary>
    /// Id de sintoma errado não dá erro nenhum em jogo: o sintoma simplesmente
    /// nunca aparece, e ninguém descobre até alguém reclamar.
    /// </summary>
    [Test]
    public async Task TodoSintomaDeclaradoExiste()
    {
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ProtoMan;
            var total = 0;

            foreach (var fonte in protos.EnumeratePrototypes<PressureSourcePrototype>())
            {
                foreach (var sintoma in fonte.Symptoms)
                {
                    total++;
                    Assert.That(protos.HasIndex(sintoma.Effect), Is.True,
                        $"a fonte {fonte.ID} aponta para {sintoma.Effect}, que não existe");
                    Assert.That(sintoma.At, Is.GreaterThan(0f),
                        $"degrau em zero na fonte {fonte.ID} ligaria o sintoma para sempre");
                }
            }

            TestContext.Out.WriteLine($"sintomas declarados: {total}");
        });
    }

    /// <summary>
    /// Duas fontes com o mesmo sintoma fazem a origem deixar de significar
    /// alguma coisa, e origem é a razão de este sistema existir em vez de uma
    /// barra de sanidade.
    /// </summary>
    [Test]
    public async Task FontesDiferentesUsamCanaisDiferentes()
    {
        await Server.WaitAssertion(() =>
        {
            var usados = new Dictionary<string, string>();

            foreach (var fonte in Server.ProtoMan.EnumeratePrototypes<PressureSourcePrototype>())
            {
                foreach (var sintoma in fonte.Symptoms)
                {
                    if (usados.TryGetValue(sintoma.Effect.Id, out var dono) && dono != fonte.ID)
                    {
                        Assert.Fail(
                            $"{fonte.ID} e {dono} usam o mesmo sintoma {sintoma.Effect.Id}, "
                            + "e aí a origem deixa de significar alguma coisa");
                    }

                    usados[sintoma.Effect.Id] = fonte.ID;
                }
            }

            TestContext.Out.WriteLine($"canais em uso: {usados.Count}");
        });
    }

    /// <summary>
    /// Degrau repetido na mesma fonte é engano de quem escreveu: dois sintomas
    /// no mesmo peso deviam ser um só.
    /// </summary>
    [Test]
    public async Task NaoExisteDegrauRepetidoNaMesmaFonte()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var fonte in Server.ProtoMan.EnumeratePrototypes<PressureSourcePrototype>())
            {
                var degraus = new HashSet<float>();

                foreach (var sintoma in fonte.Symptoms)
                {
                    Assert.That(degraus.Add(sintoma.At), Is.True,
                        $"a fonte {fonte.ID} tem dois sintomas no degrau {sintoma.At}");
                }
            }
        });
    }

    /// <summary>
    /// Sintoma que liga e desliga em poucos segundos não comunica nada.
    /// </summary>
    /// <remarks>
    /// Escrito depois de um teste em jogo, e é o tipo de defeito que nenhum
    /// teste anterior pegaria: tudo funcionava, e mesmo assim o log registrou
    /// quatro ciclos de liga e desliga seguidos. A gagueira PISCAVA em vez de
    /// durar, porque o degrau caía bem no meio da faixa em que o peso oscila
    /// durante uma briga.
    ///
    /// A conta espelha o Update de propósito, em vez de dividir peso por
    /// decaimento. Duas razões. Uma unidade de decaimento não é um segundo, é
    /// um ciclo inteiro de <see cref="MentalPressureComponent.DecayInterval"/>,
    /// e a divisão crua responde em ciclos achando que responde em segundos.
    /// A outra é o critério de desligar ser >= e não >, o que rende um ciclo a
    /// mais que a divisão não enxerga.
    ///
    /// O intervalo vem do componente e não de uma constante daqui: se um dia
    /// ele mudar, este teste tem que mudar de resposta junto, senão volta a
    /// medir uma coisa e afirmar outra.
    ///
    /// Sintoma que dura menos que o mínimo aqui aparece para quem joga como
    /// defeito, e não como consequência.
    /// </remarks>
    [Test]
    public async Task NenhumSintomaPiscaEmPoucosSegundos()
    {
        // Três ciclos de decaimento. Menos que isto não dá tempo de alguém
        // perto perceber, e o sintoma vira ruído.
        var intervalo = (float) new MentalPressureComponent().DecayInterval.TotalSeconds;
        var minimoSegundos = intervalo * 3f;

        await Server.WaitAssertion(() =>
        {
            foreach (var fonte in Server.ProtoMan.EnumeratePrototypes<PressureSourcePrototype>())
            {
                foreach (var sintoma in fonte.Symptoms)
                {
                    // Quantos gatilhos para alcançar o degrau, respeitando o teto.
                    var peso = 0f;
                    var gatilhos = 0;
                    while (peso < sintoma.At && gatilhos < 100)
                    {
                        peso = MathF.Min(peso + fonte.Weight, fonte.Cap);
                        gatilhos++;
                    }

                    Assert.That(peso, Is.GreaterThanOrEqualTo(sintoma.At),
                        $"a fonte {fonte.ID} nunca alcança o degrau {sintoma.At}: "
                        + $"o teto dela é {fonte.Cap}, então o sintoma é inalcançável");

                    // Quantos ciclos de decaimento até o peso cair abaixo do
                    // degrau, contados como o Update conta.
                    var ciclos = 0;
                    var restante = peso;
                    while (restante >= sintoma.At && ciclos < 1000)
                    {
                        restante -= fonte.Decay;
                        ciclos++;
                    }

                    var duracao = ciclos * intervalo;

                    TestContext.Out.WriteLine(
                        $"{fonte.ID} -> {sintoma.Effect.Id}: {gatilhos} gatilho(s), "
                        + $"peso {peso}, degrau {sintoma.At}, "
                        + $"dura {ciclos} ciclo(s) = {duracao:F0}s");

                    Assert.That(duracao, Is.GreaterThanOrEqualTo(minimoSegundos),
                        $"{fonte.ID} liga {sintoma.Effect.Id} e desliga em {duracao:F1}s. "
                        + "Sintoma que pisca parece defeito para quem joga: ou o degrau "
                        + "desce, ou o peso sobe, ou o decaimento cai");
                }
            }
        });
    }
}
