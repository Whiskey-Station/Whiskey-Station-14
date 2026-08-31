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
}
