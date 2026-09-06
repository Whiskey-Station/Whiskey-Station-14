// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre as guardas do gatilho de testemunhar morte.
///
/// O gatilho em si é fácil; o que decide se ele vira jogo ou vira ruído são as
/// condições. Sem elas, uma rodada de Lavaland encheria a estação inteira de
/// pressão e o sistema perderia o sentido no primeiro dia.
/// </summary>
[TestFixture]
public sealed class WitnessDeathTest : GameTest
{
    private static readonly ProtoId<PressureSourcePrototype> Morte = "WhiskeyPressaoMorte";

    /// <summary>
    /// Nasce uma testemunha com pressão a uma distância dada, mata alguém, e
    /// devolve quanta pressão de morte a testemunha ficou.
    /// </summary>
    private async Task<float> Cenario(float distancia, bool aTestemunhaEQuemMorre = false)
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();

        EntityUid vitima = default;
        EntityUid testemunha = default;

        await server.WaitPost(() =>
        {
            vitima = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);

            if (aTestemunhaEQuemMorre)
            {
                testemunha = vitima;
            }
            else
            {
                var longe = mapa.GridCoords.Offset(new System.Numerics.Vector2(distancia, 0));
                testemunha = server.EntMan.SpawnAtPosition("MobHuman", longe);
            }

            server.EntMan.AddComponent<MentalPressureComponent>(testemunha);
        });
        await pair.RunTicksSync(2);

        // Estado forçado em vez de dano, e isto foi conserto de um defeito real
        // apontado em revisão: 10000 de dano gibava a vítima, a entidade era
        // destruída durante os ticks, e o GetComponent lá embaixo estourava
        // KeyNotFoundException ANTES da asserção. O teste nunca chegava a
        // comparar o valor esperado, ou seja passava e reprovava por motivo
        // nenhum, sem exercitar o que promete.
        //
        // ChangeMobState dispara o mesmo MobStateChangedEvent que o gatilho
        // escuta, é determinístico e não depende de quanto dano gib qual
        // espécie. E deixa o cadáver de pé para poder ser consultado.
        await server.WaitPost(() =>
            server.System<MobStateSystem>().ChangeMobState(vitima, MobState.Dead));
        await pair.RunTicksSync(5);

        Assert.That(server.EntMan.EntityExists(testemunha), Is.True,
            "a testemunha precisa sobreviver ao cenário, senão o teste não mede nada");

        var comp = server.EntMan.GetComponent<MentalPressureComponent>(testemunha);
        return comp.Sources.GetValueOrDefault(Morte);
    }

    /// <summary>
    /// Quem está perto e vendo ganha a pressão.
    /// </summary>
    [Test]
    public async Task QuemVeDePertoSente()
    {
        var pressao = await Cenario(1f);
        var peso = Server.ProtoMan.Index(Morte).Weight;

        Assert.That(pressao, Is.EqualTo(peso).Within(0.001f),
            "quem viu alguém morrer a um tile de distância tinha que sentir");
    }

    /// <summary>
    /// Quem está longe não ganha nada.
    ///
    /// O alcance é de sete tiles, então vinte está confortavelmente fora. Isto
    /// cobre a metade de distância do InRangeUnOccluded; a metade de oclusão,
    /// que é a parede, precisaria de um mapa com parede e não está coberta por
    /// teste automático.
    /// </summary>
    [Test]
    public async Task QuemEstaLongeNaoSente()
    {
        var pressao = await Cenario(20f);

        Assert.That(pressao, Is.EqualTo(0f).Within(0.001f),
            "morte a vinte tiles não podia ter chegado em ninguém");
    }

    /// <summary>
    /// Quem morre não sente a própria morte.
    /// </summary>
    [Test]
    public async Task QuemMorreNaoSenteASiMesmo()
    {
        var pressao = await Cenario(0f, aTestemunhaEQuemMorre: true);

        Assert.That(pressao, Is.EqualTo(0f).Within(0.001f),
            "o morto não pode ganhar pressão da própria morte");
    }
}
