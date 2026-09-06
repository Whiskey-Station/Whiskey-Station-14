// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Transport;

/// <summary>
/// Physical mainframe endpoint configuration. Kernel and boot state are intentionally absent.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineMainframeComponent : Component
{
    public const int HardMaxSessions = 128;
    public const int HardMaxOutputLines = 512;
    public const int HardMaxOutputCharacters = 131072;

    [DataField]
    public int MaxSessions = 32;

    [DataField]
    public int OutputLineLimit = 128;

    [DataField]
    public int OutputCharacterLimit = 16384;
}
