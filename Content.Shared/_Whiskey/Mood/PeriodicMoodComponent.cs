// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Whiskey.Mood;

/// <summary>
/// Derruba o humor sozinho, em hora imprevisível, sem gatilho no ambiente. É o
/// que a depressão usa, mas serve para química e evento, e por isso não se
/// chama Depressão.
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
    /// Onde a frase aparece: popup em cima do personagem quando ligado, linha
    /// de chat quando desligado. **Um ou outro, nunca os dois.**
    ///
    /// Desligado por padrão, e a escolha tem motivo. O popup do SS14 dura
    /// 0,04 segundo por caractere, com teto de 5, então um pensamento de 50
    /// caracteres pisca em 2 segundos. Isso serve para susto, que é o caso das
    /// vozes da esquizofrenia, e não serve para pensamento que a pessoa
    /// deveria absorver.
    ///
    /// No chat ele fica, e a cor abaixo é o que impede dele se perder no meio
    /// dos comunicados da estação.
    /// </summary>
    [DataField]
    public bool Popup;

    /// <summary>
    /// Cor da linha no chat. Existe para o pensamento não se confundir com
    /// comunicado da estação nem com fala de outra pessoa.
    /// </summary>
    [DataField]
    public Color MessageColor = Color.FromHex("#9A8CA8");

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
