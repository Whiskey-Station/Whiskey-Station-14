// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Whiskey.VodkaCode.Frontend;

internal enum VodkaTokenKind
{
    EndOfFile,
    Identifier,
    Integer,
    String,

    Let,
    If,
    Else,
    While,
    Break,
    Continue,
    Return,
    Exit,
    True,
    False,
    Null,
    And,
    Or,
    Xor,
    Not,

    LeftBrace,
    RightBrace,
    LeftParenthesis,
    RightParenthesis,
    Semicolon,
    Comma,
    Dot,
    Equal,
    EqualEqual,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
}

internal readonly record struct VodkaToken(
    VodkaTokenKind Kind,
    string Lexeme,
    string? StringValue,
    VodkaSourceSpan Span);

internal sealed record VodkaLexResult(
    IReadOnlyList<VodkaToken> Tokens,
    IReadOnlyList<VodkaDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
