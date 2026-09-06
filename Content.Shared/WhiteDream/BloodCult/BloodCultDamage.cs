// SPDX-License-Identifier: AGPL-3.0-or-later
// Blood Cult: ported from WWhiteDreamProject/wwdpublic. See Content.Shared/WhiteDream/BloodCult/ATTRIBUTION.md

using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared.WhiteDream.BloodCult;

public static class BloodCultDamage
{
    /// <summary>
    ///     Copies a ritual blood cost while preventing it from creating Trauma wounds.
    /// </summary>
    public static DamageSpecifier WithoutWounds(DamageSpecifier damage)
    {
        var result = new DamageSpecifier(damage);

        // Whiskey - ritual costs should hurt the cultist without breaking or dismembering body parts.
        foreach (var (type, amount) in result.DamageDict)
        {
            if (amount > FixedPoint2.Zero)
                result.WoundSeverityMultipliers[type] = FixedPoint2.Zero;
        }

        return result;
    }
}
