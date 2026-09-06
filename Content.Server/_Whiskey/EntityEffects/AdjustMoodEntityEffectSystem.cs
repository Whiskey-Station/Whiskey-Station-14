// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.EntityEffects;
using Content.Shared.EntityEffects;

namespace Content.Server._Whiskey.EntityEffects;

/// <summary>
/// Entrega o <see cref="AdjustMood"/>, levantando o modificador na pessoa.
/// </summary>
/// <remarks>
/// Fica no servidor porque o <see cref="MoodComponent"/> é do servidor, e é ele
/// que serve de porta: sem humor, o efeito nem é chamado. A base genérica só
/// exige <c>where T : Component</c>, então componente de servidor vale.
/// </remarks>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class AdjustMoodEntityEffectSystem : EntityEffectSystem<MoodComponent, AdjustMood>
{
    protected override void Effect(Entity<MoodComponent> entity, ref EntityEffectEvent<AdjustMood> args)
    {
        // O MoodSystem escuta este evento na própria entidade e resolve o
        // prototype de lá. Levantar o evento em vez de mexer no componente
        // direto mantém um caminho só para tudo que mexe em humor, e é o mesmo
        // que o dano e os traços periódicos já usam.
        RaiseLocalEvent(
            entity.Owner,
            new MoodEffectEvent(args.Effect.Effect, args.Effect.Modifier, args.Effect.Offset));
    }
}
