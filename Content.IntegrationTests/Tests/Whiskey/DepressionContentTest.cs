// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.Mood;
using Content.Shared.Dataset;
using Content.Shared.Traits;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Verifica os contratos entre o conteúdo de depressão da Whiskey e o motor
/// genérico de humor. Estes vínculos são resolvidos apenas em runtime.
/// </summary>
[TestFixture]
public sealed class DepressionContentTest : GameTest
{
    private static readonly ProtoId<TraitPrototype> DepressionTrait = "Depression";
    private static readonly ProtoId<LocalizedDatasetPrototype> DepressionThoughts = "WhiskeyDepressaoPensamentos";

    [Test]
    public async Task TraitReferencesExistingEffectAndDataset()
    {
        var server = Server;
        var protos = server.ProtoMan;
        var trait = protos.Index(DepressionTrait);
        var componentName = server.EntMan.ComponentFactory.GetComponentName(typeof(PeriodicMoodComponent));

        Assert.That(trait.Components.TryGetComponent(componentName, out var raw), Is.True,
            "o traço Depression deve incluir PeriodicMood");

        var periodic = (PeriodicMoodComponent) raw!;
        Assert.Multiple(() =>
        {
            Assert.That(protos.HasIndex<MoodEffectPrototype>(periodic.Effect), Is.True,
                $"o efeito {periodic.Effect} não existe");
            Assert.That(periodic.Messages, Is.EqualTo(DepressionThoughts));
            Assert.That(protos.HasIndex<LocalizedDatasetPrototype>(DepressionThoughts), Is.True);
            Assert.That(periodic.MinTimeBetween, Is.LessThan(periodic.MaxTimeBetween));
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task EffectExpiresBeforeNextEpisodeCanStart()
    {
        var server = Server;
        var trait = server.ProtoMan.Index(DepressionTrait);
        var componentName = server.EntMan.ComponentFactory.GetComponentName(typeof(PeriodicMoodComponent));
        Assert.That(trait.Components.TryGetComponent(componentName, out var raw), Is.True);

        var periodic = (PeriodicMoodComponent) raw!;
        var effect = server.ProtoMan.Index<MoodEffectPrototype>(periodic.Effect);

        Assert.That(effect.Timeout, Is.GreaterThan(0));
        Assert.That(effect.Timeout, Is.LessThan(periodic.MinTimeBetween),
            "o episódio precisa expirar antes que outro possa reiniciar sua categoria");

        await Task.CompletedTask;
    }

    [Test]
    public async Task EveryPromisedThoughtIsLocalized()
    {
        var server = Server;
        var dataset = server.ProtoMan.Index(DepressionThoughts);
        var loc = server.ResolveDependency<ILocalizationManager>();
        var missing = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var key in dataset.Values)
            {
                if (!loc.TryGetString(key, out _))
                    missing.Add(key);
            }
        });

        Assert.That(missing, Is.Empty,
            "o dataset de pensamentos promete chaves ausentes: " + string.Join(", ", missing));
    }
}
