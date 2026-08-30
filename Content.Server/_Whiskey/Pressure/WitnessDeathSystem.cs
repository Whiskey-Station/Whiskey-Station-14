// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Pressure;
using Content.Shared.Examine;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Robust.Shared.Prototypes;

namespace Content.Server._Whiskey.Pressure;

/// <summary>
/// Ver alguém morrer pesa na cabeça de quem viu.
/// </summary>
/// <remarks>
/// O primeiro gatilho de pressão, e escolhido de propósito: é a coisa mais
/// pesada que acontece numa rodada normal, é fácil de reconhecer em jogo, e
/// serve de molde para os próximos.
///
/// Três condições, e cada uma existe para o efeito não virar ruído:
///
/// A morte precisa ser de alguém com mente. Bicho morrendo na Lavaland não
/// pode encher a estação inteira de pressão, e sem essa guarda encheria: a
/// Lavaland mata dezenas de goliath por rodada.
///
/// Quem viu precisa estar perto E com linha de visão. Só distância faria a
/// pressão atravessar parede, e parede é justamente o que separa quem viu de
/// quem só estava no mesmo corredor.
///
/// E quem morreu não sente a própria morte. Parece óbvio, e sem a guarda o
/// morto ganharia pressão de si mesmo.
/// </remarks>
public sealed partial class WitnessDeathSystem : EntitySystem
{
    /// <summary>
    /// Alcance da testemunha, em tiles. Sete é a distância em que dá para
    /// reconhecer o que aconteceu com alguém, e é a mesma ordem de grandeza do
    /// alcance de fala normal.
    /// </summary>
    private const float Alcance = 7f;

    private static readonly ProtoId<PressureSourcePrototype> Fonte = "WhiskeyPressaoMorte";

    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MentalPressureSystem _pressao = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        // Só morte de gente. Sem isto, a Lavaland sozinha encheria a estação de
        // pressão, porque ela mata dezenas de criaturas por rodada.
        if (!HasComp<MindContainerComponent>(args.Target))
            return;

        var consulta = EntityQueryEnumerator<MentalPressureComponent>();
        while (consulta.MoveNext(out var testemunha, out _))
        {
            if (testemunha == args.Target)
                continue;

            // Perto E vendo. Só a distância faria a pressão atravessar parede.
            if (!_examine.InRangeUnOccluded(testemunha, args.Target, Alcance))
                continue;

            _pressao.Adicionar(testemunha, Fonte);
        }
    }
}
