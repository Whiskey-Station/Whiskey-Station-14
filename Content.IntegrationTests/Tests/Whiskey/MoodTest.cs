// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Overlays;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Verifica os contratos de protótipo, estado de rede e ciclo de vida do porte
/// do sistema de humor sem depender de conteúdo específico da Whiskey.
/// </summary>
[TestFixture]
public sealed class MoodTest : GameTest
{
    private static readonly ProtoId<MoodEffectPrototype> TestEffect = "HungerOverfed";

    [Test]
    public async Task TodoAlertaDeFaixaExiste()
    {
        var protos = Server.ProtoMan;
        var padrao = new MoodComponent();

        Assert.That(padrao.MoodThresholdsAlerts, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (faixa, alerta) in padrao.MoodThresholdsAlerts)
            {
                Assert.That(protos.HasIndex<AlertPrototype>(alerta), Is.True,
                    $"a faixa {faixa} aponta para o alerta inexistente {alerta}");
            }
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task TodoModificadorDeHumorTemDescricaoECategoriaValida()
    {
        var server = Server;
        var protos = server.ProtoMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var semDescricao = new List<string>();
        var semCategoria = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var efeito in protos.EnumeratePrototypes<MoodEffectPrototype>())
            {
                if (!loc.TryGetString($"mood-effect-{efeito.ID}", out _))
                    semDescricao.Add(efeito.ID);

                if (efeito.Category is { } categoria && !protos.HasIndex<MoodCategoryPrototype>(categoria))
                    semCategoria.Add($"{efeito.ID}: {categoria}");
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(semDescricao, Is.Empty,
                "modificadores sem descrição: " + string.Join(", ", semDescricao));
            Assert.That(semCategoria, Is.Empty,
                "modificadores com categoria inexistente: " + string.Join(", ", semCategoria));
        });
    }

    [Test]
    public async Task EfeitoAtualizaEstadoDeRedeELimpaComponentesNoShutdown()
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

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<NetMoodComponent>(pessoa), Is.True);
                Assert.That(server.EntMan.HasComponent<SaturationScaleOverlayComponent>(pessoa), Is.True);
            });

            var efeito = server.ProtoMan.Index(TestEffect);
            await server.WaitPost(() =>
                server.EntMan.EventBus.RaiseLocalEvent(pessoa, new MoodEffectEvent(TestEffect)));
            await pair.RunTicksSync(2);

            var rede = server.EntMan.GetComponent<NetMoodComponent>(pessoa);
            Assert.That(rede.CurrentMoodLevel,
                Is.EqualTo(rede.NeutralMoodThreshold + efeito.MoodChange).Within(0.001f));

            await server.WaitPost(() => server.EntMan.RemoveComponent<MoodComponent>(pessoa));
            await pair.RunTicksSync(2);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<NetMoodComponent>(pessoa), Is.False);
                Assert.That(server.EntMan.HasComponent<SaturationScaleOverlayComponent>(pessoa), Is.False);
            });
        }
        finally
        {
            await server.WaitPost(() => cfg.SetCVar(CCVars.MoodEnabled, false));
        }
    }

    /// <summary>
    /// A escada de humor precisa continuar sendo a do /tg/station.
    ///
    /// Os pesos que os modificadores usam no YAML são os defines do TG, de
    /// <c>MOOD_SAD4</c> a <c>MOOD_HAPPY4</c>. O Einstein trouxe os pesos e
    /// deixou as faixas dele para trás, e o resultado era que quase nada
    /// atravessava faixa: um ferimento de -7 deixava a pessoa em Neutro.
    ///
    /// Este teste existe para ninguém mexer num dos dois lados sozinho. Quem
    /// quiser rebalancear tem que trocar peso e faixa juntos, e trocar este
    /// teste junto, que é o momento em que a pessoa lê o porquê.
    /// </summary>
    [Test]
    public async Task AEscadaDeHumorBateComOsDefinesDoTg()
    {
        var padrao = new MoodComponent();
        var neutro = padrao.MoodThresholds[MoodThreshold.Neutral];

        // Faixa daqui, e o define do TG que ela representa.
        var esperado = new Dictionary<MoodThreshold, float>
        {
            { MoodThreshold.Perfect, 15f },      // MOOD_HAPPY4
            { MoodThreshold.Exceptional, 10f },  // MOOD_HAPPY3
            { MoodThreshold.Great, 6f },         // MOOD_HAPPY2
            { MoodThreshold.Good, 2f },          // MOOD_HAPPY1
            { MoodThreshold.Neutral, 0f },       // MOOD_NEUTRAL
            { MoodThreshold.Meh, -3f },          // MOOD_SAD1
            { MoodThreshold.Bad, -7f },          // MOOD_SAD2
            { MoodThreshold.Terrible, -15f },    // MOOD_SAD3
            { MoodThreshold.Horrible, -20f },    // MOOD_SAD4
        };

        Assert.Multiple(() =>
        {
            foreach (var (faixa, define) in esperado)
            {
                Assert.That(padrao.MoodThresholds.TryGetValue(faixa, out var valor), Is.True,
                    $"a faixa {faixa} sumiu do mapa de limiares");

                Assert.That(valor, Is.EqualTo(neutro + define).Within(0.001f),
                    $"a faixa {faixa} deveria valer {neutro + define}, que é o neutro mais o define {define} do TG");
            }
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Todo alerta declarado precisa ter uma faixa capaz de chegar nele.
    ///
    /// Isto pega um defeito que veio do próprio Einstein: eles declaravam um
    /// alerta para a faixa Insane e nunca puseram Insane no mapa de limiares,
    /// então aquele ícone era inalcançável desde sempre. No TG a insanidade não
    /// é faixa de humor, é faixa de sanidade, que é um segundo número.
    ///
    /// O <see cref="TodoAlertaDeFaixaExiste"/> não pega isso, porque ele
    /// confere se o prototype do alerta existe, e existir ele existia.
    /// </summary>
    [Test]
    public async Task TodoAlertaApontaParaUmaFaixaQueExiste()
    {
        var padrao = new MoodComponent();
        var orfaos = new List<string>();

        foreach (var faixa in padrao.MoodThresholdsAlerts.Keys)
        {
            if (!padrao.MoodThresholds.ContainsKey(faixa))
                orfaos.Add(faixa.ToString());
        }

        Assert.That(orfaos, Is.Empty,
            "alerta declarado para faixa que não existe no mapa de limiares, ou seja ícone que nunca aparece: "
            + string.Join(", ", orfaos));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Duas faixas não podem valer o mesmo número.
    ///
    /// O <c>GetMoodThreshold</c> escolhe o menor limiar maior ou igual ao
    /// humor. Com valor repetido, qual das duas ganha depende da ordem de
    /// iteração do dicionário, e a perdedora vira faixa morta sem erro nenhum.
    /// Com as faixas apertadas na escala do TG, a distância entre duas delas
    /// chega a ser de dois pontos, então encostar uma na outra ficou fácil.
    /// </summary>
    [Test]
    public async Task NenhumaFaixaTemOMesmoValor()
    {
        var padrao = new MoodComponent();
        var vistos = new Dictionary<float, MoodThreshold>();
        var repetidos = new List<string>();

        foreach (var (faixa, valor) in padrao.MoodThresholds)
        {
            if (vistos.TryGetValue(valor, out var anterior))
                repetidos.Add($"{faixa} e {anterior} empatam em {valor}");
            else
                vistos[valor] = faixa;
        }

        Assert.That(repetidos, Is.Empty,
            "faixas empatadas no mesmo valor, e uma delas nunca vai aparecer: " + string.Join(", ", repetidos));

        await Task.CompletedTask;
    }
}
