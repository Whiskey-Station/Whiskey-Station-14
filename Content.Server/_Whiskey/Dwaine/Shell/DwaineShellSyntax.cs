// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Shell;

public enum DwaineShellTokenKind : byte
{
    Word,
    Pipe,
    AndIf,
    OrIf,
    Semicolon,
    RedirectOutput,
    RedirectAppend,
    RedirectInput,
}

public readonly record struct DwaineShellWordSegment(string Text, bool Expand);

public sealed record DwaineShellWord(IReadOnlyList<DwaineShellWordSegment> Segments)
{
    public string Text => string.Concat(Segments.Select(segment => segment.Text));
}

public readonly record struct DwaineShellToken(
    DwaineShellTokenKind Kind,
    DwaineShellWord? Word,
    int Position);

public enum DwaineShellChainCondition : byte
{
    Always,
    OnSuccess,
    OnFailure,
}

public enum DwaineShellRedirectionKind : byte
{
    Input,
    Output,
    Append,
}

public readonly record struct DwaineShellRedirection(
    DwaineShellRedirectionKind Kind,
    DwaineShellWord Target);

public sealed record DwaineShellCommandNode(
    IReadOnlyList<DwaineShellWord> Words,
    IReadOnlyList<DwaineShellRedirection> Redirections);

public sealed record DwaineShellPipelineNode(
    DwaineShellChainCondition Condition,
    IReadOnlyList<DwaineShellCommandNode> Commands);

public sealed record DwaineShellLineNode(IReadOnlyList<DwaineShellPipelineNode> Pipelines);

public readonly record struct DwaineShellDiagnostic(int Position, string Message)
{
    public override string ToString()
    {
        return $"shell: column {Position + 1}: {Message}";
    }
}

public readonly record struct DwaineShellParseResult(
    DwaineShellLineNode? Line,
    DwaineShellDiagnostic? Diagnostic)
{
    public bool Succeeded => Line is not null && Diagnostic is null;
}
