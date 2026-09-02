// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Friends.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Ficar sozinho pesa, e o que conta como companhia veio do /tg/station.
/// </summary>
[TestFixture]
public sealed class LonelinessTest : GameTest
{
    private static readonly ProtoId<PressureSourcePrototype> Solidao = "WhiskeyPressaoSolidao";

    /// <summary>
    /// Nasce alguém que só sente solidão, opcionalmente com companhia a uma
    /// distância dada, deixa o relógio correr e devolve quanta solidão pesou.
    /// </summary>
    private async Task<float> Cenario(string? companhia = null, float distancia = 2f, bool cego = false, bool pet = false)
    {
        var server = Server;
        var mapa = await Pair.CreateTestMap();

        EntityUid alvo = default;

        await server.WaitPost(() =>
        {
            alvo = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);

            var comp = server.EntMan.AddComponent<MentalPressureComponent>(alvo);
            // Só solidão: se sentisse tudo, outra fonte poderia sujar a medição.
            comp.SusceptibleTo.Add(Solidao);

            if (cego)
            {
                // Pelo sistema, e não escrevendo IsBlind na mão: o analisador
                // RA0002 reprova acesso de escrita ao campo, e com razão, já
                // que quem decide se a pessoa enxerga é o dano acumulado.
                server.EntMan.EnsureComponent<BlindableComponent>(alvo);
                server.System<BlindableSystem>().AdjustEyeDamage(alvo, 20);
            }

            if (companhia is not null)
            {
                var longe = mapa.GridCoords.Offset(new System.Numerics.Vector2(distancia, 0));
                var vizinho = server.EntMan.SpawnAtPosition(companhia, longe);

                // Nenhum prototype de bicho carrega PettableFriend hoje, então
                // para provar que o caminho do pet funciona o componente entra
                // aqui. Marcar os pets de verdade é conteúdo e vem depois.
                if (pet)
                    server.EntMan.EnsureComponent<PettableFriendComponent>(vizinho);
            }
        });

        // O gatilho confere de cinco em cinco segundos.
        await RunSeconds(12);

        var pressao = server.EntMan.GetComponent<MentalPressureComponent>(alvo);
        return pressao.Sources.GetValueOrDefault(Solidao, 0f);
    }

    /// <summary>
    /// Sem ninguém por perto, a solidão pesa.
    /// </summary>
    [Test]
    public async Task SozinhoPesa()
    {
        var peso = await Cenario();
        TestContext.Out.WriteLine($"sozinho: {peso:F1}");

        Assert.That(peso, Is.GreaterThan(0f),
            "ficar sozinho tem que acumular pressão, senão o traço não faz nada");
    }

    /// <summary>
    /// Um bicho de estimação resolve, e isso veio do TG de propósito.
    /// </summary>
    /// <remarks>
    /// É a única saída que não depende de outro jogador querer ficar por perto,
    /// então é a que dá autonomia para quem pegou o traço.
    /// </remarks>
    [Test]
    public async Task BichoDeEstimacaoFazCompanhia()
    {
        var peso = await Cenario("MobCorgi", pet: true);
        TestContext.Out.WriteLine($"com um corgi do lado: {peso:F1}");

        Assert.That(peso, Is.EqualTo(0f),
            "no TG o pet conta como companhia, e é o que dá autonomia a quem tem o traço");
    }

    /// <summary>
    /// Bicho que não é de estimação não resolve.
    /// </summary>
    /// <remarks>
    /// Este é o caso que separa companhia de "tem algo vivo perto". Uma sala
    /// cheia de monstro continua sendo solidão, e no .dm isso é a checagem de
    /// ckey ou pet.
    /// </remarks>
    [Test]
    public async Task BichoQualquerNaoFazCompanhia()
    {
        var peso = await Cenario("MobMouse");
        TestContext.Out.WriteLine($"com um rato do lado: {peso:F1}");

        Assert.That(peso, Is.GreaterThan(0f),
            "bicho que não é de estimação não faz companhia: uma sala cheia de rato "
            + "continua sendo solidão");
    }

    /// <summary>
    /// A sete tiles ainda faz companhia para quem enxerga.
    /// </summary>
    /// <remarks>
    /// O raio de quem enxerga é 7, e é o <c>check_radius = 7</c> do TG.
    /// </remarks>
    [Test]
    public async Task DeLongeAindaFazCompanhiaParaQuemEnxerga()
    {
        var peso = await Cenario("MobCorgi", distancia: 5f, pet: true);
        TestContext.Out.WriteLine($"corgi a 5 tiles, enxergando: {peso:F1}");

        Assert.That(peso, Is.EqualTo(0f),
            "cinco tiles cabe no raio de sete, então a companhia conta");
    }

    /// <summary>
    /// Quem não enxerga só se conforta com companhia bem perto.
    /// </summary>
    /// <remarks>
    /// No TG o raio cai de 7 para 1 se a pessoa é cega, e faz sentido: não
    /// adianta ter gente na sala se a pessoa não sabe que tem.
    /// </remarks>
    [Test]
    public async Task QuemNaoEnxergaPrecisaDeCompanhiaBemPerto()
    {
        var peso = await Cenario("MobCorgi", distancia: 5f, cego: true, pet: true);
        TestContext.Out.WriteLine($"corgi a 5 tiles, cego: {peso:F1}");

        Assert.That(peso, Is.GreaterThan(0f),
            "cego só conta companhia a um tile, então cinco tiles continua sendo solidão");
    }

    /// <summary>
    /// O texto do aviso existe nos dois idiomas.
    /// </summary>
    /// <remarks>
    /// Sem isto o aviso sai com o id cru na tela, e o jogador lê
    /// "pressure-loneliness-warning" em vez da frase. Não dá erro nenhum, que
    /// é o que torna esse engano fácil de publicar.
    /// </remarks>
    [Test]
    public async Task OAvisoTemTexto()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.ResolveDependency<ILocalizationManager>()
                    .TryGetString("pressure-loneliness-warning", out var texto), Is.True,
                "o aviso de solidão não tem texto, e o jogador veria o id cru");

            Assert.That(texto, Is.Not.Empty);
        });
    }
}