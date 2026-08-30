// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Process;

/// <summary>
/// Authoritative process lifecycle states. Runtime records and identifiers remain server-only.
/// </summary>
public enum DwaineProcessState : byte
{
    Created,
    Ready,
    Running,
    Waiting,
    Stopped,
    Exited,
    Faulted,
}

/// <summary>
/// Per-mainframe scheduler and resource limits. Every data field is clamped to a hard server limit.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineProcessSchedulerComponent : Component
{
    public const int HardMaxProcesses = 1024;
    public const int HardMaxProcessesPerOwner = 256;
    public const int HardMaxDispatchesPerUpdate = 1024;
    public const int HardMaxInstructionsPerSlice = 100_000;
    public const long HardMaxInstructionsPerProcess = 10_000_000;
    public const int HardMaxStreamChunks = 256;
    public const int HardMaxStreamCharacters = 65_536;
    public const int HardMaxMailboxMessages = 256;
    public const int HardMaxMailboxCharacters = 65_536;
    public const int HardMaxEnvironmentEntries = 128;
    public const int HardMaxEnvironmentCharacters = 16_384;
    public const int HardMaxCompletedProcesses = 1024;
    public const float HardMaxCompletedRetentionSeconds = 300f;

    [DataField]
    public int MaxProcesses = 256;

    [DataField]
    public int MaxProcessesPerOwner = 32;

    [DataField]
    public int MaxDispatchesPerUpdate = 64;

    [DataField]
    public int InstructionsPerSlice = 1024;

    [DataField]
    public long InstructionsPerProcess = 1_000_000;

    [DataField]
    public int StreamChunkLimit = 128;

    [DataField]
    public int StreamCharacterLimit = 16_384;

    [DataField]
    public int MailboxMessageLimit = 64;

    [DataField]
    public int MailboxCharacterLimit = 16_384;

    [DataField]
    public int EnvironmentEntryLimit = 64;

    [DataField]
    public int EnvironmentCharacterLimit = 8192;

    [DataField]
    public int CompletedProcessLimit = 256;

    [DataField]
    public float CompletedRetentionSeconds = 30f;
}
