// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

[RegisterComponent]
internal sealed partial class VodkaRuntimeStateComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public ulong NextSeed = 1;
    public readonly Dictionary<DwaineProcessId, VodkaActiveScript> ActiveScripts = [];
    public readonly Dictionary<DwaineProcessId, VodkaCapturedOutput> CapturedOutput = [];
}
