// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.Dwaine.Process;

public readonly record struct DwaineProcessId(ulong Value)
{
    public bool IsValid => Value != 0;

    public override string ToString()
    {
        return Value.ToString();
    }
}

/// <summary>
/// Server-side principal reference. PR 08 maps authenticated users to these opaque values.
/// </summary>
public readonly record struct DwaineProcessOwner(ulong Value)
{
    public static readonly DwaineProcessOwner System = new(0);
}

/// <summary>
/// Opaque reference to a VFS volume and directory node. The filesystem validates both values server-side.
/// </summary>
public readonly record struct DwaineWorkingDirectoryHandle(ulong Volume, ulong Node)
{
    public static readonly DwaineWorkingDirectoryHandle Root = new(1, 1);
    public bool IsValid => Volume != 0 && Node != 0;
}

public readonly record struct DwaineProgramDescriptor(string Id, string DisplayName);

public enum DwaineProcessExitReason : byte
{
    NormalExit,
    Killed,
    ParentExited,
    Fault,
    InstructionLimit,
    KernelShutdown,
}

public enum DwaineProcessControlResult : byte
{
    Success,
    MainframeUnavailable,
    ProcessNotFound,
    InvalidState,
    AccessDenied,
}

public enum DwaineProcessSpawnResult : byte
{
    Success,
    MainframeUnavailable,
    KernelNotReady,
    InvalidProgram,
    InvalidOwner,
    InvalidParent,
    InvalidWorkingDirectory,
    InvalidEnvironment,
    MainframeLimitReached,
    OwnerLimitReached,
    PidExhausted,
}

public enum DwaineProcessWaitStatus : byte
{
    Completed,
    Waiting,
    MainframeUnavailable,
    ProcessNotFound,
    NotAChild,
    InvalidState,
}

public enum DwaineProcessMessageResult : byte
{
    Success,
    MainframeUnavailable,
    ProcessNotFound,
    AccessDenied,
    MalformedMessage,
    MailboxFull,
}

public readonly record struct DwaineProcessResult(
    DwaineProcessId ProcessId,
    DwaineProcessState State,
    int ExitCode,
    DwaineProcessExitReason Reason,
    string ErrorCode);

public readonly record struct DwaineProcessSnapshot(
    DwaineProcessId ProcessId,
    DwaineProcessId? ParentId,
    DwaineProcessOwner Owner,
    DwaineProcessState State,
    DwaineProgramDescriptor Program,
    TimeSpan StartedAt,
    int? ExitCode,
    DwaineProcessExitReason? ExitReason,
    string ErrorCode,
    DwaineWorkingDirectoryHandle WorkingDirectory,
    long InstructionsConsumed,
    int ChildCount,
    DwaineProcessId? WaitingFor);

public sealed class DwaineProcessSpawnRequest
{
    public required DwaineProcessOwner Owner { get; init; }
    public required DwaineProgramDescriptor Program { get; init; }
    public required IDwaineProcessProgram Implementation { get; init; }
    public DwaineProcessId? ParentId { get; init; }
    public DwaineWorkingDirectoryHandle WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public bool InheritParentEnvironment { get; init; } = true;
}

internal readonly record struct DwaineProcessLimits(
    int MaxProcesses,
    int MaxProcessesPerOwner,
    int MaxDispatchesPerUpdate,
    int InstructionsPerSlice,
    long InstructionsPerProcess,
    int StreamChunkLimit,
    int StreamCharacterLimit,
    int MailboxMessageLimit,
    int MailboxCharacterLimit,
    int EnvironmentEntryLimit,
    int EnvironmentCharacterLimit,
    int CompletedProcessLimit,
    TimeSpan CompletedRetention)
{
    public static DwaineProcessLimits FromComponent(DwaineProcessSchedulerComponent component)
    {
        var retention = float.IsFinite(component.CompletedRetentionSeconds)
            ? component.CompletedRetentionSeconds
            : DwaineProcessSchedulerComponent.HardMaxCompletedRetentionSeconds;

        return new DwaineProcessLimits(
            Math.Clamp(component.MaxProcesses, 1, DwaineProcessSchedulerComponent.HardMaxProcesses),
            Math.Clamp(component.MaxProcessesPerOwner, 1, DwaineProcessSchedulerComponent.HardMaxProcessesPerOwner),
            Math.Clamp(component.MaxDispatchesPerUpdate, 1, DwaineProcessSchedulerComponent.HardMaxDispatchesPerUpdate),
            Math.Clamp(
                component.InstructionsPerSlice,
                DwaineProcessSchedulerComponent.MinimumInstructionsPerSlice,
                DwaineProcessSchedulerComponent.HardMaxInstructionsPerSlice),
            Math.Clamp(component.InstructionsPerProcess, 1, DwaineProcessSchedulerComponent.HardMaxInstructionsPerProcess),
            Math.Clamp(component.StreamChunkLimit, 1, DwaineProcessSchedulerComponent.HardMaxStreamChunks),
            Math.Clamp(component.StreamCharacterLimit, 1, DwaineProcessSchedulerComponent.HardMaxStreamCharacters),
            Math.Clamp(component.MailboxMessageLimit, 1, DwaineProcessSchedulerComponent.HardMaxMailboxMessages),
            Math.Clamp(component.MailboxCharacterLimit, 1, DwaineProcessSchedulerComponent.HardMaxMailboxCharacters),
            Math.Clamp(component.EnvironmentEntryLimit, 1, DwaineProcessSchedulerComponent.HardMaxEnvironmentEntries),
            Math.Clamp(component.EnvironmentCharacterLimit, 1, DwaineProcessSchedulerComponent.HardMaxEnvironmentCharacters),
            Math.Clamp(component.CompletedProcessLimit, 1, DwaineProcessSchedulerComponent.HardMaxCompletedProcesses),
            TimeSpan.FromSeconds(Math.Clamp(
                retention,
                0f,
                DwaineProcessSchedulerComponent.HardMaxCompletedRetentionSeconds)));
    }
}
