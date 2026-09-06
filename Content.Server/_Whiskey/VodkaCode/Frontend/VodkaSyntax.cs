// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.VodkaCode.Frontend;

internal abstract record VodkaSyntaxNode(VodkaSourceSpan Span);

internal sealed record VodkaProgramSyntax(
    IReadOnlyList<VodkaStatementSyntax> Statements,
    VodkaSourceSpan Span) : VodkaSyntaxNode(Span);

internal abstract record VodkaStatementSyntax(VodkaSourceSpan Span) : VodkaSyntaxNode(Span);

internal sealed record VodkaBlockStatementSyntax(
    IReadOnlyList<VodkaStatementSyntax> Statements,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaLetStatementSyntax(
    string Name,
    VodkaExpressionSyntax? Initializer,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaAssignmentStatementSyntax(
    string Name,
    VodkaExpressionSyntax Value,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaIfStatementSyntax(
    VodkaExpressionSyntax Condition,
    VodkaBlockStatementSyntax Then,
    VodkaBlockStatementSyntax? Else,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaWhileStatementSyntax(
    VodkaExpressionSyntax Condition,
    VodkaBlockStatementSyntax Body,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaBreakStatementSyntax(VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaContinueStatementSyntax(VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaReturnStatementSyntax(
    VodkaExpressionSyntax? Value,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaExitStatementSyntax(
    VodkaExpressionSyntax? Code,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal sealed record VodkaExpressionStatementSyntax(
    VodkaExpressionSyntax Expression,
    VodkaSourceSpan Span) : VodkaStatementSyntax(Span);

internal abstract record VodkaExpressionSyntax(VodkaSourceSpan Span) : VodkaSyntaxNode(Span);

internal enum VodkaLiteralKind
{
    Integer,
    String,
    Boolean,
    Null,
}

internal sealed record VodkaLiteralExpressionSyntax(
    VodkaLiteralKind Kind,
    object? Value,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal sealed record VodkaIdentifierExpressionSyntax(
    string Name,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal enum VodkaUnaryOperator
{
    Negate,
    Not,
}

internal sealed record VodkaUnaryExpressionSyntax(
    VodkaUnaryOperator Operator,
    VodkaExpressionSyntax Operand,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal enum VodkaBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    And,
    Xor,
    Or,
}

internal sealed record VodkaBinaryExpressionSyntax(
    VodkaExpressionSyntax Left,
    VodkaBinaryOperator Operator,
    VodkaExpressionSyntax Right,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal sealed record VodkaMemberExpressionSyntax(
    VodkaExpressionSyntax Target,
    string Member,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal sealed record VodkaCallExpressionSyntax(
    VodkaExpressionSyntax Target,
    IReadOnlyList<VodkaExpressionSyntax> Arguments,
    VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal sealed record VodkaErrorExpressionSyntax(VodkaSourceSpan Span) : VodkaExpressionSyntax(Span);

internal sealed record VodkaParseResult(
    VodkaProgramSyntax Program,
    IReadOnlyList<VodkaToken> Tokens,
    IReadOnlyList<VodkaDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
