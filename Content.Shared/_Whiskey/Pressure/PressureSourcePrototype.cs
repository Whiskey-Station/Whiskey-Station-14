// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Whiskey.Pressure;

/// <summary>
/// Um tipo de coisa que pesa na cabeça de alguém.
/// </summary>
/// <remarks>
/// Fica em prototype e não em código porque balancear pressão mental é ajustar
/// número e escrever texto, e nenhuma das duas coisas devia exigir recompilar.
/// É a mesma decisão que o Einstein tomou com os modificadores de humor, e
/// funcionou.
/// </remarks>
// Sem o nome no atributo: o analisador RA0042 reprova quando o nome explícito é
// igual ao que ele geraria sozinho, e "pressureSource" é exatamente o que ele
// deriva de PressureSourcePrototype.
[Prototype]
public sealed partial class PressureSourcePrototype : IPrototype
{
    // Este engine exige setter no ID de prototype, o analisador RA0020 reprova
    // sem, e ele só reprova em Release.
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Quanto esta fonte acrescenta de cada vez que é levantada.
    /// </summary>
    [DataField]
    public float Weight = 10f;

    /// <summary>
    /// Teto DESTA fonte sozinha.
    ///
    /// Separado do teto geral de propósito: uma fonte fraca e constante, tipo o
    /// escuro, tem que empurrar até certo ponto e parar. Sem isto, qualquer
    /// coisa repetida o suficiente satura a pessoa e a origem deixa de
    /// significar alguma coisa.
    /// </summary>
    [DataField]
    public float Cap = 40f;

    /// <summary>
    /// Quanto esta fonte perde a cada rodada de decaimento.
    ///
    /// É por fonte, e não global, porque as coisas saem da cabeça em ritmos
    /// diferentes: susto passa rápido, ter visto alguém morrer não.
    /// </summary>
    [DataField]
    public float Decay = 1f;

    /// <summary>
    /// O que o Psicólogo lê ao examinar alguém sob esta pressão.
    ///
    /// Esta é a razão de o sistema existir. Se fosse só um número, ninguém de
    /// fora conseguiria perceber, entender nem ajudar, e o cargo continuaria
    /// sendo um dispensador de comprimido.
    /// </summary>
    [DataField(required: true)]
    public LocId Description;
}
