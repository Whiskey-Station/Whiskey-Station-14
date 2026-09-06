// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.WhiteDream.BloodCult;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

[TestFixture]
[TestOf(typeof(BloodCultDamage))]
public sealed class BloodCultDamageTest
{
    [Test]
    public void RitualDamageDoesNotCreateWoundsOrMutatePrototypeValue()
    {
        var original = new DamageSpecifier
        {
            DamageDict = new()
            {
                { "Slash", 15 },
                { "Bloodloss", -5 },
            },
        };

        var ritualDamage = BloodCultDamage.WithoutWounds(original);
        ProtoId<DamageTypePrototype> slash = "Slash";
        ProtoId<DamageTypePrototype> bloodloss = "Bloodloss";

        Assert.Multiple(() =>
        {
            Assert.That(ritualDamage.DamageDict, Is.EqualTo(original.DamageDict));
            Assert.That(ritualDamage.WoundSeverityMultipliers[slash], Is.EqualTo(FixedPoint2.Zero));
            Assert.That(ritualDamage.WoundSeverityMultipliers.ContainsKey(bloodloss), Is.False);
            Assert.That(original.WoundSeverityMultipliers, Is.Empty);
        });
    }
}
