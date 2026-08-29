// SPDX-FileCopyrightText: 2024-2026 Simple Station
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Portado de https://github.com/Simple-Station/Einstein-Engines
// O LEGAL.md deles licencia como AGPL-3.0 tudo que entrou depois do commit
// 87c70a8, de 2024-02-17. O sistema de humor entrou em 2024-08-20.

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /*
        * Mood System
        */

    public static readonly CVarDef<bool> MoodEnabled =
#if RELEASE
        CVarDef.Create("mood.enabled", true, CVar.SERVER);
#else
        CVarDef.Create("mood.enabled", false, CVar.SERVER);
#endif

    public static readonly CVarDef<bool> MoodIncreasesSpeed =
        CVarDef.Create("mood.increases_speed", true, CVar.SERVER);

    public static readonly CVarDef<bool> MoodDecreasesSpeed =
        CVarDef.Create("mood.decreases_speed", true, CVar.SERVER);

    public static readonly CVarDef<bool> MoodModifiesThresholds =
        CVarDef.Create("mood.modify_thresholds", false, CVar.SERVER);

    public static readonly CVarDef<bool> MoodVisualEffects =
        CVarDef.Create("mood.visual_effects", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
