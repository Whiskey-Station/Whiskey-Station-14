// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Content.Server._Whiskey.VodkaCode.Frontend;

/// <summary>
/// Bounded lexer for untrusted Vodka Code source. It accepts only the grammar's ASCII identifiers and punctuation.
/// </summary>
internal sealed class VodkaLexer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _source;
    private readonly VodkaFrontendLimits _limits;
    private readonly List<VodkaToken> _tokens = new();
    private readonly List<VodkaDiagnostic> _diagnostics = new();

    private int _offset;
    private int _line = 1;
    private int _column = 1;
    private bool _diagnosticLimitReported;

    private VodkaLexer(string source, VodkaFrontendLimits limits)
    {
        _source = source;
        _limits = limits.Clamped();
    }

    public static VodkaLexResult Lex(string source, VodkaFrontendLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new VodkaLexer(source, limits ?? VodkaFrontendLimits.Default).Run();
    }

    private VodkaLexResult Run()
    {
        if (!ValidateUnicode())
        {
            AddEndOfFile();
            return Finish();
        }

        if (StrictUtf8.GetByteCount(_source) > _limits.MaxSourceBytes)
        {
            var start = new VodkaSourcePosition(0, 1, 1);
            AddDiagnostic(
                VodkaDiagnosticCode.SourceTooLarge,
                $"source exceeds {_limits.MaxSourceBytes} UTF-8 bytes",
                new VodkaSourceSpan(start, start));
            AddEndOfFile();
            return Finish();
        }

        while (!AtEnd)
        {
            SkipTrivia();
            if (AtEnd)
                break;

            if (_tokens.Count >= _limits.MaxTokens)
            {
                var here = Position;
                AddDiagnostic(
                    VodkaDiagnosticCode.TokenLimitExceeded,
                    $"token limit of {_limits.MaxTokens} exceeded",
                    new VodkaSourceSpan(here, here));
                break;
            }

            ScanToken();
        }

        AddEndOfFile();
        return Finish();
    }

    private VodkaLexResult Finish()
    {
        return new VodkaLexResult(_tokens.ToArray(), _diagnostics.ToArray());
    }

    private bool ValidateUnicode()
    {
        for (var index = 0; index < _source.Length; index++)
        {
            var character = _source[index];
            if (!char.IsSurrogate(character))
                continue;

            if (char.IsHighSurrogate(character)
                && index + 1 < _source.Length
                && char.IsLowSurrogate(_source[index + 1]))
            {
                index++;
                continue;
            }

            var position = PositionAt(index);
            AddDiagnostic(
                VodkaDiagnosticCode.InvalidUnicode,
                "source contains an invalid Unicode sequence",
                new VodkaSourceSpan(position, position with { Offset = index + 1, Column = position.Column + 1 }));
            return false;
        }

        return true;
    }

    private VodkaSourcePosition PositionAt(int targetOffset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < targetOffset; index++)
        {
            if (_source[index] == '\r')
            {
                if (index + 1 < targetOffset && _source[index + 1] == '\n')
                    index++;
                line++;
                column = 1;
            }
            else if (_source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new VodkaSourcePosition(targetOffset, line, column);
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            switch (Current)
            {
                case ' ':
                case '\t':
                case '\f':
                    Advance();
                    break;
                case '\r':
                case '\n':
                    ConsumeNewline();
                    break;
                case '#':
                    while (!AtEnd && Current is not ('\r' or '\n'))
                        Advance();
                    break;
                default:
                    return;
            }
        }
    }

    private void ScanToken()
    {
        var start = Position;
        var character = Advance();

        if (IsIdentifierStart(character))
        {
            ScanIdentifier(start);
            return;
        }

        if (IsDigit(character))
        {
            ScanInteger(start);
            return;
        }

        switch (character)
        {
            case '"':
                ScanString(start);
                return;
            case '{':
                AddToken(VodkaTokenKind.LeftBrace, start);
                return;
            case '}':
                AddToken(VodkaTokenKind.RightBrace, start);
                return;
            case '(':
                AddToken(VodkaTokenKind.LeftParenthesis, start);
                return;
            case ')':
                AddToken(VodkaTokenKind.RightParenthesis, start);
                return;
            case ';':
                AddToken(VodkaTokenKind.Semicolon, start);
                return;
            case ',':
                AddToken(VodkaTokenKind.Comma, start);
                return;
            case '.':
                AddToken(VodkaTokenKind.Dot, start);
                return;
            case '+':
                AddToken(VodkaTokenKind.Plus, start);
                return;
            case '-':
                AddToken(VodkaTokenKind.Minus, start);
                return;
            case '*':
                AddToken(VodkaTokenKind.Star, start);
                return;
            case '/':
                AddToken(VodkaTokenKind.Slash, start);
                return;
            case '%':
                AddToken(VodkaTokenKind.Percent, start);
                return;
            case '=':
                AddToken(Match('=') ? VodkaTokenKind.EqualEqual : VodkaTokenKind.Equal, start);
                return;
            case '!':
                if (Match('='))
                {
                    AddToken(VodkaTokenKind.BangEqual, start);
                    return;
                }
                break;
            case '<':
                AddToken(Match('=') ? VodkaTokenKind.LessEqual : VodkaTokenKind.Less, start);
                return;
            case '>':
                AddToken(Match('=') ? VodkaTokenKind.GreaterEqual : VodkaTokenKind.Greater, start);
                return;
        }

        AddDiagnostic(
            VodkaDiagnosticCode.UnexpectedCharacter,
            $"unexpected character '{SafeCharacter(character)}'",
            new VodkaSourceSpan(start, Position));
    }

    private void ScanIdentifier(VodkaSourcePosition start)
    {
        while (!AtEnd && IsIdentifierPart(Current))
            Advance();

        var lexeme = _source[start.Offset.._offset];
        var kind = lexeme switch
        {
            "let" => VodkaTokenKind.Let,
            "if" => VodkaTokenKind.If,
            "else" => VodkaTokenKind.Else,
            "while" => VodkaTokenKind.While,
            "break" => VodkaTokenKind.Break,
            "continue" => VodkaTokenKind.Continue,
            "return" => VodkaTokenKind.Return,
            "exit" => VodkaTokenKind.Exit,
            "true" => VodkaTokenKind.True,
            "false" => VodkaTokenKind.False,
            "null" => VodkaTokenKind.Null,
            "and" => VodkaTokenKind.And,
            "or" => VodkaTokenKind.Or,
            "xor" => VodkaTokenKind.Xor,
            "not" => VodkaTokenKind.Not,
            _ => VodkaTokenKind.Identifier,
        };

        AddToken(kind, start);
    }

    private void ScanInteger(VodkaSourcePosition start)
    {
        while (!AtEnd && IsDigit(Current))
            Advance();

        AddToken(VodkaTokenKind.Integer, start);
    }

    private void ScanString(VodkaSourcePosition start)
    {
        var value = new StringBuilder();
        while (!AtEnd && Current is not ('\r' or '\n'))
        {
            var character = Advance();
            if (character == '"')
            {
                AddToken(VodkaTokenKind.String, start, value.ToString());
                return;
            }

            if (character != '\\')
            {
                value.Append(character);
                continue;
            }

            if (AtEnd || Current is '\r' or '\n')
                break;

            var escapeStart = Position with { Offset = _offset - 1, Column = _column - 1 };
            var escaped = Advance();
            switch (escaped)
            {
                case 'n':
                    value.Append('\n');
                    break;
                case 'r':
                    value.Append('\r');
                    break;
                case 't':
                    value.Append('\t');
                    break;
                case '"':
                    value.Append('"');
                    break;
                case '\\':
                    value.Append('\\');
                    break;
                default:
                    AddDiagnostic(
                        VodkaDiagnosticCode.InvalidEscape,
                        $"invalid escape sequence '\\{SafeCharacter(escaped)}'",
                        new VodkaSourceSpan(escapeStart, Position));
                    break;
            }
        }

        AddDiagnostic(
            VodkaDiagnosticCode.UnterminatedString,
            "unterminated string literal",
            new VodkaSourceSpan(start, Position));
    }

    private void AddToken(VodkaTokenKind kind, VodkaSourcePosition start, string? stringValue = null)
    {
        _tokens.Add(new VodkaToken(kind, _source[start.Offset.._offset], stringValue, new VodkaSourceSpan(start, Position)));
    }

    private void AddEndOfFile()
    {
        var position = Position;
        _tokens.Add(new VodkaToken(
            VodkaTokenKind.EndOfFile,
            string.Empty,
            null,
            new VodkaSourceSpan(position, position)));
    }

    private void AddDiagnostic(VodkaDiagnosticCode code, string message, VodkaSourceSpan span)
    {
        if (_diagnostics.Count < _limits.MaxDiagnostics)
        {
            _diagnostics.Add(new VodkaDiagnostic(code, message, span));
            return;
        }

        if (_diagnosticLimitReported)
            return;

        _diagnosticLimitReported = true;
        var terminal = _diagnostics[^1];
        _diagnostics[^1] = new VodkaDiagnostic(
            VodkaDiagnosticCode.TooManyDiagnostics,
            $"diagnostic limit of {_limits.MaxDiagnostics} reached",
            terminal.Span);
    }

    private char Advance()
    {
        var value = _source[_offset++];
        _column++;
        return value;
    }

    private void ConsumeNewline()
    {
        if (Current == '\r')
        {
            _offset++;
            if (!AtEnd && Current == '\n')
                _offset++;
        }
        else
        {
            _offset++;
        }

        _line++;
        _column = 1;
    }

    private bool Match(char expected)
    {
        if (AtEnd || Current != expected)
            return false;

        Advance();
        return true;
    }

    private static bool IsIdentifierStart(char character)
    {
        return character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or '_';
    }

    private static bool IsIdentifierPart(char character)
    {
        return IsIdentifierStart(character) || IsDigit(character);
    }

    private static bool IsDigit(char character)
    {
        return character is >= '0' and <= '9';
    }

    private static string SafeCharacter(char character)
    {
        return char.IsControl(character) ? $"U+{(int) character:X4}" : character.ToString();
    }

    private VodkaSourcePosition Position => new(_offset, _line, _column);
    private bool AtEnd => _offset >= _source.Length;
    private char Current => _source[_offset];
}
