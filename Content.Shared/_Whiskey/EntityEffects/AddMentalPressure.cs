// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Pressure;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Whiskey.EntityEffects;

/// <summary>
/// Acrescenta pressão mental de uma fonte.
/// </summary>
/// <remarks>
/// A porta de entrada do sistema, e é um efeito de entidade pelo mesmo motivo
/// do <see cref="AdjustMood"/>: assim química, comida, evento de estação e
/// qualquer outra coisa que já usa efeito conseguem empurrar pressão sem cada
/// assunto precisar do seu próprio sistema.
/// </remarks>
/// <inheritdoc cref="EntityEffect"/>
public sealed partial class AddMentalPressure : EntityEffectBase<AddMentalPressure>
{
    /// <summary>
    /// De onde vem a pressão. O peso, o teto e o decaimento são propriedades da
    /// fonte, no YAML, e não daqui.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<PressureSourcePrototype> Source;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-add-mental-pressure", ("chance", Probability));
}
