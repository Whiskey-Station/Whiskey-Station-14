// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;
using System;

namespace Content.Server._Whiskey.Dwaine.Shell;

public sealed class DwaineShellProcessProgram(
    DwaineShellEngine engine,
    DwaineShellSession session,
    IDwaineShellHost host) : IDwaineProcessProgram
{
    private DwaineProcessId? _waitingFor;

    public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
    {
        if (_waitingFor is { } child)
        {
            if (!context.TryTakeWaitResult(out var waitResult))
                return DwaineProcessStepResult.Wait(child);

            var output = new DwaineShellProgramOutput(
                string.Empty,
                waitResult.State == Content.Shared._Whiskey.Dwaine.Process.DwaineProcessState.Faulted
                    ? $"vodka: process terminated: {waitResult.ErrorCode.Replace('-', ' ')}\n"
                    : string.Empty,
                waitResult.ExitCode,
                waitResult.ErrorCode);
            if (host is IDwaineVodkaShellHost vodkaHost)
                vodkaHost.TryTakeVodkaOutput(child, out output);

            _waitingFor = null;
            session.LastExitCode = output.ExitCode;
            if (!WriteChunks(output.StandardOutput, context.TryWriteStdout)
                || !WriteChunks(output.StandardError, context.TryWriteStderr))
            {
                return DwaineProcessStepResult.Fault("shell-output-limit");
            }
            return DwaineProcessStepResult.WaitForInput();
        }

        if (!context.TryReadStdin(out var input))
            return DwaineProcessStepResult.WaitForInput();

        var result = engine.Execute(input, session, host);
        if (!context.TryChargeInstructions(Math.Max(1, result.InstructionsConsumed)))
            return DwaineProcessStepResult.Fault("shell-instruction-budget");
        if (!WriteChunks(result.StandardOutput, context.TryWriteStdout)
            || !WriteChunks(result.StandardError, context.TryWriteStderr))
        {
            return DwaineProcessStepResult.Fault("shell-output-limit");
        }

        if (result.TerminateProcess)
            return DwaineProcessStepResult.Exit(result.ExitCode);
        if (result.WaitFor is { } waitFor)
        {
            _waitingFor = waitFor;
            return DwaineProcessStepResult.Wait(waitFor);
        }
        return DwaineProcessStepResult.WaitForInput();
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
