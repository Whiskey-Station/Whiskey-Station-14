// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Mood;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre a tradução de dano para modificador de humor.
///
/// Fica em arquivo próprio de propósito, e não no <see cref="MoodTest"/>: esta
/// mudança e a da escada de faixas andam em Pull Requests separadas, e as duas
/// acrescentavam teste no fim do mesmo arquivo. Separando, elas deixam de
/// conflitar e dá para mergear em qualquer ordem.
/// </summary>
[TestFixture]
public sealed class MoodDamageTest : GameTest
{
    private static readonly ProtoId<DamageGroupPrototype> GrupoDeDano = "Brute";

    /// <summary>
    /// Cair no chão tem que levantar o modificador pesado de saúde.
    ///
    /// O porte media o dano contra o limiar de <see cref="MobState.Critical"/>,
    /// que é o estado onde a pessoa desmaia no jogo de origem. Aqui o Trauma
    /// acrescentou o <see cref="MobState.SoftCrit"/> antes dele, e é nele que a
    /// pessoa cai. Com a escada herdada de species_base.yml, que é 100 SoftCrit
    /// e 150 Critical, cair valia 100/150, ou seja 0,67, e o modificador pesado
    /// exigia 0,8. Resultado: por mais machucada que a pessoa estivesse, o
    /// humor parava no modificador de -7 e a faixa não passava de Ruim.
    ///
    /// Este teste lê o limiar do próprio prototype em vez de cravar 100, para
    /// continuar valendo se alguém rebalancear a saúde.
    /// </summary>
    [Test]
    public async Task CairNoChaoLevantaOModificadorPesado()
    {
        var pair = Pair;
        var server = Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapa = await pair.CreateTestMap();

        EntityUid pessoa = default;
        FixedPoint2? limiar = null;

        try
        {
            await server.WaitPost(() =>
            {
                cfg.SetCVar(CCVars.MoodEnabled, true);
                pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
                server.EntMan.AddComponent<MoodComponent>(pessoa);
            });
            await pair.RunTicksSync(2);

            await server.WaitPost(() =>
                server.System<MobThresholdSystem>()
                    .TryGetThresholdForState(pessoa, MobState.SoftCrit, out limiar));

            Assert.That(limiar, Is.Not.Null,
                "o humanoide precisa ter limiar de SoftCrit, senão este teste não prova nada");

            // Dano exatamente no ponto em que a pessoa cai no chão, ignorando
            // resistência para o número chegar inteiro.
            await server.WaitPost(() =>
                server.System<DamageableSystem>().TryChangeDamage(
                    pessoa,
                    new DamageSpecifier(server.ProtoMan.Index(GrupoDeDano), limiar!.Value),
                    true));
            await pair.RunTicksSync(2);

            var humor = server.EntMan.GetComponent<MoodComponent>(pessoa);

            Assert.That(humor.CategorisedEffects.TryGetValue("Health", out var efeito), Is.True,
                "dano tem que levantar algum modificador da categoria Health");

            Assert.That(efeito, Is.EqualTo("HealthHeavyDamage"),
                "quem cai no chão tem que pegar o modificador pesado. Vindo HealthSevereDamage, "
                + "o cálculo voltou a usar o Critical como referência em vez do SoftCrit.");
        }
        finally
        {
            await server.WaitPost(() => cfg.SetCVar(CCVars.MoodEnabled, false));
        }
    }
}
