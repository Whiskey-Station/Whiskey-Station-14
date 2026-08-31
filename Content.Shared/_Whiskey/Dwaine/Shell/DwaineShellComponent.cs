// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Shell;

/// <summary>
/// Server-clamped interactive shell limits. Session state and command contents are server-only.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineShellComponent : Component
{
    public const int HardMaxInputLength = 4096;
    public const int HardMaxTokens = 256;
    public const int HardMaxPipelineStages = 16;
    public const int HardMaxCommands = 64;
    public const int HardMaxHistoryEntries = 256;
    public const int MinimumEnvironmentEntries = 3;
    public const int HardMaxEnvironmentEntries = 128;
    public const int MinimumEnvironmentCharacters = 128;
    public const int HardMaxEnvironmentCharacters = 65_536;
    public const int HardMaxOutputCharacters = 65_536;
    public const int HardMaxEvaluationDepth = 8;
    public const int HardMaxLoopIterations = 64;

    [DataField]
    public int MaxInputLength = 2048;

    [DataField]
    public int MaxTokens = 128;

    [DataField]
    public int MaxPipelineStages = 8;

    [DataField]
    public int MaxCommands = 32;

    [DataField]
    public int MaxHistoryEntries = 64;

    [DataField]
    public int MaxEnvironmentEntries = 64;

    [DataField]
    public int MaxEnvironmentCharacters = 8192;

    [DataField]
    public int MaxOutputCharacters = 16_384;

    [DataField]
    public int MaxEvaluationDepth = 4;

    [DataField]
    public int MaxLoopIterations = 32;
}
