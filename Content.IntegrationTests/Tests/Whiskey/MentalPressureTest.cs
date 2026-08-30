// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Pressure;
using Content.Shared._Whiskey.Pressure;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre o que diferencia a pressão mental de uma barra comum: ela sabe de onde
/// veio, e cada fonte tem o próprio teto e o próprio ritmo de saída.
///
/// Se estes testes passassem com o sistema somando tudo num número só, o
/// sistema não precisaria existir.
/// </summary>
[TestFixture]
public sealed class MentalPressureTest : GameTest
{
    private static readonly ProtoId<PressureSourcePrototype> Morte = "WhiskeyPressaoMorte";
    private static readonly ProtoId<PressureSourcePrototype> Escuro = "WhiskeyPressaoEscuro";

    /// <summary>
    /// Duas fontes diferentes convivem, cada uma com o próprio peso, e o total
    /// é a soma delas.
    /// </summary>
    [Test]
    public async Task DuasFontesConvivemESomam()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            server.EntMan.AddComponent<MentalPressureComponent>(pessoa);

            var sis = server.System<MentalPressureSystem>();
            sis.Adicionar(pessoa, Morte);
            sis.Adicionar(pessoa, Escuro);
        });
        await pair.RunTicksSync(2);

        var comp = server.EntMan.GetComponent<MentalPressureComponent>(pessoa);
        var pesoMorte = server.ProtoMan.Index(Morte).Weight;
        var pesoEscuro = server.ProtoMan.Index(Escuro).Weight;

        Assert.Multiple(() =>
        {
            Assert.That(comp.Sources, Has.Count.EqualTo(2),
                "as duas fontes têm que existir separadas, senão a origem se perdeu");
            Assert.That(comp.Sources[Morte], Is.EqualTo(pesoMorte).Within(0.001f));
            Assert.That(comp.Sources[Escuro], Is.EqualTo(pesoEscuro).Within(0.001f));
            Assert.That(comp.Total, Is.EqualTo(pesoMorte + pesoEscuro).Within(0.001f));
        });
    }

    /// <summary>
    /// O teto é por fonte, e não só geral.
    ///
    /// É isto que impede uma coisa fraca e repetida de empatar com uma pesada.
    /// Sem o teto por fonte, ficar meia hora no escuro valeria o mesmo que ter
    /// visto alguém morrer, e a origem deixaria de significar coisa nenhuma.
    /// </summary>
    [Test]
    public async Task OTetoEPorFonte()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;

        var proto = Server.ProtoMan.Index(Escuro);

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            server.EntMan.AddComponent<MentalPressureComponent>(pessoa);

            var sis = server.System<MentalPressureSystem>();

            // Muito mais vezes do que o teto aguenta.
            for (var i = 0; i < 100; i++)
                sis.Adicionar(pessoa, Escuro);
        });
        await pair.RunTicksSync(2);

        var comp = server.EntMan.GetComponent<MentalPressureComponent>(pessoa);

        Assert.That(comp.Sources[Escuro], Is.EqualTo(proto.Cap).Within(0.001f),
            $"cem vezes a fonte {Escuro} tinha que parar no teto dela, {proto.Cap}");
    }

    /// <summary>
    /// Aliviar mexe em UMA fonte e deixa as outras em paz.
    ///
    /// É isto que permite tratar a causa em vez do total: sair da sala do
    /// cadáver alivia aquela entrada, e não o que a pessoa está sentindo de
    /// dor. Uma barra única não consegue fazer essa distinção.
    /// </summary>
    [Test]
    public async Task AliviarMexeSoNaFonteEscolhida()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            server.EntMan.AddComponent<MentalPressureComponent>(pessoa);

            var sis = server.System<MentalPressureSystem>();
            sis.Adicionar(pessoa, Morte);
            sis.Adicionar(pessoa, Escuro);

            // Alívio maior que o peso: a entrada tem que sumir inteira.
            sis.Aliviar(pessoa, Escuro, 9999f);
        });
        await pair.RunTicksSync(2);

        var comp = server.EntMan.GetComponent<MentalPressureComponent>(pessoa);
        var pesoMorte = server.ProtoMan.Index(Morte).Weight;

        Assert.Multiple(() =>
        {
            Assert.That(comp.Sources.ContainsKey(Escuro), Is.False, "a fonte aliviada tinha que sumir");
            Assert.That(comp.Sources.ContainsKey(Morte), Is.True, "a outra fonte não podia ter sido tocada");
            Assert.That(comp.Total, Is.EqualTo(pesoMorte).Within(0.001f));
        });
    }

    /// <summary>
    /// Toda fonte declarada tem texto de examinar, nos dois idiomas ativos.
    ///
    /// Sem o texto, examinar mostra a chave crua na tela e a superfície social
    /// do sistema, que é a razão dele existir, deixa de funcionar.
    /// </summary>
    [Test]
    public async Task TodaFonteTemTextoDeExaminar()
    {
        var protos = Server.ProtoMan;
        var loc = Server.ResolveDependency<ILocalizationManager>();
        var faltando = new List<string>();
        var total = 0;

        await Server.WaitPost(() =>
        {
            foreach (var fonte in protos.EnumeratePrototypes<PressureSourcePrototype>())
            {
                total++;
                if (!loc.TryGetString(fonte.Description, out _))
                    faltando.Add($"{fonte.ID} promete {fonte.Description}");
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.GreaterThan(0), "nenhuma fonte declarada, então este teste não confere nada");
            Assert.That(faltando, Is.Empty,
                "fonte sem texto de examinar: " + string.Join(", ", faltando));
        });
    }
}
