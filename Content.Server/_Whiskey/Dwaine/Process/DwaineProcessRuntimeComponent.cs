// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.Dwaine.Process;

[RegisterComponent]
public sealed partial class DwaineProcessRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public ulong NextProcessId = 1;
    internal readonly Dictionary<DwaineProcessId, DwaineProcessRecord> Processes = new();
    internal readonly Dictionary<DwaineProcessOwner, int> ActiveByOwner = new();
    internal readonly Queue<DwaineProcessId> ReadyQueue = new();
    internal readonly HashSet<DwaineProcessId> Queued = new();
    internal readonly Queue<DwaineProcessId> CompletedOrder = new();
    internal int CompletedProcessCount;
}

internal sealed class DwaineProcessRecord
{
    public readonly DwaineProcessId Id;
    public readonly DwaineProcessId? ParentId;
    public readonly DwaineProcessOwner Owner;
    public readonly DwaineProgramDescriptor Program;
    public readonly IDwaineProcessProgram Implementation;
    public readonly TimeSpan StartedAt;
    public readonly DwaineWorkingDirectoryHandle WorkingDirectory;
    public readonly DwaineProcessEnvironment Environment;
    public readonly DwaineProcessTextStream Stdin;
    public readonly DwaineProcessTextStream Stdout;
    public readonly DwaineProcessTextStream Stderr;
    public readonly DwaineProcessMailbox Mailbox;
    public readonly HashSet<DwaineProcessId> Children = new();

    public DwaineProcessState State = DwaineProcessState.Created;
    public DwaineProcessState ResumeState = DwaineProcessState.Ready;
    public DwaineProcessId? WaitingFor;
    public DwaineProcessResult? LastWaitResult;
    public long InstructionsConsumed;
    public int? ExitCode;
    public DwaineProcessExitReason? ExitReason;
    public string ErrorCode = string.Empty;
    public TimeSpan? CompletedAt;

    public bool IsTerminal => State is DwaineProcessState.Exited or DwaineProcessState.Faulted;

    public DwaineProcessRecord(
        DwaineProcessId id,
        DwaineProcessId? parentId,
        DwaineProcessOwner owner,
        DwaineProgramDescriptor program,
        IDwaineProcessProgram implementation,
        TimeSpan startedAt,
        DwaineWorkingDirectoryHandle workingDirectory,
        DwaineProcessEnvironment environment,
        DwaineProcessTextStream stdin,
        DwaineProcessTextStream stdout,
        DwaineProcessTextStream stderr,
        DwaineProcessMailbox mailbox)
    {
        Id = id;
        ParentId = parentId;
        Owner = owner;
        Program = program;
        Implementation = implementation;
        StartedAt = startedAt;
        WorkingDirectory = workingDirectory;
        Environment = environment;
        Stdin = stdin;
        Stdout = stdout;
        Stderr = stderr;
        Mailbox = mailbox;
    }

    public DwaineProcessSnapshot Snapshot()
    {
        return new DwaineProcessSnapshot(
            Id,
            ParentId,
            Owner,
            State,
            Program,
            StartedAt,
            ExitCode,
            ExitReason,
            ErrorCode,
            WorkingDirectory,
            InstructionsConsumed,
            Children.Count,
            WaitingFor);
    }

    public DwaineProcessResult Result()
    {
        return new DwaineProcessResult(
            Id,
            State,
            ExitCode ?? -1,
            ExitReason ?? DwaineProcessExitReason.Fault,
            ErrorCode);
    }
}

[ByRefEvent]
public readonly record struct DwaineProcessStateChangedEvent(
    DwaineProcessId ProcessId,
    DwaineProcessState Previous,
    DwaineProcessState Current,
    ulong BootGeneration);
