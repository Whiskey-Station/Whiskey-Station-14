// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._EinsteinEngines.Mood;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Whiskey.EntityEffects;

/// <summary>
/// Levanta um modificador de humor em quem tiver humor. Não faz nada em quem
/// não tem <c>MoodComponent</c>, e isso é de propósito: aqui humor é de quem
/// escolheu o traço, não de todo mundo.
/// </summary>
/// <inheritdoc cref="EntityEffect"/>
public sealed partial class AdjustMood : EntityEffectBase<AdjustMood>
{
    /// <summary>
    /// Qual modificador levantar. O peso, a duração e a categoria são
    /// propriedades dele, no YAML, e não daqui: assim balancear é mexer em
    /// prototype e não em código.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<MoodEffectPrototype> Effect;

    /// <summary>
    /// Multiplica o peso do modificador. Só vale para modificador SEM
    /// categoria: com categoria, o sistema de humor ignora, porque ali quem
    /// manda é a substituição por categoria.
    /// </summary>
    [DataField]
    public float Modifier = 1f;

    /// <summary>
    /// Soma ao peso depois da multiplicação. Mesma ressalva do
    /// <see cref="Modifier"/>.
    /// </summary>
    [DataField]
    public float Offset;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-adjust-mood", ("chance", Probability));
}
