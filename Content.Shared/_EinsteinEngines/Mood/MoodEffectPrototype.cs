// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines
// O LEGAL.md deles licencia como AGPL-3.0 tudo que entrou depois do commit
// 87c70a8, de 2024-02-17. O sistema de humor entrou em 2024-08-20.

using Robust.Shared.Prototypes;

namespace Content.Shared._EinsteinEngines.Mood;

[Prototype]
public sealed partial class MoodEffectPrototype : IPrototype
{
    /// <summary>
    ///     The ID of the moodlet to use.
    /// </summary>
    // Whiskey: este engine exige setter no ID de prototype, o RA0020 reprova sem.
    [IdDataField]
    public string ID { get; private set; } = default!;

    public string Description => Loc.GetString($"mood-effect-{ID}");

    /// <summary>
    ///     If they already have an effect with the same category, the new one will replace the old one.
    /// </summary>
    // Whiskey: o ValidatePrototypeId foi aposentado neste engine, e quem faz
    // a mesma coisa hoje é o ProtoId, que já valida na desserialização.
    [DataField]
    public ProtoId<MoodCategoryPrototype>? Category;

    /// <summary>
    ///     How much should this moodlet modify an entity's Mood.
    /// </summary>
    [DataField(required: true)]
    public float MoodChange;

    /// <summary>
    ///     How long, in Seconds, does this moodlet last? If omitted, the moodlet will last until canceled by any system.
    /// </summary>
    [DataField]
    public int Timeout;

    /// <summary>
    ///     Should this moodlet be hidden from the player? EG: No popups or chat messages.
    /// </summary>
    [DataField]
    public bool Hidden;

    /// <summary>
    ///     When not null, this moodlet will replace itself with another Moodlet upon expiration.
    /// </summary>
    [DataField]
    public ProtoId<MoodEffectPrototype>? MoodletOnEnd;
}
