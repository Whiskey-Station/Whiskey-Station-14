// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Pressure;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Whiskey.Pressure;

/// <summary>
/// Levar uma pancada forte pesa na cabeça, e não só no corpo.
/// </summary>
/// <remarks>
/// Este gatilho é de propósito o mais barato dos quatro: ele escuta o evento de
/// dano <b>só em quem tem MentalPressureComponent</b>, então o custo é zero para
/// a estação inteira e existe apenas para quem tem o traço.
///
/// Escuro e solidão, que são as fontes que faltam, precisariam varrer o mapa de
/// tempos em tempos, e é justamente isso que derrubou o desempenho do fork de
/// SCP quando eles ligaram medo com cone de visão. Elas ficam para depois, com
/// medida antes.
/// </remarks>
public sealed partial class PainPressureSystem : EntitySystem
{
    /// <summary>
    /// Dano de uma vez que conta como pancada.
    /// </summary>
    /// <remarks>
    /// Quinze é um golpe de verdade e não um arranhão: fica acima de um soco,
    /// que é 5, e abaixo de um tiro de pistola. Sem esse piso, comer comida
    /// quente ou esbarrar numa grade viraria trauma.
    /// </remarks>
    private const float Pancada = 15f;

    private static readonly ProtoId<PressureSourcePrototype> Fonte = "WhiskeyPressaoDor";

    [Dependency] private MentalPressureSystem _pressao = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MentalPressureComponent, DamageChangedEvent>(OnDano);
    }

    private void OnDano(Entity<MentalPressureComponent> ent, ref DamageChangedEvent args)
    {
        // DamageDelta nulo é recálculo, não pancada nova. E cura não assusta.
        if (args.DamageDelta is not { } delta || !args.DamageIncreased)
            return;

        if (delta.GetTotal().Float() < Pancada)
            return;

        _pressao.Adicionar(ent.Owner, Fonte);
    }
}
