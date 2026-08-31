// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace Content.Server._Whiskey.VodkaCode.Frontend;

/// <summary>
/// Error-recovering recursive-descent parser for the bounded Vodka Code grammar.
/// </summary>
internal sealed class VodkaParser
{
    private readonly IReadOnlyList<VodkaToken> _tokens;
    private readonly VodkaFrontendLimits _limits;
    private readonly List<VodkaDiagnostic> _diagnostics;

    private int _current;
    private int _syntaxDepth;
    private int _loopDepth;
    private bool _diagnosticLimitReported;

    private VodkaParser(VodkaLexResult lexed, VodkaFrontendLimits limits)
    {
        _tokens = lexed.Tokens;
        _limits = limits.Clamped();
        _diagnostics = new List<VodkaDiagnostic>(lexed.Diagnostics);
    }

    public static VodkaParseResult Parse(string source, VodkaFrontendLimits? limits = null)
    {
        var effectiveLimits = (limits ?? VodkaFrontendLimits.Default).Clamped();
        var lexed = VodkaLexer.Lex(source, effectiveLimits);
        return new VodkaParser(lexed, effectiveLimits).Run();
    }

    private VodkaParseResult Run()
    {
        var statements = new List<VodkaStatementSyntax>();
        var start = Current.Span.Start;

        while (!AtEnd)
        {
            var before = _current;
            var statement = ParseStatement();
            if (statement != null)
                statements.Add(statement);

            if (_current == before)
                Advance();
        }

        var program = new VodkaProgramSyntax(
            statements.ToArray(),
            new VodkaSourceSpan(start, Current.Span.End));
        return new VodkaParseResult(program, _tokens, _diagnostics.ToArray());
    }

    private VodkaStatementSyntax? ParseStatement()
    {
        if (Match(VodkaTokenKind.LeftBrace))
            return ParseBlock(Previous);
        if (Match(VodkaTokenKind.Let))
            return ParseLet(Previous);
        if (Match(VodkaTokenKind.If))
            return ParseIf(Previous);
        if (Match(VodkaTokenKind.While))
            return ParseWhile(Previous);
        if (Match(VodkaTokenKind.Break))
            return ParseBreak(Previous);
        if (Match(VodkaTokenKind.Continue))
            return ParseContinue(Previous);
        if (Match(VodkaTokenKind.Return))
            return ParseReturn(Previous);
        if (Match(VodkaTokenKind.Exit))
            return ParseExit(Previous);
        if (Check(VodkaTokenKind.Identifier) && CheckNext(VodkaTokenKind.Equal))
            return ParseAssignment();

        return ParseExpressionStatement();
    }

    private VodkaBlockStatementSyntax ParseBlock(VodkaToken openingBrace)
    {
        if (!TryEnterSyntax(openingBrace.Span))
        {
            SkipBalancedBlock();
            return new VodkaBlockStatementSyntax(Array.Empty<VodkaStatementSyntax>(), openingBrace.Span);
        }

        var statements = new List<VodkaStatementSyntax>();
        while (!Check(VodkaTokenKind.RightBrace) && !AtEnd)
        {
            var before = _current;
            var statement = ParseStatement();
            if (statement != null)
                statements.Add(statement);
            if (_current == before)
                Advance();
        }

        var closingBrace = Consume(VodkaTokenKind.RightBrace, "expected '}' after block");
        ExitSyntax();
        return new VodkaBlockStatementSyntax(
            statements.ToArray(),
            VodkaSourceSpan.Cover(openingBrace.Span, closingBrace.Span));
    }

    private VodkaStatementSyntax ParseLet(VodkaToken keyword)
    {
        var name = Consume(VodkaTokenKind.Identifier, "expected variable name after 'let'");
        VodkaExpressionSyntax? initializer = null;
        if (Match(VodkaTokenKind.Equal))
            initializer = ParseExpression();

        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after declaration");
        return new VodkaLetStatementSyntax(
            name.Lexeme,
            initializer,
            VodkaSourceSpan.Cover(keyword.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseAssignment()
    {
        var name = Advance();
        Consume(VodkaTokenKind.Equal, "expected '=' after variable name");
        var value = ParseExpression();
        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after assignment");
        return new VodkaAssignmentStatementSyntax(
            name.Lexeme,
            value,
            VodkaSourceSpan.Cover(name.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseIf(VodkaToken keyword)
    {
        Consume(VodkaTokenKind.LeftParenthesis, "expected '(' after 'if'");
        var condition = ParseExpression();
        Consume(VodkaTokenKind.RightParenthesis, "expected ')' after condition");
        var openingBrace = Consume(VodkaTokenKind.LeftBrace, "expected '{' before if body");
        var then = ParseBlock(openingBrace);

        VodkaBlockStatementSyntax? otherwise = null;
        if (Match(VodkaTokenKind.Else))
        {
            var elseBrace = Consume(VodkaTokenKind.LeftBrace, "expected '{' before else body");
            otherwise = ParseBlock(elseBrace);
        }

        return new VodkaIfStatementSyntax(
            condition,
            then,
            otherwise,
            VodkaSourceSpan.Cover(keyword.Span, (otherwise ?? then).Span));
    }

    private VodkaStatementSyntax ParseWhile(VodkaToken keyword)
    {
        Consume(VodkaTokenKind.LeftParenthesis, "expected '(' after 'while'");
        var condition = ParseExpression();
        Consume(VodkaTokenKind.RightParenthesis, "expected ')' after condition");
        var openingBrace = Consume(VodkaTokenKind.LeftBrace, "expected '{' before while body");

        _loopDepth++;
        var body = ParseBlock(openingBrace);
        _loopDepth--;

        return new VodkaWhileStatementSyntax(
            condition,
            body,
            VodkaSourceSpan.Cover(keyword.Span, body.Span));
    }

    private VodkaStatementSyntax ParseBreak(VodkaToken keyword)
    {
        if (_loopDepth == 0)
        {
            AddDiagnostic(
                VodkaDiagnosticCode.InvalidControlFlow,
                "'break' is only valid inside a while loop",
                keyword.Span);
        }

        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after 'break'");
        return new VodkaBreakStatementSyntax(VodkaSourceSpan.Cover(keyword.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseContinue(VodkaToken keyword)
    {
        if (_loopDepth == 0)
        {
            AddDiagnostic(
                VodkaDiagnosticCode.InvalidControlFlow,
                "'continue' is only valid inside a while loop",
                keyword.Span);
        }

        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after 'continue'");
        return new VodkaContinueStatementSyntax(VodkaSourceSpan.Cover(keyword.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseReturn(VodkaToken keyword)
    {
        var value = Check(VodkaTokenKind.Semicolon) ? null : ParseExpression();
        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after 'return'");
        return new VodkaReturnStatementSyntax(value, VodkaSourceSpan.Cover(keyword.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseExit(VodkaToken keyword)
    {
        var code = Check(VodkaTokenKind.Semicolon) ? null : ParseExpression();
        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after 'exit'");
        return new VodkaExitStatementSyntax(code, VodkaSourceSpan.Cover(keyword.Span, semicolon.Span));
    }

    private VodkaStatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();
        var semicolon = Consume(VodkaTokenKind.Semicolon, "expected ';' after expression");
        return new VodkaExpressionStatementSyntax(
            expression,
            VodkaSourceSpan.Cover(expression.Span, semicolon.Span));
    }

    private VodkaExpressionSyntax ParseExpression()
    {
        return ParseLogicalOr();
    }

    private VodkaExpressionSyntax ParseLogicalOr()
    {
        var expression = ParseLogicalXor();
        while (Match(VodkaTokenKind.Or))
            expression = Binary(expression, VodkaBinaryOperator.Or, ParseLogicalXor());
        return expression;
    }

    private VodkaExpressionSyntax ParseLogicalXor()
    {
        var expression = ParseLogicalAnd();
        while (Match(VodkaTokenKind.Xor))
            expression = Binary(expression, VodkaBinaryOperator.Xor, ParseLogicalAnd());
        return expression;
    }

    private VodkaExpressionSyntax ParseLogicalAnd()
    {
        var expression = ParseEquality();
        while (Match(VodkaTokenKind.And))
            expression = Binary(expression, VodkaBinaryOperator.And, ParseEquality());
        return expression;
    }

    private VodkaExpressionSyntax ParseEquality()
    {
        var expression = ParseRelation();
        while (Match(VodkaTokenKind.EqualEqual, VodkaTokenKind.BangEqual))
        {
            var operation = Previous.Kind == VodkaTokenKind.EqualEqual
                ? VodkaBinaryOperator.Equal
                : VodkaBinaryOperator.NotEqual;
            expression = Binary(expression, operation, ParseRelation());
        }

        return expression;
    }

    private VodkaExpressionSyntax ParseRelation()
    {
        var expression = ParseSum();
        while (Match(
                   VodkaTokenKind.Less,
                   VodkaTokenKind.LessEqual,
                   VodkaTokenKind.Greater,
                   VodkaTokenKind.GreaterEqual))
        {
            var operation = Previous.Kind switch
            {
                VodkaTokenKind.Less => VodkaBinaryOperator.Less,
                VodkaTokenKind.LessEqual => VodkaBinaryOperator.LessOrEqual,
                VodkaTokenKind.Greater => VodkaBinaryOperator.Greater,
                _ => VodkaBinaryOperator.GreaterOrEqual,
            };
            expression = Binary(expression, operation, ParseSum());
        }

        return expression;
    }

    private VodkaExpressionSyntax ParseSum()
    {
        var expression = ParseProduct();
        while (Match(VodkaTokenKind.Plus, VodkaTokenKind.Minus))
        {
            var operation = Previous.Kind == VodkaTokenKind.Plus
                ? VodkaBinaryOperator.Add
                : VodkaBinaryOperator.Subtract;
            expression = Binary(expression, operation, ParseProduct());
        }

        return expression;
    }

    private VodkaExpressionSyntax ParseProduct()
    {
        var expression = ParseUnary();
        while (Match(VodkaTokenKind.Star, VodkaTokenKind.Slash, VodkaTokenKind.Percent))
        {
            var operation = Previous.Kind switch
            {
                VodkaTokenKind.Star => VodkaBinaryOperator.Multiply,
                VodkaTokenKind.Slash => VodkaBinaryOperator.Divide,
                _ => VodkaBinaryOperator.Modulo,
            };
            expression = Binary(expression, operation, ParseUnary());
        }

        return expression;
    }

    private VodkaExpressionSyntax ParseUnary()
    {
        if (Match(VodkaTokenKind.Not, VodkaTokenKind.Minus))
        {
            var operation = Previous;
            if (!TryEnterSyntax(operation.Span))
                return new VodkaErrorExpressionSyntax(operation.Span);

            var operand = ParseUnary();
            ExitSyntax();
            return new VodkaUnaryExpressionSyntax(
                operation.Kind == VodkaTokenKind.Not ? VodkaUnaryOperator.Not : VodkaUnaryOperator.Negate,
                operand,
                VodkaSourceSpan.Cover(operation.Span, operand.Span));
        }

        return ParseCall();
    }

    private VodkaExpressionSyntax ParseCall()
    {
        var expression = ParsePrimary();
        while (true)
        {
            if (Match(VodkaTokenKind.Dot))
            {
                var member = Consume(VodkaTokenKind.Identifier, "expected member name after '.'");
                expression = new VodkaMemberExpressionSyntax(
                    expression,
                    member.Lexeme,
                    VodkaSourceSpan.Cover(expression.Span, member.Span));
                continue;
            }

            if (!Match(VodkaTokenKind.LeftParenthesis))
                break;

            var opening = Previous;
            if (!TryEnterSyntax(opening.Span))
            {
                SkipCallArguments();
                var skippedClosing = Consume(VodkaTokenKind.RightParenthesis, "expected ')' after arguments");
                expression = new VodkaErrorExpressionSyntax(VodkaSourceSpan.Cover(expression.Span, skippedClosing.Span));
                continue;
            }

            var arguments = new List<VodkaExpressionSyntax>();
            if (!Check(VodkaTokenKind.RightParenthesis))
            {
                do
                {
                    if (arguments.Count >= _limits.MaxArguments)
                    {
                        AddDiagnostic(
                            VodkaDiagnosticCode.ArgumentLimitExceeded,
                            $"call argument limit of {_limits.MaxArguments} exceeded",
                            Current.Span);
                        SkipCallArguments();
                        break;
                    }

                    arguments.Add(ParseExpression());
                }
                while (Match(VodkaTokenKind.Comma));
            }

            var closing = Consume(VodkaTokenKind.RightParenthesis, "expected ')' after arguments");
            ExitSyntax();
            expression = new VodkaCallExpressionSyntax(
                expression,
                arguments.ToArray(),
                VodkaSourceSpan.Cover(expression.Span, closing.Span));
        }

        return expression;
    }

    private VodkaExpressionSyntax ParsePrimary()
    {
        if (Match(VodkaTokenKind.Integer))
        {
            var token = Previous;
            if (!long.TryParse(token.Lexeme, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                AddDiagnostic(
                    VodkaDiagnosticCode.IntegerOutOfRange,
                    "integer literal is outside the signed 64-bit range",
                    token.Span);
                return new VodkaErrorExpressionSyntax(token.Span);
            }

            return new VodkaLiteralExpressionSyntax(VodkaLiteralKind.Integer, value, token.Span);
        }

        if (Match(VodkaTokenKind.String))
            return new VodkaLiteralExpressionSyntax(VodkaLiteralKind.String, Previous.StringValue ?? string.Empty, Previous.Span);
        if (Match(VodkaTokenKind.True))
            return new VodkaLiteralExpressionSyntax(VodkaLiteralKind.Boolean, true, Previous.Span);
        if (Match(VodkaTokenKind.False))
            return new VodkaLiteralExpressionSyntax(VodkaLiteralKind.Boolean, false, Previous.Span);
        if (Match(VodkaTokenKind.Null))
            return new VodkaLiteralExpressionSyntax(VodkaLiteralKind.Null, null, Previous.Span);
        if (Match(VodkaTokenKind.Identifier))
            return new VodkaIdentifierExpressionSyntax(Previous.Lexeme, Previous.Span);

        if (Match(VodkaTokenKind.LeftParenthesis))
        {
            var opening = Previous;
            if (!TryEnterSyntax(opening.Span))
            {
                SkipParenthesizedExpression();
                return new VodkaErrorExpressionSyntax(opening.Span);
            }

            var expression = ParseExpression();
            var closing = Consume(VodkaTokenKind.RightParenthesis, "expected ')' after expression");
            ExitSyntax();
            return expression with { Span = VodkaSourceSpan.Cover(opening.Span, closing.Span) };
        }

        var unexpected = Current;
        AddDiagnostic(
            VodkaDiagnosticCode.UnexpectedToken,
            AtEnd ? "expected expression before end of source" : $"unexpected token '{unexpected.Lexeme}'",
            unexpected.Span);
        if (!AtEnd)
            Advance();
        return new VodkaErrorExpressionSyntax(unexpected.Span);
    }

    private static VodkaBinaryExpressionSyntax Binary(
        VodkaExpressionSyntax left,
        VodkaBinaryOperator operation,
        VodkaExpressionSyntax right)
    {
        return new VodkaBinaryExpressionSyntax(left, operation, right, VodkaSourceSpan.Cover(left.Span, right.Span));
    }

    private bool TryEnterSyntax(VodkaSourceSpan span)
    {
        if (_syntaxDepth < _limits.MaxSyntaxDepth)
        {
            _syntaxDepth++;
            return true;
        }

        AddDiagnostic(
            VodkaDiagnosticCode.SyntaxDepthExceeded,
            $"syntax nesting limit of {_limits.MaxSyntaxDepth} exceeded",
            span);
        return false;
    }

    private void ExitSyntax()
    {
        _syntaxDepth--;
    }

    private void SkipBalancedBlock()
    {
        var depth = 1;
        while (!AtEnd && depth > 0)
        {
            if (Match(VodkaTokenKind.LeftBrace))
                depth++;
            else if (Match(VodkaTokenKind.RightBrace))
                depth--;
            else
                Advance();
        }
    }

    private void SkipParenthesizedExpression()
    {
        var depth = 1;
        while (!AtEnd && depth > 0)
        {
            if (Match(VodkaTokenKind.LeftParenthesis))
                depth++;
            else if (Match(VodkaTokenKind.RightParenthesis))
                depth--;
            else
                Advance();
        }
    }

    private void SkipCallArguments()
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Check(VodkaTokenKind.RightParenthesis) && depth == 0)
                return;
            if (Match(VodkaTokenKind.LeftParenthesis))
                depth++;
            else if (Match(VodkaTokenKind.RightParenthesis))
                depth--;
            else
                Advance();
        }
    }

    private VodkaToken Consume(VodkaTokenKind kind, string message)
    {
        if (Check(kind))
            return Advance();

        AddDiagnostic(VodkaDiagnosticCode.ExpectedToken, message, Current.Span);
        return new VodkaToken(kind, string.Empty, null, Current.Span);
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

    private bool Match(params VodkaTokenKind[] kinds)
    {
        foreach (var kind in kinds)
        {
            if (!Check(kind))
                continue;
            Advance();
            return true;
        }

        return false;
    }

    private bool Check(VodkaTokenKind kind)
    {
        return Current.Kind == kind;
    }

    private bool CheckNext(VodkaTokenKind kind)
    {
        return _current + 1 < _tokens.Count && _tokens[_current + 1].Kind == kind;
    }

    private VodkaToken Advance()
    {
        if (!AtEnd)
            _current++;
        return Previous;
    }

    private VodkaToken Current => _tokens[_current];
    private VodkaToken Previous => _tokens[Math.Max(0, _current - 1)];
    private bool AtEnd => Current.Kind == VodkaTokenKind.EndOfFile;
}
