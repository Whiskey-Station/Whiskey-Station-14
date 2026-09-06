// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.VodkaCode.Frontend;

/// <summary>
/// Authoritative limits for turning untrusted Vodka Code source into syntax trees.
/// </summary>
internal readonly record struct VodkaFrontendLimits(
    int MaxSourceBytes,
    int MaxTokens,
    int MaxDiagnostics,
    int MaxSyntaxDepth,
    int MaxArguments)
{
    public static VodkaFrontendLimits Default => new(
        MaxSourceBytes: 65_536,
        MaxTokens: 32_768,
        MaxDiagnostics: 128,
        MaxSyntaxDepth: 64,
        MaxArguments: 64);

    public VodkaFrontendLimits Clamped()
    {
        var hard = Default;
        return new VodkaFrontendLimits(
            Math.Clamp(MaxSourceBytes, 1, hard.MaxSourceBytes),
            Math.Clamp(MaxTokens, 1, hard.MaxTokens),
            Math.Clamp(MaxDiagnostics, 1, hard.MaxDiagnostics),
            Math.Clamp(MaxSyntaxDepth, 1, hard.MaxSyntaxDepth),
            Math.Clamp(MaxArguments, 1, hard.MaxArguments));
    }
}

internal readonly record struct VodkaSourcePosition(int Offset, int Line, int Column)
{
    public override string ToString()
    {
        return $"{Line}:{Column}";
    }
}

internal readonly record struct VodkaSourceSpan(VodkaSourcePosition Start, VodkaSourcePosition End)
{
    public int Length => Math.Max(0, End.Offset - Start.Offset);

    public static VodkaSourceSpan Cover(VodkaSourceSpan first, VodkaSourceSpan last)
    {
        return new VodkaSourceSpan(first.Start, last.End);
    }
}

internal enum VodkaDiagnosticCode
{
    InvalidUnicode,
    SourceTooLarge,
    TokenLimitExceeded,
    TooManyDiagnostics,
    UnexpectedCharacter,
    UnterminatedString,
    InvalidEscape,
    UnexpectedToken,
    ExpectedToken,
    IntegerOutOfRange,
    SyntaxDepthExceeded,
    ArgumentLimitExceeded,
    InvalidControlFlow,
}

internal sealed record VodkaDiagnostic(
    VodkaDiagnosticCode Code,
    string Message,
    VodkaSourceSpan Span)
{
    public string ToTerminalMessage()
    {
        return $"vodka: line {Span.Start.Line}:{Span.Start.Column}: {Message}";
    }
}
