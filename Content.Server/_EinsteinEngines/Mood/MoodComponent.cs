// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines
// O LEGAL.md deles licencia como AGPL-3.0 tudo que entrou depois do commit
// 87c70a8, de 2024-02-17. O sistema de humor entrou em 2024-08-20.

using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;

namespace Content.Server._EinsteinEngines.Mood;

[RegisterComponent]
public sealed partial class MoodComponent : Component
{
    [DataField]
    public float CurrentMoodLevel;

    [DataField]
    public MoodThreshold CurrentMoodThreshold;

    [DataField]
    public MoodThreshold LastThreshold;

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly Dictionary<string, string> CategorisedEffects = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public readonly Dictionary<string, float> UncategorisedEffects = new();

    /// <summary>
    ///     The formula for the movement speed modifier is SpeedBonusGrowth ^ (MoodLevel - MoodThreshold.Neutral).
    ///     Change this ONLY BY 0.001 AT A TIME.
    /// </summary>
    [DataField]
    public float SpeedBonusGrowth = 1.003f;

    /// <summary>
    ///     The lowest point that low morale can multiply our movement speed by. Lowering speed follows a linear curve, rather than geometric.
    /// </summary>
    [DataField]
    public float MinimumSpeedModifier = 0.75f;

    /// <summary>
    ///     The maximum amount that high morale can multiply our movement speed by. This follows a significantly slower geometric sequence.
    /// </summary>
    [DataField]
    public float MaximumSpeedModifier = 1.15f;

    [DataField]
    public float IncreaseCritThreshold = 1.2f;

    [DataField]
    public float DecreaseCritThreshold = 0.9f;

    [ViewVariables(VVAccess.ReadOnly)]
    public FixedPoint2 CritThresholdBeforeModify;

    [DataField]
    public ProtoId<AlertCategoryPrototype> MoodCategory = "Mood";

    /// <summary>
    ///     Teto de cada faixa, e não piso: o <c>GetMoodThreshold</c> escolhe o
    ///     menor limiar que ainda é maior ou igual ao humor.
    /// </summary>
    /// <remarks>
    ///     Whiskey: estes números são os do /tg/station, e não os do Einstein.
    ///
    ///     O Einstein copiou de lá os pesos dos modificadores e não copiou as
    ///     faixas. Dá para conferir sem sair do repositório: os pesos usados
    ///     pelos prototypes deste mesmo porte são -20, -15, -7, -3, 0, +3, +7,
    ///     +10 e +15, que são um a um os defines de <c>MOOD_SAD4</c> até
    ///     <c>MOOD_HAPPY4</c> em <c>code/__DEFINES/mood.dm</c>. Números
    ///     calibrados para uma escala que vai de -20 a +15 caíram numa escala de
    ///     0 a 100 com faixa de dez em dez.
    ///
    ///     O efeito em jogo é que quase nada atravessa faixa. Um -7, que no TG é
    ///     um estado claramente sentido, deixava a pessoa em Neutro com 43 de
    ///     humor, e ferimento nenhum passava de Ruim, porque os quatro
    ///     modificadores de saúde são da mesma categoria e se substituem em vez
    ///     de somar. Na prática existiam três faixas, não dez.
    ///
    ///     Aqui cada faixa vale o neutro mais o define correspondente, então a
    ///     escada volta a bater com os pesos que já estão escritos no YAML.
    /// </remarks>
    [DataField(customTypeSerializer: typeof(DictionarySerializer<MoodThreshold, float>))]
    public Dictionary<MoodThreshold, float> MoodThresholds = new()
    {
        { MoodThreshold.Perfect, 65f },      // MOOD_HAPPY4, +15
        { MoodThreshold.Exceptional, 60f },  // MOOD_HAPPY3, +10
        { MoodThreshold.Great, 56f },        // MOOD_HAPPY2, +6
        { MoodThreshold.Good, 52f },         // MOOD_HAPPY1, +2
        { MoodThreshold.Neutral, 50f },      // MOOD_NEUTRAL, 0
        { MoodThreshold.Meh, 47f },          // MOOD_SAD1, -3
        { MoodThreshold.Bad, 43f },          // MOOD_SAD2, -7
        { MoodThreshold.Terrible, 35f },     // MOOD_SAD3, -15
        { MoodThreshold.Horrible, 30f },     // MOOD_SAD4, -20
        { MoodThreshold.Dead, 0f }           // morte, e só ela: o efeito Dead pesa -1000
    };

    [DataField(customTypeSerializer: typeof(DictionarySerializer<MoodThreshold, ProtoId<AlertPrototype>>))]
    public Dictionary<MoodThreshold, ProtoId<AlertPrototype>> MoodThresholdsAlerts = new()
    {
        { MoodThreshold.Dead, "MoodDead" },
        { MoodThreshold.Horrible, "Horrible" },
        { MoodThreshold.Terrible, "Terrible" },
        { MoodThreshold.Bad, "Bad" },
        { MoodThreshold.Meh, "Meh" },
        { MoodThreshold.Neutral, "Neutral" },
        { MoodThreshold.Good, "Good" },
        { MoodThreshold.Great, "Great" },
        { MoodThreshold.Exceptional, "Exceptional" },
        { MoodThreshold.Perfect, "Perfect" }
        // Whiskey: o Einstein listava aqui um alerta para MoodThreshold.Insane,
        // e o Insane nunca esteve no mapa de faixas acima, nem no deles. Ou
        // seja, aquele ícone era inalcançável desde sempre. No TG a insanidade
        // não é faixa de humor, é faixa de sanidade, que é um segundo número
        // que este porte não tem. O prototype do alerta continua existindo,
        // para quando a sanidade chegar.
    };

    /// <summary>
    ///     These thresholds represent a percentage of Crit-Threshold, 0.8 corresponding with 80%.
    /// </summary>
    [DataField(customTypeSerializer: typeof(DictionarySerializer<string, float>))]
    public Dictionary<string, float> HealthMoodEffectsThresholds = new()
    {
        { "HealthHeavyDamage", 0.8f },
        { "HealthSevereDamage", 0.5f },
        { "HealthLightDamage", 0.1f },
        { "HealthNoDamage", 0.05f }
    };
}

[Serializable]
public enum MoodThreshold : ushort
{
    Insane = 1,
    Horrible = 2,
    Terrible = 3,
    Bad = 4,
    Meh = 5,
    Neutral = 6,
    Good = 7,
    Great = 8,
    Exceptional = 9,
    Perfect = 10,
    Dead = 0
}
