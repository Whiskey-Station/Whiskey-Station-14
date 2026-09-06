// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Process;

/// <summary>
/// Owns per-mainframe process tables, fair bounded scheduling, streams, waits, cancellation and IPC.
/// Programs execute synchronously one logical step at a time and never become background Tasks.
/// </summary>
public sealed partial class DwaineProcessSystem : EntitySystem
{
    public const int HardMaxProgramIdLength = 64;
    public const int HardMaxProgramDisplayNameLength = 96;
    public const int HardMaxErrorCodeLength = 48;

    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineFileSystemSystem _fileSystem = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _scheduledMainframes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineProcessSchedulerComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineProcessRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        foreach (var mainframe in _scheduledMainframes.ToArray())
        {
            if (TerminatingOrDeleted(mainframe)
                || !TryComp<DwaineProcessSchedulerComponent>(mainframe, out var config)
                || !TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime)
                || !runtime.Online
                || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady)
            {
                _scheduledMainframes.Remove(mainframe);
                continue;
            }

            var limits = DwaineProcessLimits.FromComponent(config);
            PruneCompleted(mainframe, runtime, limits, now);

            // Snapshotting the queue length guarantees at most one dispatch per process per update.
            var dispatches = Math.Min(limits.MaxDispatchesPerUpdate, runtime.ReadyQueue.Count);
            for (var index = 0; index < dispatches; index++)
            {
                if (!TryDequeueReady(runtime, out var process))
                    break;

                Dispatch(mainframe, runtime, process, limits, now);
            }

            if (runtime.ReadyQueue.Count == 0)
                _scheduledMainframes.Remove(mainframe);
        }
    }

    private void OnKernelReady(Entity<DwaineProcessSchedulerComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineProcessRuntimeComponent>(ent, out var runtime))
            return;

        if (runtime.Online || runtime.Processes.Count > 0)
            ShutdownRuntime(ent.Owner, runtime, DwaineProcessExitReason.KernelShutdown, true);

        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (_kernel.TryRegisterService(
                ent.Owner,
                "process-runtime",
                new ProcessKernelService(this, ent.Owner, args.BootGeneration)))
        {
            return;
        }

        runtime.Online = false;
        _kernel.Panic(ent.Owner, "process-service-registration");
    }

    private void OnRuntimeShutdown(Entity<DwaineProcessRuntimeComponent> ent, ref ComponentShutdown args)
    {
        ShutdownRuntime(ent.Owner, ent.Comp, DwaineProcessExitReason.KernelShutdown, false);
    }

    public DwaineProcessSpawnResult TrySpawn(
        EntityUid mainframe,
        DwaineProcessSpawnRequest request,
        out DwaineProcessId processId)
    {
        processId = default;
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineProcessSchedulerComponent>(mainframe, out var config)
            || !TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime))
        {
            return DwaineProcessSpawnResult.MainframeUnavailable;
        }

        if (!runtime.Online || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady)
            return DwaineProcessSpawnResult.KernelNotReady;

        if (request is null
            || request.Implementation is null
            || !IsValidProgramDescriptor(request.Program))
        {
            return DwaineProcessSpawnResult.InvalidProgram;
        }

        var limits = DwaineProcessLimits.FromComponent(config);
        PruneCompleted(mainframe, runtime, limits, _timing.CurTime);

        DwaineProcessRecord? parent = null;
        if (request.ParentId is { } parentId)
        {
            if (!runtime.Processes.TryGetValue(parentId, out parent)
                || parent.IsTerminal
                || parent.Owner != request.Owner)
            {
                return DwaineProcessSpawnResult.InvalidParent;
            }
        }

        var workingDirectory = request.WorkingDirectory.IsValid
            ? request.WorkingDirectory
            : parent?.WorkingDirectory ?? DwaineWorkingDirectoryHandle.Root;
        if (!_fileSystem.IsDirectory(mainframe, workingDirectory))
            return DwaineProcessSpawnResult.InvalidWorkingDirectory;

        if (GetActiveProcessCount(runtime) >= limits.MaxProcesses)
            return DwaineProcessSpawnResult.MainframeLimitReached;

        if (runtime.ActiveByOwner.GetValueOrDefault(request.Owner) >= limits.MaxProcessesPerOwner)
            return DwaineProcessSpawnResult.OwnerLimitReached;

        var environment = parent is not null && request.InheritParentEnvironment
            ? parent.Environment.Clone()
            : new DwaineProcessEnvironment(limits.EnvironmentEntryLimit, limits.EnvironmentCharacterLimit);
        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                if (!environment.TrySet(name, value))
                    return DwaineProcessSpawnResult.InvalidEnvironment;
            }
        }

        if (!TryAllocateProcessId(runtime, out processId))
            return DwaineProcessSpawnResult.PidExhausted;

        var process = new DwaineProcessRecord(
            processId,
            request.ParentId,
            request.Owner,
            request.Program,
            request.Implementation,
            _timing.CurTime,
            workingDirectory,
            request.TerminalSession is { IsValid: true } explicitSession
                ? explicitSession
                : parent?.TerminalSession,
            environment,
            new DwaineProcessTextStream(limits.StreamChunkLimit, limits.StreamCharacterLimit),
            new DwaineProcessTextStream(limits.StreamChunkLimit, limits.StreamCharacterLimit),
            new DwaineProcessTextStream(limits.StreamChunkLimit, limits.StreamCharacterLimit),
            new DwaineProcessMailbox(limits.MailboxMessageLimit, limits.MailboxCharacterLimit));

        runtime.Processes.Add(processId, process);
        runtime.ActiveByOwner[request.Owner] = runtime.ActiveByOwner.GetValueOrDefault(request.Owner) + 1;
        parent?.Children.Add(processId);
        SetState(mainframe, runtime, process, DwaineProcessState.Ready);
        EnqueueReady(mainframe, runtime, process);
        return DwaineProcessSpawnResult.Success;
    }

    public DwaineProcessControlResult TryExit(EntityUid mainframe, DwaineProcessId processId, int exitCode = 0)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessControlResult.MainframeUnavailable;
        if (!runtime.Processes.TryGetValue(processId, out var process))
            return DwaineProcessControlResult.ProcessNotFound;

        if (process.IsTerminal)
            return DwaineProcessControlResult.InvalidState;

        CompleteProcessTree(
            mainframe,
            runtime,
            process,
            DwaineProcessState.Exited,
            exitCode,
            DwaineProcessExitReason.NormalExit,
            string.Empty,
            _timing.CurTime);
        return DwaineProcessControlResult.Success;
    }

    public DwaineProcessControlResult TryKill(
        EntityUid mainframe,
        DwaineProcessId requesterId,
        DwaineProcessId targetId)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessControlResult.MainframeUnavailable;

        if (!runtime.Processes.TryGetValue(requesterId, out var requester)
            || requester.IsTerminal
            || !runtime.Processes.TryGetValue(targetId, out var target))
        {
            return DwaineProcessControlResult.ProcessNotFound;
        }

        if (target.IsTerminal)
            return DwaineProcessControlResult.InvalidState;

        if (!CanControl(requester, target))
            return DwaineProcessControlResult.AccessDenied;

        CompleteProcessTree(
            mainframe,
            runtime,
            target,
            DwaineProcessState.Exited,
            137,
            DwaineProcessExitReason.Killed,
            "killed",
            _timing.CurTime);
        return DwaineProcessControlResult.Success;
    }

    public DwaineProcessControlResult TryKillAsOwner(
        EntityUid mainframe,
        DwaineProcessOwner requester,
        DwaineProcessId targetId)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessControlResult.MainframeUnavailable;
        if (!runtime.Processes.TryGetValue(targetId, out var target))
            return DwaineProcessControlResult.ProcessNotFound;
        if (target.IsTerminal)
            return DwaineProcessControlResult.InvalidState;
        if (requester != DwaineProcessOwner.System && requester != target.Owner)
            return DwaineProcessControlResult.AccessDenied;

        CompleteProcessTree(
            mainframe,
            runtime,
            target,
            DwaineProcessState.Exited,
            137,
            DwaineProcessExitReason.Killed,
            "killed",
            _timing.CurTime);
        return DwaineProcessControlResult.Success;
    }

    public DwaineProcessControlResult TryStop(
        EntityUid mainframe,
        DwaineProcessId requesterId,
        DwaineProcessId targetId)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessControlResult.MainframeUnavailable;

        if (!runtime.Processes.TryGetValue(requesterId, out var requester)
            || requester.IsTerminal
            || !runtime.Processes.TryGetValue(targetId, out var target))
        {
            return DwaineProcessControlResult.ProcessNotFound;
        }

        if (!CanControl(requester, target))
            return DwaineProcessControlResult.AccessDenied;

        if (target.State is not (DwaineProcessState.Ready
            or DwaineProcessState.Running
            or DwaineProcessState.Waiting))
        {
            return DwaineProcessControlResult.InvalidState;
        }

        target.ResumeState = target.State == DwaineProcessState.Running
            ? DwaineProcessState.Ready
            : target.State;
        runtime.Queued.Remove(target.Id);
        SetState(mainframe, runtime, target, DwaineProcessState.Stopped);
        return DwaineProcessControlResult.Success;
    }

    public DwaineProcessControlResult TryContinue(
        EntityUid mainframe,
        DwaineProcessId requesterId,
        DwaineProcessId targetId)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessControlResult.MainframeUnavailable;

        if (!runtime.Processes.TryGetValue(requesterId, out var requester)
            || requester.IsTerminal
            || !runtime.Processes.TryGetValue(targetId, out var target))
        {
            return DwaineProcessControlResult.ProcessNotFound;
        }

        if (!CanControl(requester, target))
            return DwaineProcessControlResult.AccessDenied;

        if (target.State != DwaineProcessState.Stopped)
            return DwaineProcessControlResult.InvalidState;

        if (target.ResumeState == DwaineProcessState.Waiting
            && target.WaitingFor is { } childId
            && runtime.Processes.TryGetValue(childId, out var child))
        {
            if (!child.IsTerminal)
            {
                SetState(mainframe, runtime, target, DwaineProcessState.Waiting);
                return DwaineProcessControlResult.Success;
            }

            target.LastWaitResult = child.Result();
            target.WaitingFor = null;
            RemoveProcessRecord(mainframe, runtime, child);
        }

        SetState(mainframe, runtime, target, DwaineProcessState.Ready);
        EnqueueReady(mainframe, runtime, target);
        return DwaineProcessControlResult.Success;
    }

    public DwaineProcessWaitStatus TryWait(
        EntityUid mainframe,
        DwaineProcessId parentId,
        DwaineProcessId childId,
        out DwaineProcessResult result)
    {
        result = default;
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessWaitStatus.MainframeUnavailable;

        if (!runtime.Processes.TryGetValue(parentId, out var parent) || parent.IsTerminal)
            return DwaineProcessWaitStatus.ProcessNotFound;

        if (parent.LastWaitResult is { } delivered && delivered.ProcessId == childId)
        {
            result = delivered;
            parent.LastWaitResult = null;
            return DwaineProcessWaitStatus.Completed;
        }

        if (!runtime.Processes.TryGetValue(childId, out var child))
            return DwaineProcessWaitStatus.ProcessNotFound;

        if (child.ParentId != parentId)
            return DwaineProcessWaitStatus.NotAChild;

        if (child.IsTerminal)
        {
            result = child.Result();
            RemoveProcessRecord(mainframe, runtime, child);
            return DwaineProcessWaitStatus.Completed;
        }

        if (parent.State == DwaineProcessState.Waiting && parent.WaitingFor == childId)
            return DwaineProcessWaitStatus.Waiting;

        if (parent.State != DwaineProcessState.Ready)
            return DwaineProcessWaitStatus.InvalidState;

        runtime.Queued.Remove(parent.Id);
        parent.WaitingFor = childId;
        SetState(mainframe, runtime, parent, DwaineProcessState.Waiting);
        return DwaineProcessWaitStatus.Waiting;
    }

    public DwaineProcessMessageResult TrySendMessage(
        EntityUid mainframe,
        DwaineProcessId senderId,
        DwaineProcessId targetId,
        string type,
        string payload)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessMessageResult.MainframeUnavailable;

        if (!runtime.Processes.TryGetValue(senderId, out var sender)
            || sender.IsTerminal
            || !runtime.Processes.TryGetValue(targetId, out var target)
            || target.IsTerminal)
        {
            return DwaineProcessMessageResult.ProcessNotFound;
        }

        if (sender.Owner != target.Owner
            && sender.ParentId != target.Id
            && target.ParentId != sender.Id
            && sender.Owner != DwaineProcessOwner.System)
        {
            return DwaineProcessMessageResult.AccessDenied;
        }

        if (!target.Mailbox.IsValidMessage(type, payload))
            return DwaineProcessMessageResult.MalformedMessage;

        var message = new DwaineProcessMessage(senderId, type, payload, _timing.CurTime);
        return target.Mailbox.TryWrite(message)
            ? DwaineProcessMessageResult.Success
            : DwaineProcessMessageResult.MailboxFull;
    }

    /// <summary>
    /// Delivers a typed kernel-originated notification through the same bounded mailbox as IPC.
    /// The zero sender is reserved for the kernel and cannot be selected by a user process.
    /// </summary>
    public DwaineProcessMessageResult TrySendKernelMessage(
        EntityUid mainframe,
        DwaineProcessId targetId,
        DwaineKernelMessageType type,
        string payload,
        DwaineRequestCorrelationId correlation = default)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime))
            return DwaineProcessMessageResult.MainframeUnavailable;
        if (!runtime.Processes.TryGetValue(targetId, out var target) || target.IsTerminal)
            return DwaineProcessMessageResult.ProcessNotFound;
        if (payload.IndexOf('\0') >= 0 || payload.Length > DwaineProcessMailbox.HardMaxPayloadLength - 32)
            return DwaineProcessMessageResult.MalformedMessage;

        var messageType = type switch
        {
            DwaineKernelMessageType.TaskExit => "kernel.task-exit",
            DwaineKernelMessageType.ReceiveFile => "kernel.receive-file",
            DwaineKernelMessageType.Break => "kernel.break",
            DwaineKernelMessageType.Reply => "kernel.reply",
            _ => string.Empty,
        };
        if (messageType.Length == 0)
            return DwaineProcessMessageResult.MalformedMessage;
        var body = correlation.IsValid ? $"{correlation.Value}:{payload}" : payload;
        if (!target.Mailbox.IsValidMessage(messageType, body))
            return DwaineProcessMessageResult.MalformedMessage;
        return target.Mailbox.TryWrite(new DwaineProcessMessage(
                new DwaineProcessId(0),
                messageType,
                body,
                _timing.CurTime))
            ? DwaineProcessMessageResult.Success
            : DwaineProcessMessageResult.MailboxFull;
    }

    public bool TryReceiveMessage(
        EntityUid mainframe,
        DwaineProcessId receiverId,
        out DwaineProcessMessage message)
    {
        message = default;
        return TryGetRuntimeProcess(mainframe, receiverId, out _, out var receiver)
               && !receiver.IsTerminal
               && receiver.Mailbox.TryRead(out message);
    }

    public bool TryWriteInput(EntityUid mainframe, DwaineProcessId processId, string text)
    {
        if (!TryGetRuntimeProcess(mainframe, processId, out var runtime, out var process)
            || process.IsTerminal
            || !process.Stdin.TryWrite(text))
        {
            return false;
        }

        if (process.State == DwaineProcessState.Waiting && process.WaitingFor is null)
        {
            SetState(mainframe, runtime, process, DwaineProcessState.Ready);
            EnqueueReady(mainframe, runtime, process);
        }

        return true;
    }

    public bool TryReadOutput(EntityUid mainframe, DwaineProcessId processId, out string text)
    {
        text = string.Empty;
        return TryGetRuntimeProcess(mainframe, processId, out _, out var process)
               && process.Stdout.TryRead(out text);
    }

    public bool TryReadError(EntityUid mainframe, DwaineProcessId processId, out string text)
    {
        text = string.Empty;
        return TryGetRuntimeProcess(mainframe, processId, out _, out var process)
               && process.Stderr.TryRead(out text);
    }

    public bool TryReap(EntityUid mainframe, DwaineProcessOwner requester, DwaineProcessId processId)
    {
        if (!TryGetOnlineRuntime(mainframe, out var runtime)
            || !runtime.Processes.TryGetValue(processId, out var process)
            || !process.IsTerminal
            || requester != DwaineProcessOwner.System && requester != process.Owner)
        {
            return false;
        }

        RemoveProcessRecord(mainframe, runtime, process);
        return true;
    }

    public bool TryGetProcess(EntityUid mainframe, DwaineProcessId processId, out DwaineProcessSnapshot snapshot)
    {
        snapshot = default;
        if (!TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Processes.TryGetValue(processId, out var process))
        {
            return false;
        }

        snapshot = process.Snapshot();
        return true;
    }

    public DwaineProcessSnapshot[] GetProcessTable(EntityUid mainframe)
    {
        return TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime)
            ? runtime.Processes.Values
                .OrderBy(process => process.Id.Value)
                .Select(process => process.Snapshot())
                .ToArray()
            : [];
    }

    public int GetActiveProcessCount(EntityUid mainframe)
    {
        return TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime)
            ? GetActiveProcessCount(runtime)
            : 0;
    }

    public IReadOnlyDictionary<string, string>? GetEnvironmentSnapshot(
        EntityUid mainframe,
        DwaineProcessId processId)
    {
        return TryGetRuntimeProcess(mainframe, processId, out _, out var process)
            ? process.Environment.Snapshot()
            : null;
    }

    private void Dispatch(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process,
        DwaineProcessLimits limits,
        TimeSpan now)
    {
        var instructionsRemaining = limits.InstructionsPerProcess - process.InstructionsConsumed;
        if (instructionsRemaining <= 0)
        {
            CompleteProcessTree(
                mainframe,
                runtime,
                process,
                DwaineProcessState.Faulted,
                -1,
                DwaineProcessExitReason.InstructionLimit,
                "instruction-limit-exceeded",
                now);
            return;
        }

        SetState(mainframe, runtime, process, DwaineProcessState.Running);
        var instructionBudget = (int) Math.Min(limits.InstructionsPerSlice, instructionsRemaining);
        var context = new DwaineProcessExecutionContext(
            process,
            instructionBudget,
            (target, type, payload) => TrySendMessage(mainframe, process.Id, target, type, payload));

        DwaineProcessStepResult step;
        try
        {
            step = process.Implementation.Step(context);
        }
        catch (Exception)
        {
            process.InstructionsConsumed += context.InstructionsConsumed;
            CompleteProcessTree(
                mainframe,
                runtime,
                process,
                DwaineProcessState.Faulted,
                -1,
                DwaineProcessExitReason.Fault,
                "program-exception",
                now);
            return;
        }

        process.InstructionsConsumed += context.InstructionsConsumed;
        if (context.BudgetExceeded)
        {
            CompleteProcessTree(
                mainframe,
                runtime,
                process,
                DwaineProcessState.Faulted,
                -1,
                DwaineProcessExitReason.InstructionLimit,
                "instruction-budget-exceeded",
                now);
            return;
        }

        switch (step.Kind)
        {
            case DwaineProcessStepKind.Yield:
                SetState(mainframe, runtime, process, DwaineProcessState.Ready);
                EnqueueReady(mainframe, runtime, process);
                break;
            case DwaineProcessStepKind.WaitForInput:
                process.WaitingFor = null;
                if (process.Stdin.Count > 0)
                {
                    SetState(mainframe, runtime, process, DwaineProcessState.Ready);
                    EnqueueReady(mainframe, runtime, process);
                }
                else
                {
                    SetState(mainframe, runtime, process, DwaineProcessState.Waiting);
                }
                break;
            case DwaineProcessStepKind.WaitForChild:
                if (step.WaitFor is not { } childId
                    || !BeginWait(mainframe, runtime, process, childId))
                {
                    CompleteProcessTree(
                        mainframe,
                        runtime,
                        process,
                        DwaineProcessState.Faulted,
                        -1,
                        DwaineProcessExitReason.Fault,
                        "invalid-wait-target",
                        now);
                }
                break;
            case DwaineProcessStepKind.Exit:
                CompleteProcessTree(
                    mainframe,
                    runtime,
                    process,
                    DwaineProcessState.Exited,
                    step.ExitCode,
                    DwaineProcessExitReason.NormalExit,
                    string.Empty,
                    now);
                break;
            case DwaineProcessStepKind.Fault:
                CompleteProcessTree(
                    mainframe,
                    runtime,
                    process,
                    DwaineProcessState.Faulted,
                    -1,
                    DwaineProcessExitReason.Fault,
                    NormalizeCode(step.ErrorCode, "program-fault"),
                    now);
                break;
            default:
                CompleteProcessTree(
                    mainframe,
                    runtime,
                    process,
                    DwaineProcessState.Faulted,
                    -1,
                    DwaineProcessExitReason.Fault,
                    "invalid-step-result",
                    now);
                break;
        }

        PruneCompleted(mainframe, runtime, limits, now);
    }

    private bool BeginWait(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord parent,
        DwaineProcessId childId)
    {
        if (!runtime.Processes.TryGetValue(childId, out var child) || child.ParentId != parent.Id)
            return false;

        if (child.IsTerminal)
        {
            parent.LastWaitResult = child.Result();
            RemoveProcessRecord(mainframe, runtime, child);
            SetState(mainframe, runtime, parent, DwaineProcessState.Ready);
            EnqueueReady(mainframe, runtime, parent);
            return true;
        }

        parent.WaitingFor = childId;
        SetState(mainframe, runtime, parent, DwaineProcessState.Waiting);
        return true;
    }

    private void CompleteProcessTree(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process,
        DwaineProcessState state,
        int exitCode,
        DwaineProcessExitReason reason,
        string errorCode,
        TimeSpan now)
    {
        var descendants = new List<DwaineProcessRecord>();
        var terminating = new HashSet<DwaineProcessId> { process.Id };
        var pending = new Stack<DwaineProcessId>(process.Children);
        while (pending.TryPop(out var childId))
        {
            if (!runtime.Processes.TryGetValue(childId, out var child))
                continue;

            descendants.Add(child);
            terminating.Add(child.Id);
            foreach (var grandchild in child.Children)
                pending.Push(grandchild);
        }

        for (var index = descendants.Count - 1; index >= 0; index--)
        {
            var descendant = descendants[index];
            if (!descendant.IsTerminal)
            {
                CompleteSingle(
                    mainframe,
                    runtime,
                    descendant,
                    DwaineProcessState.Exited,
                    143,
                    DwaineProcessExitReason.ParentExited,
                    "parent-exited",
                    now,
                    terminating);
            }
        }

        CompleteSingle(mainframe, runtime, process, state, exitCode, reason, errorCode, now, terminating);
    }

    private void CompleteSingle(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process,
        DwaineProcessState state,
        int exitCode,
        DwaineProcessExitReason reason,
        string errorCode,
        TimeSpan now,
        IReadOnlySet<DwaineProcessId>? terminating = null)
    {
        if (process.IsTerminal)
            return;

        runtime.Queued.Remove(process.Id);
        DecrementOwnerCount(runtime, process.Owner);

        if (reason != DwaineProcessExitReason.NormalExit
            && process.Implementation is IDwaineCancellableProcessProgram cancellable)
        {
            try
            {
                cancellable.Cancel(reason);
            }
            catch (Exception)
            {
                // A cancellation callback is advisory and cannot interrupt kernel cleanup.
            }
        }

        process.ExitCode = exitCode;
        process.ExitReason = reason;
        process.ErrorCode = NormalizeCode(errorCode, string.Empty);
        process.CompletedAt = now;
        process.WaitingFor = null;
        runtime.CompletedProcessCount++;
        SetState(mainframe, runtime, process, state);

        var delivered = false;
        if (process.ParentId is { } parentId
            && runtime.Processes.TryGetValue(parentId, out var parent)
            && parent.WaitingFor == process.Id)
        {
            parent.WaitingFor = null;
            parent.Children.Remove(process.Id);
            var parentWillTerminate = terminating?.Contains(parent.Id) == true;
            if (!parentWillTerminate)
            {
                parent.LastWaitResult = process.Result();
                if (parent.State == DwaineProcessState.Waiting)
                {
                    SetState(mainframe, runtime, parent, DwaineProcessState.Ready);
                    EnqueueReady(mainframe, runtime, parent);
                }
            }

            delivered = true;
        }

        if (delivered)
            RemoveProcessRecord(mainframe, runtime, process);
        else
            runtime.CompletedOrder.Enqueue(process.Id);
    }

    private void ShutdownRuntime(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessExitReason reason,
        bool raiseEvents)
    {
        _scheduledMainframes.Remove(mainframe);
        var now = _timing.CurTime;
        foreach (var process in runtime.Processes.Values.ToArray())
        {
            if (process.IsTerminal)
                continue;

            if (process.Implementation is IDwaineCancellableProcessProgram cancellable)
            {
                try
                {
                    cancellable.Cancel(reason);
                }
                catch (Exception)
                {
                    // Cleanup must continue for every process.
                }
            }

            process.ExitCode = 143;
            process.ExitReason = reason;
            process.ErrorCode = "kernel-shutdown";
            process.CompletedAt = now;
            process.WaitingFor = null;
            if (raiseEvents)
                SetState(mainframe, runtime, process, DwaineProcessState.Exited);
            else
                process.State = DwaineProcessState.Exited;
        }

        foreach (var process in runtime.Processes.Values)
        {
            process.Stdin.Clear();
            process.Stdout.Clear();
            process.Stderr.Clear();
            process.Mailbox.Clear();
        }

        runtime.Processes.Clear();
        runtime.ActiveByOwner.Clear();
        runtime.ReadyQueue.Clear();
        runtime.Queued.Clear();
        runtime.CompletedOrder.Clear();
        runtime.CompletedProcessCount = 0;
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private void OnKernelServiceShutdown(
        EntityUid mainframe,
        ulong bootGeneration,
        DwaineKernelShutdownReason reason)
    {
        if (!TryComp<DwaineProcessRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Online
            || runtime.BootGeneration != bootGeneration)
        {
            return;
        }

        ShutdownRuntime(mainframe, runtime, DwaineProcessExitReason.KernelShutdown, true);
    }

    private void EnqueueReady(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process)
    {
        if (process.State != DwaineProcessState.Ready || !runtime.Queued.Add(process.Id))
            return;

        runtime.ReadyQueue.Enqueue(process.Id);
        _scheduledMainframes.Add(mainframe);
    }

    private static bool TryDequeueReady(
        DwaineProcessRuntimeComponent runtime,
        out DwaineProcessRecord process)
    {
        while (runtime.ReadyQueue.TryDequeue(out var processId))
        {
            runtime.Queued.Remove(processId);
            if (runtime.Processes.TryGetValue(processId, out process!)
                && process.State == DwaineProcessState.Ready)
            {
                return true;
            }
        }

        process = null!;
        return false;
    }

    private void SetState(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process,
        DwaineProcessState state)
    {
        var previous = process.State;
        process.State = state;
        if (previous == state)
            return;

        var changed = new DwaineProcessStateChangedEvent(
            process.Id,
            previous,
            state,
            runtime.BootGeneration);
        RaiseLocalEvent(mainframe, ref changed);
    }

    private static bool TryAllocateProcessId(
        DwaineProcessRuntimeComponent runtime,
        out DwaineProcessId processId)
    {
        for (var attempts = 0; attempts <= runtime.Processes.Count; attempts++)
        {
            var candidate = runtime.NextProcessId++;
            if (runtime.NextProcessId == 0)
                runtime.NextProcessId = 1;
            if (candidate == 0)
                continue;

            processId = new DwaineProcessId(candidate);
            if (!runtime.Processes.ContainsKey(processId))
                return true;
        }

        processId = default;
        return false;
    }

    private bool TryGetRuntimeProcess(
        EntityUid mainframe,
        DwaineProcessId processId,
        out DwaineProcessRuntimeComponent runtime,
        out DwaineProcessRecord process)
    {
        if (!TryGetOnlineRuntime(mainframe, out runtime!)
            || !runtime.Processes.TryGetValue(processId, out process!))
        {
            runtime = null!;
            process = null!;
            return false;
        }

        return true;
    }

    private bool TryGetOnlineRuntime(EntityUid mainframe, out DwaineProcessRuntimeComponent runtime)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp(mainframe, out runtime!)
            || !runtime.Online)
        {
            runtime = null!;
            return false;
        }

        return true;
    }

    private static bool CanControl(DwaineProcessRecord requester, DwaineProcessRecord target)
    {
        return requester.Id == target.Id
               || requester.Owner == DwaineProcessOwner.System
               || requester.Owner == target.Owner;
    }

    private static bool IsValidProgramDescriptor(DwaineProgramDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id)
            || descriptor.Id.Length > HardMaxProgramIdLength
            || string.IsNullOrWhiteSpace(descriptor.DisplayName)
            || descriptor.DisplayName.Length > HardMaxProgramDisplayNameLength)
        {
            return false;
        }

        foreach (var character in descriptor.Id)
        {
            if (!(character is >= 'a' and <= 'z'
                  or >= '0' and <= '9'
                  or '.' or '-' or '_'))
            {
                return false;
            }
        }

        return descriptor.DisplayName.All(character => !char.IsControl(character));
    }

    private static string NormalizeCode(string? code, string fallback)
    {
        if (string.IsNullOrWhiteSpace(code))
            return fallback;

        var normalized = new string(code
            .ToLowerInvariant()
            .Where(character => character is >= 'a' and <= 'z'
                                or >= '0' and <= '9'
                                or '-' or '_' or '.')
            .Take(HardMaxErrorCodeLength)
            .ToArray());
        return string.IsNullOrEmpty(normalized) ? fallback : normalized;
    }

    private static int GetActiveProcessCount(DwaineProcessRuntimeComponent runtime)
    {
        return runtime.ActiveByOwner.Values.Sum();
    }

    private static void DecrementOwnerCount(
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessOwner owner)
    {
        if (!runtime.ActiveByOwner.TryGetValue(owner, out var count))
            return;

        if (count <= 1)
            runtime.ActiveByOwner.Remove(owner);
        else
            runtime.ActiveByOwner[owner] = count - 1;
    }

    private void RemoveProcessRecord(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessRecord process)
    {
        runtime.Processes.Remove(process.Id);
        runtime.Queued.Remove(process.Id);
        if (process.ParentId is { } parentId
            && runtime.Processes.TryGetValue(parentId, out var parent))
        {
            parent.Children.Remove(process.Id);
        }

        process.Stdin.Clear();
        process.Stdout.Clear();
        process.Stderr.Clear();
        process.Mailbox.Clear();
        if (process.IsTerminal && runtime.CompletedProcessCount > 0)
            runtime.CompletedProcessCount--;
        var removed = new DwaineProcessRemovedEvent(process.Id, runtime.BootGeneration);
        RaiseLocalEvent(mainframe, ref removed);
    }

    private void PruneCompleted(
        EntityUid mainframe,
        DwaineProcessRuntimeComponent runtime,
        DwaineProcessLimits limits,
        TimeSpan now)
    {
        while (runtime.CompletedOrder.TryPeek(out var processId))
        {
            if (!runtime.Processes.TryGetValue(processId, out var process) || !process.IsTerminal)
            {
                runtime.CompletedOrder.Dequeue();
                continue;
            }

            var expired = process.CompletedAt is { } completedAt
                          && now - completedAt >= limits.CompletedRetention;
            if (runtime.CompletedProcessCount <= limits.CompletedProcessLimit && !expired)
                break;

            runtime.CompletedOrder.Dequeue();
            RemoveProcessRecord(mainframe, runtime, process);
        }
    }

    private sealed class ProcessKernelService(
        DwaineProcessSystem system,
        EntityUid mainframe,
        ulong bootGeneration) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe != mainframe || context.BootGeneration != bootGeneration)
                return;

            system.OnKernelServiceShutdown(mainframe, bootGeneration, context.Reason);
        }
    }
}
