// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Syscalls;

/// <summary>
/// Bounded server-side syscall dispatcher configuration. Requests are never accepted directly from a client.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineSyscallComponent : Component
{
    public const int HardMaxArguments = 32;
    public const int HardMaxArgumentCharacters = 16384;
    public const int HardMaxResultCharacters = 65536;

    [DataField]
    public int MaxArguments = 16;

    [DataField]
    public int MaxArgumentCharacters = 8192;

    [DataField]
    public int MaxResultCharacters = 16384;
}
