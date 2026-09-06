// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Medical.Common.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

[TestFixture]
public sealed class SlowOnDamageTest : GameTest
{
    private static readonly ProtoId<DamageGroupPrototype> GrupoDeDano = "Brute";

    [Test]
    public async Task CurarSoftCritRemoveSlowdownSemRejuvenescimento()
    {
        var pair = Pair;
        var server = Server;
        var mapa = await pair.CreateTestMap();

        EntityUid pessoa = default;

        await server.WaitPost(() =>
        {
            pessoa = server.EntMan.SpawnAtPosition("MobHuman", mapa.GridCoords);
            server.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(pessoa);
        });
        await pair.RunTicksSync(5);

        var movimento = server.EntMan.GetComponent<MovementSpeedModifierComponent>(pessoa);
        var caminhadaBase = movimento.WalkSpeedModifier;
        var corridaBase = movimento.SprintSpeedModifier;
        var dano = new DamageSpecifier(
            server.ProtoMan.Index(GrupoDeDano),
            FixedPoint2.New(100));

        await server.WaitPost(() =>
            server.System<DamageableSystem>().ChangeDamage(
                pessoa,
                dano,
                ignoreResistances: true,
                targetPart: TargetBodyPart.Head,
                canMiss: false));
        await pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(server.System<DamageableSystem>().GetTotalDamage(pessoa),
                Is.EqualTo(FixedPoint2.New(100)));
            Assert.That(movimento.WalkSpeedModifier, Is.LessThan(caminhadaBase));
            Assert.That(movimento.SprintSpeedModifier, Is.LessThan(corridaBase));
        });

        await server.WaitPost(() =>
            server.System<DamageableSystem>().ChangeDamage(
                pessoa,
                -dano,
                ignoreResistances: true,
                targetPart: TargetBodyPart.Head,
                canMiss: false));
        await pair.RunTicksSync(2);

        Assert.Multiple(() =>
        {
            Assert.That(server.System<DamageableSystem>().GetTotalDamage(pessoa), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(movimento.WalkSpeedModifier, Is.EqualTo(caminhadaBase).Within(0.001f));
            Assert.That(movimento.SprintSpeedModifier, Is.EqualTo(corridaBase).Within(0.001f));
        });
    }
}
