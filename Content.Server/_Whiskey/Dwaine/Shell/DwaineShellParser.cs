// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.Dwaine.Shell;

/// <summary>
/// Purpose-built bounded shell lexer/parser. It does not invoke a host shell or native evaluator.
/// </summary>
public sealed class DwaineShellParser(DwaineShellLimits limits)
{
    public DwaineShellParseResult Parse(string? source)
    {
        if (source is null)
            return Failure(0, "input is null");
        if (source.Length > limits.MaxInputLength)
            return Failure(limits.MaxInputLength, "input limit exceeded");
        if (!TryLex(source, out var tokens, out var diagnostic))
            return new DwaineShellParseResult(null, diagnostic);
        if (tokens.Count == 0)
            return new DwaineShellParseResult(new DwaineShellLineNode([]), null);

        var pipelines = new List<DwaineShellPipelineNode>();
        var index = 0;
        var condition = DwaineShellChainCondition.Always;
        var commandCount = 0;
        while (index < tokens.Count)
        {
            var commands = new List<DwaineShellCommandNode>();
            while (true)
            {
                if (!TryParseCommand(tokens, ref index, out var command, out diagnostic))
                    return new DwaineShellParseResult(null, diagnostic);
                commands.Add(command!);
                commandCount++;
                if (commandCount > limits.MaxCommands)
                    return Failure(tokens[index - 1].Position, "command limit exceeded");
                if (commands.Count > limits.MaxPipelineStages)
                    return Failure(tokens[index - 1].Position, "pipeline limit exceeded");

                if (index >= tokens.Count || tokens[index].Kind != DwaineShellTokenKind.Pipe)
                    break;
                index++;
                if (index >= tokens.Count)
                    return Failure(tokens[index - 1].Position, "expected command after pipe");
            }

            pipelines.Add(new DwaineShellPipelineNode(condition, commands));
            if (index >= tokens.Count)
                break;

            var connector = tokens[index++];
            condition = connector.Kind switch
            {
                DwaineShellTokenKind.AndIf => DwaineShellChainCondition.OnSuccess,
                DwaineShellTokenKind.OrIf => DwaineShellChainCondition.OnFailure,
                DwaineShellTokenKind.Semicolon => DwaineShellChainCondition.Always,
                _ => (DwaineShellChainCondition) byte.MaxValue,
            };
            if (condition == (DwaineShellChainCondition) byte.MaxValue)
                return Failure(connector.Position, "unexpected operator");
            if (index >= tokens.Count)
                return Failure(connector.Position, "expected command after connector");
        }

        return new DwaineShellParseResult(new DwaineShellLineNode(pipelines), null);
    }

    private bool TryParseCommand(
        IReadOnlyList<DwaineShellToken> tokens,
        ref int index,
        out DwaineShellCommandNode? command,
        out DwaineShellDiagnostic? diagnostic)
    {
        command = null;
        diagnostic = null;
        var words = new List<DwaineShellWord>();
        var redirections = new List<DwaineShellRedirection>();

        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.Kind == DwaineShellTokenKind.Word)
            {
                words.Add(token.Word!);
                index++;
                continue;
            }

            if (token.Kind is DwaineShellTokenKind.RedirectInput
                or DwaineShellTokenKind.RedirectOutput
                or DwaineShellTokenKind.RedirectAppend)
            {
                index++;
                if (index >= tokens.Count || tokens[index].Kind != DwaineShellTokenKind.Word)
                {
                    diagnostic = new DwaineShellDiagnostic(token.Position, "redirection requires a path");
                    return false;
                }

                var kind = token.Kind switch
                {
                    DwaineShellTokenKind.RedirectInput => DwaineShellRedirectionKind.Input,
                    DwaineShellTokenKind.RedirectOutput => DwaineShellRedirectionKind.Output,
                    _ => DwaineShellRedirectionKind.Append,
                };
                redirections.Add(new DwaineShellRedirection(kind, tokens[index].Word!));
                index++;
                continue;
            }

            break;
        }

        if (words.Count == 0)
        {
            var position = index < tokens.Count ? tokens[index].Position : 0;
            diagnostic = new DwaineShellDiagnostic(position, "expected command");
            return false;
        }

        if (redirections.Count(redirection => redirection.Kind == DwaineShellRedirectionKind.Input) > 1
            || redirections.Count(redirection => redirection.Kind != DwaineShellRedirectionKind.Input) > 1)
        {
            diagnostic = new DwaineShellDiagnostic(tokens[Math.Max(0, index - 1)].Position, "duplicate redirection");
            return false;
        }

        command = new DwaineShellCommandNode(words, redirections);
        return true;
    }

    private bool TryLex(
        string source,
        out List<DwaineShellToken> tokens,
        out DwaineShellDiagnostic? diagnostic)
    {
        var tokenList = new List<DwaineShellToken>();
        tokens = tokenList;
        diagnostic = null;
        var segments = new List<DwaineShellWordSegment>();
        var current = new StringBuilder();
        var currentExpand = true;
        var wordStarted = false;
        var wordPosition = 0;

        void FlushSegment()
        {
            if (current.Length == 0)
                return;
            segments.Add(new DwaineShellWordSegment(current.ToString(), currentExpand));
            current.Clear();
        }

        bool FlushWord()
        {
            FlushSegment();
            if (!wordStarted)
                return true;
            if (segments.Count == 0)
                segments.Add(new DwaineShellWordSegment(string.Empty, false));
            tokenList.Add(new DwaineShellToken(
                DwaineShellTokenKind.Word,
                new DwaineShellWord(segments.ToArray()),
                wordPosition));
            segments.Clear();
            wordStarted = false;
            return tokenList.Count <= limits.MaxTokens;
        }

        void SetExpansion(bool expand)
        {
            if (currentExpand == expand)
                return;
            FlushSegment();
            currentExpand = expand;
        }

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsWhiteSpace(character))
            {
                if (!FlushWord())
                    return TooManyTokens(index, out diagnostic);
                continue;
            }

            if (character is '\'' or '"')
            {
                if (!wordStarted)
                {
                    wordStarted = true;
                    wordPosition = index;
                }

                var quote = character;
                SetExpansion(quote == '"');
                var closed = false;
                while (++index < source.Length)
                {
                    character = source[index];
                    if (character == quote)
                    {
                        closed = true;
                        break;
                    }

                    if (character == '\\' && quote == '"')
                    {
                        if (++index >= source.Length)
                            break;
                        current.Append(Unescape(source[index]));
                        continue;
                    }

                    if (character == '$' && quote == '"' && index + 1 < source.Length && source[index + 1] == '(')
                    {
                        if (!TryAppendSubstitution(source, ref index, current, out diagnostic))
                            return false;
                        continue;
                    }

                    current.Append(character);
                }

                if (!closed)
                {
                    diagnostic = new DwaineShellDiagnostic(wordPosition, "unterminated quote");
                    return false;
                }

                SetExpansion(true);
                continue;
            }

            if (character == '\\')
            {
                if (!wordStarted)
                {
                    wordStarted = true;
                    wordPosition = index;
                }
                if (++index >= source.Length)
                {
                    diagnostic = new DwaineShellDiagnostic(index - 1, "trailing escape");
                    return false;
                }
                SetExpansion(false);
                current.Append(Unescape(source[index]));
                SetExpansion(true);
                continue;
            }

            if (character == '$' && index + 1 < source.Length && source[index + 1] == '(')
            {
                if (!wordStarted)
                {
                    wordStarted = true;
                    wordPosition = index;
                }
                SetExpansion(true);
                if (!TryAppendSubstitution(source, ref index, current, out diagnostic))
                    return false;
                continue;
            }

            if (character == '&')
            {
                if (index + 1 < source.Length && source[index + 1] == '&')
                {
                    // Handled by the operator lexer below.
                }
                else
                {
                    diagnostic = new DwaineShellDiagnostic(index, "unsupported '&' operator; use &&");
                    return false;
                }
            }

            if (TryOperator(source, index, out var operatorKind, out var consumed))
            {
                if (!FlushWord())
                    return TooManyTokens(index, out diagnostic);
                tokenList.Add(new DwaineShellToken(operatorKind, null, index));
                if (tokenList.Count > limits.MaxTokens)
                    return TooManyTokens(index, out diagnostic);
                index += consumed - 1;
                continue;
            }

            if (!wordStarted)
            {
                wordStarted = true;
                wordPosition = index;
            }
            SetExpansion(true);
            current.Append(character);
        }

        if (!FlushWord())
            return TooManyTokens(source.Length, out diagnostic);
        return true;
    }

    private static bool TryAppendSubstitution(
        string source,
        ref int index,
        StringBuilder target,
        out DwaineShellDiagnostic? diagnostic)
    {
        diagnostic = null;
        var start = index;
        var depth = 1;
        var quote = '\0';
        target.Append("$(");
        index++;
        while (++index < source.Length)
        {
            var character = source[index];
            target.Append(character);
            if (character == '\\')
            {
                if (++index < source.Length)
                    target.Append(source[index]);
                continue;
            }
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character == '(')
                depth++;
            else if (character == ')' && --depth == 0)
                return true;
        }

        diagnostic = new DwaineShellDiagnostic(start, "unterminated command substitution");
        return false;
    }

    private static bool TryOperator(
        string source,
        int index,
        out DwaineShellTokenKind kind,
        out int consumed)
    {
        consumed = 1;
        kind = source[index] switch
        {
            '|' => DwaineShellTokenKind.Pipe,
            ';' => DwaineShellTokenKind.Semicolon,
            '>' => DwaineShellTokenKind.RedirectOutput,
            '<' => DwaineShellTokenKind.RedirectInput,
            _ => (DwaineShellTokenKind) byte.MaxValue,
        };
        if (index + 1 < source.Length)
        {
            if (source[index] == '|' && source[index + 1] == '|')
            {
                kind = DwaineShellTokenKind.OrIf;
                consumed = 2;
            }
            else if (source[index] == '&' && source[index + 1] == '&')
            {
                kind = DwaineShellTokenKind.AndIf;
                consumed = 2;
            }
            else if (source[index] == '>' && source[index + 1] == '>')
            {
                kind = DwaineShellTokenKind.RedirectAppend;
                consumed = 2;
            }
        }

        return kind != (DwaineShellTokenKind) byte.MaxValue;
    }

    private static char Unescape(char character)
    {
        return character switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            _ => character,
        };
    }

    private static bool TooManyTokens(int position, out DwaineShellDiagnostic? diagnostic)
    {
        diagnostic = new DwaineShellDiagnostic(position, "token limit exceeded");
        return false;
    }

    private static DwaineShellParseResult Failure(int position, string message)
    {
        return new DwaineShellParseResult(null, new DwaineShellDiagnostic(position, message));
    }
}
