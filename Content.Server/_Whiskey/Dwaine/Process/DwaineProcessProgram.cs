// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.Dwaine.Process;

public enum DwaineProcessStepKind : byte
{
    Yield,
    WaitForInput,
    WaitForChild,
    Exit,
    Fault,
}

public readonly record struct DwaineProcessStepResult(
    DwaineProcessStepKind Kind,
    DwaineProcessId? WaitFor,
    int ExitCode,
    string ErrorCode)
{
    public static DwaineProcessStepResult Yield()
    {
        return new DwaineProcessStepResult(DwaineProcessStepKind.Yield, null, 0, string.Empty);
    }

    public static DwaineProcessStepResult Wait(DwaineProcessId child)
    {
        return new DwaineProcessStepResult(DwaineProcessStepKind.WaitForChild, child, 0, string.Empty);
    }

    public static DwaineProcessStepResult WaitForInput()
    {
        return new DwaineProcessStepResult(DwaineProcessStepKind.WaitForInput, null, 0, string.Empty);
    }

    public static DwaineProcessStepResult Exit(int exitCode = 0)
    {
        return new DwaineProcessStepResult(DwaineProcessStepKind.Exit, null, exitCode, string.Empty);
    }

    public static DwaineProcessStepResult Fault(string errorCode)
    {
        return new DwaineProcessStepResult(DwaineProcessStepKind.Fault, null, -1, errorCode);
    }
}

/// <summary>
/// A trusted server program executes exactly one bounded logical step per scheduler dispatch.
/// Implementations may not block, sleep, start a Task, or perform their own scheduling.
/// </summary>
public interface IDwaineProcessProgram
{
    DwaineProcessStepResult Step(DwaineProcessExecutionContext context);
}

public interface IDwaineCancellableProcessProgram
{
    void Cancel(DwaineProcessExitReason reason);
}

/// <summary>
/// Narrow process API. It exposes bounded streams, accounting, wait results and typed IPC,
/// never an EntityUid or a mutable process record.
/// </summary>
public sealed class DwaineProcessExecutionContext
{
    private readonly DwaineProcessRecord _process;
    private readonly Func<DwaineProcessId, string, string, DwaineProcessMessageResult> _sendMessage;
    private readonly int _instructionBudget;

    public DwaineProcessId ProcessId => _process.Id;
    public DwaineProcessId? ParentId => _process.ParentId;
    public DwaineProcessOwner Owner => _process.Owner;
    public DwaineWorkingDirectoryHandle WorkingDirectory => _process.WorkingDirectory;
    public int InstructionsConsumed { get; private set; } = 1;
    public int InstructionsRemaining => Math.Max(0, _instructionBudget - InstructionsConsumed);
    public bool BudgetExceeded { get; private set; }

    internal DwaineProcessExecutionContext(
        DwaineProcessRecord process,
        int instructionBudget,
        Func<DwaineProcessId, string, string, DwaineProcessMessageResult> sendMessage)
    {
        _process = process;
        _instructionBudget = instructionBudget;
        _sendMessage = sendMessage;
        if (instructionBudget < 1)
            BudgetExceeded = true;
    }

    public bool TryChargeInstructions(int instructions)
    {
        if (instructions <= 0 || BudgetExceeded)
            return false;

        if (instructions > _instructionBudget - InstructionsConsumed)
        {
            BudgetExceeded = true;
            return false;
        }

        InstructionsConsumed += instructions;
        return true;
    }

    public bool TryReadStdin(out string text)
    {
        return _process.Stdin.TryRead(out text);
    }

    public bool TryWriteStdout(string text)
    {
        return _process.Stdout.TryWrite(text);
    }

    public bool TryWriteStderr(string text)
    {
        return _process.Stderr.TryWrite(text);
    }

    public bool TryGetEnvironment(string name, out string value)
    {
        return _process.Environment.TryGet(name, out value);
    }

    public bool TrySetEnvironment(string name, string value)
    {
        return _process.Environment.TrySet(name, value);
    }

    public bool TryReceiveMessage(out DwaineProcessMessage message)
    {
        return _process.Mailbox.TryRead(out message);
    }

    public DwaineProcessMessageResult TrySendMessage(
        DwaineProcessId target,
        string type,
        string payload)
    {
        return _sendMessage(target, type, payload);
    }

    public bool TryTakeWaitResult(out DwaineProcessResult result)
    {
        if (_process.LastWaitResult is not { } available)
        {
            result = default;
            return false;
        }

        result = available;
        _process.LastWaitResult = null;
        return true;
    }
}
