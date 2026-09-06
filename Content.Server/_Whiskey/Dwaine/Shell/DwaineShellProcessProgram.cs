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
    public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
    {
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

        return result.TerminateProcess
            ? DwaineProcessStepResult.Exit(result.ExitCode)
            : DwaineProcessStepResult.WaitForInput();
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
