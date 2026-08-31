// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using System.Linq;
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
/// Cobre o sintoma de ponta a ponta, e não só o contrato de nomes.
///
/// Escrito assim de propósito: na tempestade radioativa eu testei o contrato
/// entre o reagente e a condição, os dois lados batiam, e mesmo assim a
/// proteção não funcionava em jogo. Contrato bater não é comportamento
/// funcionar, e é o comportamento que o jogador sente.
/// </summary>
[TestFixture]
public sealed class PressureSymptomTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly StatusEffectsSystem _status = null!;

    private static readonly ProtoId<PressureSourcePrototype> Morte = "WhiskeyPressaoMorte";
    private static readonly EntProtoId Apertada = "StatusEffectVisaoApertada";
    private static readonly EntProtoId Tunel = "StatusEffectVisaoTunel";
    private static readonly ProtoId<PressureSourcePrototype> Dor = "WhiskeyPressaoDor";

    /// <summary>
    /// Uma morte sozinha não faz nada. Duas apertam. E quando a pressão sai, o
    /// sintoma sai junto.
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

            // Tratar a causa: o sintoma tem que sair junto com a pressão.
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
    /// O sintoma promete apertar a visão. Se o status não modificar o cone, a
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

    /// <summary>
    /// Todo sintoma declarado numa fonte precisa existir como prototype. Id
    /// errado aqui não dá erro nenhum: o sintoma simplesmente nunca aparece.
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

                var degraus = fonte.Symptoms.Select(s => s.At).ToList();
                Assert.That(degraus, Is.Unique, $"a fonte {fonte.ID} tem dois sintomas no mesmo degrau");
            }

            TestContext.Out.WriteLine($"sintomas declarados: {total}");
        });
    }

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

    /// <summary>
    /// Cada fonte tem que falar por um canal diferente, senão elas viram a
    /// mesma coisa com nomes diferentes e o sistema perde a razão de existir.
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
}
