// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Medical.Common.Traumas;
using Content.Medical.Shared.Traumas;
using Content.Medical.Shared.Wounds;
using Content.Server.Medical;
using Content.Shared.Body;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

[TestFixture]
public sealed class HealthAnalyzerTraumaTest : GameTest
{
    private static readonly ProtoId<OrganCategoryPrototype> Head = "Head";
    private static readonly ProtoId<OrganCategoryPrototype> Torso = "Torso";

    [Test]
    public async Task AnalisadorInformaFraturaESangramentoNaParteCorreta()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();

        EntityUid paciente = default;
        EntityUid cabeca = default;

        await server.WaitAssertion(() =>
        {
            paciente = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            cabeca = server.System<BodySystem>().GetOrgan(paciente, Head)
                ?? throw new AssertionException("Paciente não possui cabeça.");

            var trauma = server.System<TraumaSystem>();
            Assert.That(trauma.SetBoneIntegrity(cabeca, 0), Is.True);
            server.EntMan.GetComponent<WoundableComponent>(cabeca).Bleeds = 1;

            var state = server.System<HealthAnalyzerSystem>().GetHealthAnalyzerUiState((EntityUid?) paciente, null);

            Assert.Multiple(() =>
            {
                Assert.That(state.BoneDamage, Does.ContainKey(Head));
                Assert.That(state.BoneDamage[Head], Is.EqualTo(BoneSeverity.Broken));
                Assert.That(state.BoneDamage, Does.Not.ContainKey(Torso));
                Assert.That(state.Bleeding, Does.Contain(Head));
                Assert.That(state.Bleeding, Does.Not.Contain(Torso));
            });

            var bone = server.EntMan.GetComponent<BoneComponent>(cabeca);
            Assert.That(trauma.SetBoneIntegrity(cabeca, bone.IntegrityCap), Is.True);

            state = server.System<HealthAnalyzerSystem>().GetHealthAnalyzerUiState((EntityUid?) paciente, null);
            Assert.That(state.BoneDamage, Does.Not.ContainKey(Head));
        });
    }
}
