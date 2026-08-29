// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Whiskey.Mood;

/// <summary>
/// Derruba o humor de tempos em tempos, sozinho, sem gatilho no ambiente.
///
/// É o que a depressão usa: o baque não vem de fora, vem de dentro, e em hora
/// imprevisível. Serve igual para química ou evento que precise disso, e por
/// isso não se chama Depressão.
///
/// Antes isto empurrava uma medida de estresse própria, escrita aqui enquanto
/// o fork não tinha humor. Com o humor do Einstein portado, o estresse saiu e
/// este componente passou a levantar um modificador de humor de verdade, que é
/// o que faz o efeito aparecer no alerta, na velocidade e na saturação da tela.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class PeriodicMoodComponent : Component
{
    /// <summary>
    /// Modificador de humor levantado a cada episódio. O quanto ele pesa e
    /// quanto tempo dura são propriedades do modificador, não daqui.
    /// </summary>
    [DataField(required: true)]
    public string Effect = default!;

    /// <summary>
    /// Menor espera entre episódios, em segundos.
    /// </summary>
    [DataField]
    public float MinTimeBetween = 240f;

    /// <summary>
    /// Maior espera entre episódios, em segundos. O intervalo é largo de
    /// propósito: episódio em hora previsível vira relógio, e a pessoa passa a
    /// planejar em volta dele em vez de ser pega por ele.
    /// </summary>
    [DataField]
    public float MaxTimeBetween = 900f;

    /// <summary>
    /// Frases que a pessoa lê quando o episódio bate. Opcional: sem isto o
    /// episódio é silencioso e só se percebe pelo humor caindo.
    ///
    /// É conjunto e não frase única de propósito: com uma frase só ela repete
    /// toda vez e a pessoa para de ler depois da terceira.
    /// </summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype>? Messages;

    /// <summary>
    /// Aviso mostrado uma vez, quando a pessoa ganha isto. Serve para deixar
    /// claro que é interpretação de papel, que é o que o TG faz com a quirk
    /// equivalente.
    /// </summary>
    [DataField]
    public string? GainMessage;

    /// <summary>
    /// Quando vem o próximo. Pausa junto com a entidade, senão a rodada volta
    /// de uma pausa com todos os episódios atrasados disparando de uma vez.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextEpisode;
}
