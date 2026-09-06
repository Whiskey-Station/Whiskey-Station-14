// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Pressure;
using Content.Server.Examine;
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
    private static readonly ProtoId<PressureSourcePrototype> Dor = "WhiskeyPressaoDor";
    private static readonly ProtoId<PressureSourcePrototype> Solidao = "WhiskeyPressaoSolidao";

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

    /// <summary>
    /// A soma das fontes respeita o teto geral.
    /// </summary>
    /// <remarks>
    /// Apontado em revisão, e o número mostra por que importa: as quatro fontes
    /// declaradas hoje têm tetos que somam 160, contra um Max de 100. Ou seja
    /// basta as quatro estarem ativas ao mesmo tempo para o clamp do Recalcular
    /// ser exercitado, e nenhum teste passava por esse caminho.
    ///
    /// O teste cobra as duas coisas juntas: que o total respeite o teto, e que
    /// as fontes continuem guardadas por inteiro por baixo dele. Se o clamp
    /// fosse aplicado nas fontes em vez do total, a origem se perderia, que é
    /// justamente o que o sistema existe para evitar.
    /// </remarks>
    [Test]
    public async Task SomaDeVariasFontesRespeitaOTetoGeral()
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

            // Enche cada fonte até o teto dela, para a soma passar de 100.
            foreach (var fonte in new[] { Morte, Escuro, Dor, Solidao })
            {
                for (var i = 0; i < 30; i++)
                    sis.Adicionar(pessoa, fonte);
            }
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<MentalPressureComponent>(pessoa);
            var somaDasFontes = comp.Sources.Values.Sum();

            TestContext.Out.WriteLine(
                $"soma das fontes: {somaDasFontes}, total: {comp.Total}, teto: {comp.Max}");

            Assert.Multiple(() =>
            {
                Assert.That(somaDasFontes, Is.GreaterThan(comp.Max),
                    "o cenário precisa passar do teto, senão o clamp não é exercitado");

                Assert.That(comp.Total, Is.LessThanOrEqualTo(comp.Max),
                    "o total não pode passar do teto geral");

                Assert.That(comp.Sources, Has.Count.EqualTo(4),
                    "o teto é do total, e não das fontes: a origem tem que continuar inteira por baixo dele");
            });
        });
    }

    /// <summary>
    /// O examinar mostra a pressão mais pesada primeiro.
    /// </summary>
    /// <remarks>
    /// Apontado em revisão: o OnExamined ordena por peso e nada testava a ordem.
    ///
    /// A ordem não é enfeite. Quem examina lê primeiro o que mais importa, em
    /// vez da ordem em que as coisas aconteceram, e é isso que transforma o
    /// examinar em deixa de conversa para o Psicólogo. Com a ordem trocada, a
    /// primeira linha seria a mais fraca e a leitura mudaria de sentido.
    /// </remarks>
    [Test]
    public async Task OExaminarMostraAPressaoMaisPesadaPrimeiro()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();

        EntityUid pessoa = default;
        EntityUid quemOlha = default;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            quemOlha = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            server.EntMan.AddComponent<MentalPressureComponent>(pessoa);

            var sis = server.System<MentalPressureSystem>();

            // O escuro entra primeiro e é o mais leve, com peso 4. A morte entra
            // depois e é a mais pesada, com 25. Se a saída fosse por ordem de
            // chegada, o escuro viria na frente.
            sis.Adicionar(pessoa, Escuro);
            sis.Adicionar(pessoa, Morte);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var texto = server.System<ExamineSystem>()
                .GetExamineText(pessoa, quemOlha)
                .ToMarkup();

            var protos = server.ProtoMan;
            var loc = server.ResolveDependency<ILocalizationManager>();
            var daMorte = loc.GetString(protos.Index(Morte).Description);
            var doEscuro = loc.GetString(protos.Index(Escuro).Description);

            var posMorte = texto.IndexOf(daMorte, System.StringComparison.Ordinal);
            var posEscuro = texto.IndexOf(doEscuro, System.StringComparison.Ordinal);

            TestContext.Out.WriteLine($"posição da morte: {posMorte}, do escuro: {posEscuro}");

            Assert.Multiple(() =>
            {
                Assert.That(posMorte, Is.GreaterThanOrEqualTo(0), "o texto da morte tinha que aparecer");
                Assert.That(posEscuro, Is.GreaterThanOrEqualTo(0), "o texto do escuro tinha que aparecer");
                Assert.That(posMorte, Is.LessThan(posEscuro),
                    "a pressão mais pesada tem que vir primeiro, mesmo tendo chegado depois");
            });
        });
    }
}
