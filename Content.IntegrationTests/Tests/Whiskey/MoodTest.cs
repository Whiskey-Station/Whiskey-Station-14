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
}
