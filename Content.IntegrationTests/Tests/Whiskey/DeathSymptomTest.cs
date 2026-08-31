// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Whiskey.Pressure;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.StatusEffectNew;
using Content.Trauma.Shared.Viewcone.Components;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// O sintoma da morte, de ponta a ponta e não só no contrato.
/// </summary>
/// <remarks>
/// Escrito assim de propósito. Na tempestade radioativa eu testei o contrato
/// entre um reagente e uma condição, os dois lados batiam, e a proteção não
/// funcionava em jogo. Contrato bater não é comportamento funcionar, e é o
/// comportamento que a pessoa sente.
/// </remarks>
[TestFixture]
public sealed class DeathSymptomTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly StatusEffectsSystem _status = null!;

    private static readonly ProtoId<PressureSourcePrototype> Morte = "WhiskeyPressaoMorte";
    private static readonly EntProtoId Apertada = "StatusEffectVisaoApertada";
    private static readonly EntProtoId Tunel = "StatusEffectVisaoTunel";

    /// <summary>
    /// Uma morte não faz nada. Duas apertam. Tratar a causa solta.
    /// </summary>
    [Test]
    public async Task VerMorteApertaAVisaoESoltaDepois()
    {
        var pessoa = await Spawn("MobHuman");
        var pressao = Server.System<MentalPressureSystem>();

        await Server.WaitAssertion(() =>
        {
            Server.EntMan.EnsureComponent<MentalPressureComponent>(pessoa);

            // Uma morte: 25 de peso, abaixo do primeiro degrau, que é 30.
            pressao.Adicionar(pessoa, Morte);
            Assert.That(_status.HasStatusEffect(pessoa, Apertada), Is.False,
                "uma morte é coisa de rodada normal e não pode virar sintoma sozinha");

            // A segunda leva a 50, que é o teto da fonte e o segundo degrau.
            pressao.Adicionar(pessoa, Morte);
            var peso = Server.EntMan.GetComponent<MentalPressureComponent>(pessoa)
                .Sources.GetValueOrDefault(Morte);
            TestContext.Out.WriteLine($"peso depois de duas mortes: {peso}");

            Assert.Multiple(() =>
            {
                Assert.That(_status.HasStatusEffect(pessoa, Apertada), Is.True,
                    "duas mortes na mesma cena têm que apertar a visão");
                Assert.That(_status.HasStatusEffect(pessoa, Tunel), Is.True,
                    $"com {peso} de peso o segundo degrau também vale");
            });

            pressao.Aliviar(pessoa, Morte, 100f);
        });

        // O TryRemoveStatusEffect usa PredictedQueueDel, ou seja a entidade do
        // status só morre no fim do tique. Conferir no mesmo tique dá falso
        // positivo, e foi o que aconteceu na primeira versão deste teste.
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(_status.HasStatusEffect(pessoa, Apertada), Is.False,
                    "sem a pressão não pode sobrar sintoma");
                Assert.That(_status.HasStatusEffect(pessoa, Tunel), Is.False);
            });
        });
    }

    /// <summary>
    /// O sintoma promete apertar a visão. Sem o modificador no status, a
    /// promessa é só texto.
    /// </summary>
    [Test]
    public async Task OsSintomasRealmenteMexemNoCone()
    {
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ProtoMan;
            var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(ViewconeModifierComponent));

            foreach (var id in new[] { Apertada, Tunel })
            {
                Assert.That(protos.Index(id).Components.TryGetComponent(nome, out var raw), Is.True,
                    $"{id} precisa ter ViewconeModifier, senão não aperta nada");

                var mod = (ViewconeModifierComponent) raw!;
                TestContext.Out.WriteLine($"{id.Id}: modificador {mod.AngleModifier}");

                Assert.Multiple(() =>
                {
                    Assert.That(mod.AngleModifier, Is.LessThan(1f), "sintoma tem que estreitar");
                    Assert.That(mod.AngleModifier, Is.GreaterThan(0f),
                        "zerar a visão é o jogo jogando pela pessoa, e isso ficou fora do desenho de propósito");
                });
            }
        });
    }
}
