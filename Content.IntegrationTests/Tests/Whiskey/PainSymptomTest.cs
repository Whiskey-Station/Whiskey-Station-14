// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Pressure;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// O sintoma da dor, e principalmente o canal que ele NÃO pode usar.
/// </summary>
[TestFixture]
public sealed class PainSymptomTest : GameTest
{
    private static readonly ProtoId<PressureSourcePrototype> Dor = "WhiskeyPressaoDor";

    /// <summary>
    /// A dor fala, e não anda nem derruba coisa.
    /// </summary>
    /// <remarks>
    /// O canal é decisão de desenho e vale travar: fala é o único que os outros
    /// percebem sem examinar, e o único que não atrapalha o que a pessoa está
    /// fazendo. Lentidão empilharia em cima do que o dano já faz, e desajeitado
    /// tira o item da mão no meio de uma cirurgia.
    /// </remarks>
    [Test]
    public async Task ADorSaiPelaFalaENaoPeloCorpo()
    {
        await Server.WaitAssertion(() =>
        {
            var dor = Server.ProtoMan.Index(Dor);

            Assert.That(dor.Symptoms, Is.Not.Empty, "a dor precisa causar alguma coisa");

            var canais = dor.Symptoms.Select(s => s.Effect.Id).ToList();
            TestContext.Out.WriteLine("sintomas da dor: " + string.Join(", ", canais));

            Assert.Multiple(() =>
            {
                Assert.That(canais, Does.Contain("StatusEffectStutter"),
                    "o canal escolhido para a dor foi a fala");

                foreach (var proibido in new[] { "Slowdown", "Clumsy", "Stun" })
                {
                    Assert.That(canais.Any(c => c.Contains(proibido)), Is.False,
                        $"{proibido} tira controle da pessoa, e isso ficou fora do desenho de propósito");
                }
            });
        });
    }
}
