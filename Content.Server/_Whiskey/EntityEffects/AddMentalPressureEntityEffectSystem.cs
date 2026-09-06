// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Pressure;
using Content.Shared._Whiskey.EntityEffects;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.EntityEffects;

namespace Content.Server._Whiskey.EntityEffects;

/// <summary>
/// Entrega o <see cref="AddMentalPressure"/>.
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class AddMentalPressureEntityEffectSystem
    : EntityEffectSystem<MentalPressureComponent, AddMentalPressure>
{
    [Dependency] private MentalPressureSystem _pressao = default!;

    protected override void Effect(Entity<MentalPressureComponent> entity, ref EntityEffectEvent<AddMentalPressure> args)
    {
        _pressao.Adicionar(entity.Owner, args.Effect.Source);
    }
}
