// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines
// O LEGAL.md deles licencia como AGPL-3.0 tudo que entrou depois do commit
// 87c70a8, de 2024-02-17. O sistema de humor entrou em 2024-08-20.

using Robust.Shared.Prototypes;

namespace Content.Shared._EinsteinEngines.Mood;

/// <summary>
///     A prototype defining a category for moodlets, where only a single moodlet of a given category is permitted.
/// </summary>
[Prototype]
public sealed partial class MoodCategoryPrototype : IPrototype
{
    // Whiskey: este engine exige setter no ID de prototype, o RA0020 reprova sem.
    [IdDataField]
    public string ID { get; private set; } = default!;
}
