// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

internal sealed class VodkaProcessProgram : IDwaineProcessProgram, IDwaineCancellableProcessProgram
{
    private readonly VodkaVirtualMachine _machine;

    public VodkaProcessProgram(VodkaVirtualMachine machine)
    {
        _machine = machine;
    }

    public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
    {
        var slice = _machine.ExecuteSlice(context.InstructionsRemaining);
        if (slice.InstructionsConsumed > 0 && !context.TryChargeInstructions(slice.InstructionsConsumed))
            return DwaineProcessStepResult.Fault("vodka-scheduler-budget");
        if (!WriteChunks(slice.StandardOutput, context.TryWriteStdout)
            || !WriteChunks(slice.StandardError, context.TryWriteStderr))
        {
            return DwaineProcessStepResult.Fault("vodka-process-output");
        }

        return slice.State switch
        {
            VodkaExecutionState.Ready or VodkaExecutionState.Yielded => DwaineProcessStepResult.Yield(),
            VodkaExecutionState.Returned => DwaineProcessStepResult.Exit(0),
            VodkaExecutionState.Exited => DwaineProcessStepResult.Exit(slice.ExitCode),
            VodkaExecutionState.Cancelled => DwaineProcessStepResult.Exit(130),
            VodkaExecutionState.Faulted => DwaineProcessStepResult.Fault(slice.ErrorCode),
            _ => DwaineProcessStepResult.Fault("vodka-invalid-state"),
        };
    }

    public void Cancel(DwaineProcessExitReason reason)
    {
        _machine.Cancel();
    }

    private static bool WriteChunks(string text, Func<string, bool> writer)
    {
        for (var offset = 0; offset < text.Length; offset += DwaineProcessTextStream.HardMaxChunkLength)
        {
            var length = Math.Min(DwaineProcessTextStream.HardMaxChunkLength, text.Length - offset);
            if (!writer(text.Substring(offset, length)))
                return false;
        }
        return true;
    }
}
