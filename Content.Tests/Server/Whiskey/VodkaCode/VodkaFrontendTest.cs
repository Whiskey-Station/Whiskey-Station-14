// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Content.Server._Whiskey.VodkaCode.Frontend;
using Content.Shared._Whiskey.VodkaCode;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.VodkaCode;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class VodkaFrontendTest
{
    [Test]
    public void LexerPreservesDecodedValuesKeywordsAndSourceLocations()
    {
        const string source = "# heading\r\nlet greeting = \"hello\\nworld\";\nreturn greeting;";

        var result = VodkaLexer.Lex(source);

        Assert.That(result.Succeeded, Is.True, FormatDiagnostics(result.Diagnostics));
        Assert.Multiple(() =>
        {
            Assert.That(result.Tokens.Select(token => token.Kind), Is.EqualTo(new[]
            {
                VodkaTokenKind.Let,
                VodkaTokenKind.Identifier,
                VodkaTokenKind.Equal,
                VodkaTokenKind.String,
                VodkaTokenKind.Semicolon,
                VodkaTokenKind.Return,
                VodkaTokenKind.Identifier,
                VodkaTokenKind.Semicolon,
                VodkaTokenKind.EndOfFile,
            }));
            Assert.That(result.Tokens[0].Span.Start.Line, Is.EqualTo(2));
            Assert.That(result.Tokens[0].Span.Start.Column, Is.EqualTo(1));
            Assert.That(result.Tokens[3].StringValue, Is.EqualTo("hello\nworld"));
            Assert.That(result.Tokens[5].Span.Start.Line, Is.EqualTo(3));
            Assert.That(result.Tokens[^1].Span.Start.Offset, Is.EqualTo(source.Length));
        });
    }

    [Test]
    public void LexerRejectsInvalidInputAndEnforcesHardBounds()
    {
        var invalidEscape = VodkaLexer.Lex("let x = \"bad\\q\";");
        var unterminated = VodkaLexer.Lex("let x = \"bad\nreturn x;");
        var invalidUnicode = VodkaLexer.Lex("let x = \"\ud800\";");
        var unexpected = VodkaLexer.Lex("let café = 1;");
        var smallSource = VodkaLexer.Lex("let value = 1234;", VodkaFrontendLimits.Default with { MaxSourceBytes = 8 });
        var fewTokens = VodkaLexer.Lex("a; b; c;", VodkaFrontendLimits.Default with { MaxTokens = 3 });

        Assert.Multiple(() =>
        {
            Assert.That(invalidEscape.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.InvalidEscape), Is.True);
            Assert.That(unterminated.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.UnterminatedString), Is.True);
            Assert.That(invalidUnicode.Diagnostics.Single().Code, Is.EqualTo(VodkaDiagnosticCode.InvalidUnicode));
            Assert.That(unexpected.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.UnexpectedCharacter), Is.True);
            Assert.That(smallSource.Diagnostics.Single().Code, Is.EqualTo(VodkaDiagnosticCode.SourceTooLarge));
            Assert.That(fewTokens.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.TokenLimitExceeded), Is.True);
            Assert.That(fewTokens.Tokens[^1].Kind, Is.EqualTo(VodkaTokenKind.EndOfFile));
        });
    }

    [Test]
    public void ParserBuildsStructuredAstWithStablePrecedenceAndCalls()
    {
        const string source = """
            let total = 1 + 2 * 3;
            if (total >= 7 and not false) {
                console.write("ok");
            } else {
                exit 1;
            }
            while (total < 10) {
                total = total + 1;
                continue;
            }
            return total;
            """;

        var result = VodkaParser.Parse(source);

        Assert.That(result.Succeeded, Is.True, FormatDiagnostics(result.Diagnostics));
        Assert.That(result.Program.Statements, Has.Count.EqualTo(4));

        var declaration = (VodkaLetStatementSyntax) result.Program.Statements[0];
        var addition = (VodkaBinaryExpressionSyntax) declaration.Initializer!;
        var multiplication = (VodkaBinaryExpressionSyntax) addition.Right;
        var conditional = (VodkaIfStatementSyntax) result.Program.Statements[1];
        var conjunction = (VodkaBinaryExpressionSyntax) conditional.Condition;
        var callStatement = (VodkaExpressionStatementSyntax) conditional.Then.Statements.Single();
        var call = (VodkaCallExpressionSyntax) callStatement.Expression;
        var member = (VodkaMemberExpressionSyntax) call.Target;
        var loop = (VodkaWhileStatementSyntax) result.Program.Statements[2];

        Assert.Multiple(() =>
        {
            Assert.That(declaration.Name, Is.EqualTo("total"));
            Assert.That(addition.Operator, Is.EqualTo(VodkaBinaryOperator.Add));
            Assert.That(multiplication.Operator, Is.EqualTo(VodkaBinaryOperator.Multiply));
            Assert.That(conjunction.Operator, Is.EqualTo(VodkaBinaryOperator.And));
            Assert.That(conjunction.Right, Is.TypeOf<VodkaUnaryExpressionSyntax>());
            Assert.That(member.Member, Is.EqualTo("write"));
            Assert.That(((VodkaIdentifierExpressionSyntax) member.Target).Name, Is.EqualTo("console"));
            Assert.That(call.Arguments, Has.Count.EqualTo(1));
            Assert.That(conditional.Else, Is.Not.Null);
            Assert.That(loop.Body.Statements[0], Is.TypeOf<VodkaAssignmentStatementSyntax>());
            Assert.That(loop.Body.Statements[1], Is.TypeOf<VodkaContinueStatementSyntax>());
            Assert.That(result.Program.Statements[3], Is.TypeOf<VodkaReturnStatementSyntax>());
        });
    }

    [Test]
    public void ExitKeywordIsContextualAfterMemberAccess()
    {
        var result = VodkaParser.Parse("sys.process.exit(0);");

        Assert.That(result.Succeeded, Is.True, FormatDiagnostics(result.Diagnostics));
        var statement = (VodkaExpressionStatementSyntax) result.Program.Statements.Single();
        var call = (VodkaCallExpressionSyntax) statement.Expression;
        var exit = (VodkaMemberExpressionSyntax) call.Target;
        Assert.That(exit.Member, Is.EqualTo("exit"));
    }

    [TestCase("let = 1;")]
    [TestCase("let value = ;")]
    [TestCase("if true { return; }")]
    [TestCase("while (true) break;")]
    [TestCase("call(,);")]
    [TestCase("let value = 1")]
    [TestCase("{ let value = 1;")]
    [TestCase("exit 1 2;")]
    public void MalformedCorpusProducesPlayerSafeDiagnostics(string source)
    {
        var result = VodkaParser.Parse(source);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Diagnostics, Is.Not.Empty);
        Assert.That(result.Diagnostics, Has.All.Matches<VodkaDiagnostic>(diagnostic =>
            diagnostic.Span.Start.Line >= 1
            && diagnostic.Span.Start.Column >= 1
            && diagnostic.ToTerminalMessage().StartsWith("vodka: line ", StringComparison.Ordinal)
            && !diagnostic.ToTerminalMessage().Contains("Exception", StringComparison.Ordinal)));
    }

    [Test]
    public void ParserRejectsInvalidControlFlowDepthAndArgumentExhaustion()
    {
        var controlFlow = VodkaParser.Parse("break; continue;");
        var deep = VodkaParser.Parse(
            new string('(', 128) + "true" + new string(')', 128) + ";",
            VodkaFrontendLimits.Default with { MaxSyntaxDepth = 8 });
        var arguments = VodkaParser.Parse(
            "call(1, 2, 3, 4);",
            VodkaFrontendLimits.Default with { MaxArguments = 3 });
        var nestedCalls = VodkaParser.Parse(
            string.Concat(Enumerable.Repeat("call(", 128)) + "true" + new string(')', 128) + ";",
            VodkaFrontendLimits.Default with { MaxSyntaxDepth = 8 });

        Assert.Multiple(() =>
        {
            Assert.That(controlFlow.Diagnostics.Count(d => d.Code == VodkaDiagnosticCode.InvalidControlFlow),
                Is.EqualTo(2));
            Assert.That(deep.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.SyntaxDepthExceeded), Is.True);
            Assert.That(arguments.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.ArgumentLimitExceeded), Is.True);
            Assert.That(nestedCalls.Diagnostics.Any(d => d.Code == VodkaDiagnosticCode.SyntaxDepthExceeded), Is.True);
            Assert.That(deep.Diagnostics.Count, Is.LessThanOrEqualTo(128));
            Assert.That(nestedCalls.Diagnostics.Count, Is.LessThanOrEqualTo(128));
        });
    }

    [Test]
    public void ParserFuzzSeedsAreDeterministicBoundedAndNeverThrow()
    {
        const string alphabet = "abcXYZ019_{}();,.=!-+*/%<>#\"\\ \t\r\nç\0";
        var random = new Random(0x564F444B);
        var limits = VodkaFrontendLimits.Default with
        {
            MaxSourceBytes = 1024,
            MaxTokens = 256,
            MaxDiagnostics = 16,
            MaxSyntaxDepth = 12,
            MaxArguments = 8,
        };

        for (var fixture = 0; fixture < 256; fixture++)
        {
            var length = random.Next(0, 512);
            var builder = new StringBuilder(length);
            for (var index = 0; index < length; index++)
                builder.Append(alphabet[random.Next(alphabet.Length)]);

            var source = builder.ToString();
            VodkaParseResult first = null!;
            VodkaParseResult second = null!;
            Assert.DoesNotThrow(() => first = VodkaParser.Parse(source, limits), $"fixture {fixture}");
            Assert.DoesNotThrow(() => second = VodkaParser.Parse(source, limits), $"fixture {fixture}");

            Assert.Multiple(() =>
            {
                Assert.That(first!.Diagnostics.Count, Is.LessThanOrEqualTo(limits.MaxDiagnostics));
                Assert.That(first.Tokens.Count, Is.LessThanOrEqualTo(limits.MaxTokens + 1));
                Assert.That(second!.Diagnostics, Is.EqualTo(first.Diagnostics));
                Assert.That(second.Tokens, Is.EqualTo(first.Tokens));
            });
        }
    }

    [Test]
    public void EmbeddedVodkaFixturesAndCanonicalExtensionMatchTheSpecification()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(VodkaCodeSpecification.FileExtension, StringComparison.Ordinal))
            .Where(name => name.Contains(".Fixtures.Valid.", StringComparison.Ordinal)
                           || name.Contains(".Fixtures.Invalid.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(VodkaCodeSpecification.FileExtension, Is.EqualTo(".vodka"));
        Assert.That(resources, Has.Length.EqualTo(4));

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.That(stream, Is.Not.Null, resource);
            using var reader = new StreamReader(stream!);
            var result = VodkaParser.Parse(reader.ReadToEnd());
            var shouldBeValid = resource.Contains(".Valid.", StringComparison.Ordinal);
            Assert.That(result.Succeeded, Is.EqualTo(shouldBeValid), $"{resource}: {FormatDiagnostics(result.Diagnostics)}");
        }
    }

    private static string FormatDiagnostics(IEnumerable<VodkaDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToTerminalMessage()));
    }
}
