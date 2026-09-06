// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.EntityEffects;
using Content.Shared.CCVar;
using Content.Shared.EntityEffects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre a ponte entre efeito de entidade e humor.
///
/// Antes dela, nada em prototype nenhum conseguia mexer no humor: as únicas
/// coisas ligadas eram o dano, por código, e os traços periódicos. Química,
/// comida e evento não tinham por onde entrar.
/// </summary>
[TestFixture]
public sealed class AdjustMoodEffectTest : GameTest
{
    // Modificador que já existe no porte, positivo e sem categoria de saúde,
    // para o teste não depender de conteúdo que esta PR acrescenta.
    private static readonly ProtoId<MoodEffectPrototype> Modificador = "HungerOverfed";

    /// <summary>
    /// O efeito levanta o modificador em quem tem humor.
    /// </summary>
    [Test]
    public async Task OEfeitoLevantaOModificador()
    {
        var pair = Pair;
        var server = Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;

        try
        {
            await server.WaitPost(() =>
            {
                cfg.SetCVar(CCVars.MoodEnabled, true);
                pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
                server.EntMan.AddComponent<MoodComponent>(pessoa);
            });
            await pair.RunTicksSync(2);

            var humor = server.EntMan.GetComponent<MoodComponent>(pessoa);
            var neutro = humor.MoodThresholds[MoodThreshold.Neutral];
            var peso = server.ProtoMan.Index(Modificador).MoodChange;

            await server.WaitPost(() =>
                server.System<SharedEntityEffectsSystem>()
                    .ApplyEffect(pessoa, new AdjustMood { Effect = Modificador }));
            await pair.RunTicksSync(2);

            Assert.That(humor.CurrentMoodLevel, Is.EqualTo(neutro + peso).Within(0.001f),
                $"o efeito deveria ter levado o humor a {neutro + peso}");
        }
        finally
        {
            await server.WaitPost(() => cfg.SetCVar(CCVars.MoodEnabled, false));
        }
    }

    /// <summary>
    /// Em quem NÃO tem humor, o efeito não faz nada e não quebra.
    ///
    /// Isto importa porque neste fork o humor é de quem escolheu um traço que
    /// dá humor, e não de todo mundo. Um remédio de humor vai ser tomado por
    /// gente sem humor o tempo todo, e isso precisa ser um silêncio e não uma
    /// exceção no meio da rodada.
    /// </summary>
    [Test]
    public async Task EmQuemNaoTemHumorOEfeitoEInofensivo()
    {
        var pair = Pair;
        var server = Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapa = await pair.CreateTestMap();
        EntityUid pessoa = default;

        try
        {
            await server.WaitPost(() =>
            {
                cfg.SetCVar(CCVars.MoodEnabled, true);
                pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            });
            await pair.RunTicksSync(2);

            Assert.That(server.EntMan.HasComponent<MoodComponent>(pessoa), Is.False,
                "este teste precisa de alguém SEM humor para valer alguma coisa");

            await server.WaitPost(() =>
                server.System<SharedEntityEffectsSystem>()
                    .ApplyEffect(pessoa, new AdjustMood { Effect = Modificador }));
            await pair.RunTicksSync(2);

            Assert.That(server.EntMan.HasComponent<MoodComponent>(pessoa), Is.False,
                "o efeito não pode criar humor em quem não escolheu ter");
        }
        finally
        {
            await server.WaitPost(() => cfg.SetCVar(CCVars.MoodEnabled, false));
        }
    }
}
