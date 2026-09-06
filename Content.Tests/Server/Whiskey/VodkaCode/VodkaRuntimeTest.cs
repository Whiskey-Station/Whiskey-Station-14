// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Content.Server._Whiskey.VodkaCode.Runtime;
using Content.Shared._Whiskey.VodkaCode;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.VodkaCode;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class VodkaRuntimeTest
{
    [Test]
    public void VariablesOperatorsScopesAndControlFlowAreDeterministic()
    {
        const string source = """
            let total = 1 + 2 * 3;
            let text = "vo" + "dka";
            if (total == 7 and text == "vodka") {
                let index = 0;
                while (index < 5) {
                    index = index + 1;
                    if (index == 2) { continue; }
                    if (index == 5) { break; }
                    total = total + index;
                }
            } else {
                exit 9;
            }
            console.writeln(total);
            console.writeln(!false eor false);
            return total;
            """;

        var result = Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Returned));
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.ReturnValue, Is.EqualTo(VodkaValue.FromInteger(15)));
            Assert.That(result.Output, Is.EqualTo("15\ntrue\n"));
            Assert.That(result.Error, Is.Empty);
        });
    }

    [Test]
    public void AndOrShortCircuitWithoutEvaluatingFaultingOperands()
    {
        const string source = """
            let first = false and (1 / 0 == 0);
            let second = true or (1 / 0 == 0);
            console.writeln(first);
            console.writeln(second);
            """;

        var result = Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.Output, Is.EqualTo("false\ntrue\n"));
            Assert.That(result.Error, Is.Empty);
        });
    }

    [TestCase("console.writeln(1 / 0);", "division-by-zero")]
    [TestCase("console.writeln(9223372036854775807 + 1);", "integer-overflow")]
    [TestCase("let value = 1; value = \"mixed\" + 1;", "type-error")]
    [TestCase("missing = 1;", "undefined-variable")]
    [TestCase("let same = 1; let same = 2;", "duplicate-variable")]
    [TestCase("if (1) { print(1); }", "type-error")]
    [TestCase("exit 9223372036854775807;", "invalid-exit-code")]
    [TestCase("unknown.call();", "unknown-function")]
    [TestCase("stack.pop();", "stack-underflow")]
    public void RuntimeErrorsAreStableAndPlayerSafe(string source, string expectedCode)
    {
        var result = Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Faulted));
            Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
            Assert.That(result.Error, Does.StartWith("vodka: line ").Or.StartWith("vodka: process terminated:"));
            Assert.That(result.Error, Does.Not.Contain("Exception"));
            Assert.That(result.Error, Does.Not.Contain(" at Content."));
        });
    }

    [Test]
    public void InstructionBudgetTerminatesInfiniteLoopAcrossSlices()
    {
        var limits = VodkaRuntimeLimits.Default with { MaxInstructions = 80 };
        var result = Run("while (true) { let value = 1; }", limits, slice: 7);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Faulted));
            Assert.That(result.ErrorCode, Is.EqualTo("instruction-budget-exceeded"));
            Assert.That(result.Instructions, Is.EqualTo(80));
            Assert.That(result.Error, Does.Contain("instruction budget exceeded"));
        });
    }

    [Test]
    public void HostCallsProgressWithSingleInstructionSlices()
    {
        var result = Run("console.writeln(fs.exists(\"/visible\"));", slice: 1, host: new TestHost());

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(result.Output, Is.EqualTo("true\n"));
            Assert.That(result.Instructions, Is.GreaterThan(0));
        });
    }

    [Test]
    public void LongStringOperationsConsumeWeightedInstructions()
    {
        var shortString = Run("string.upper(\"x\");");
        var longString = Run("string.upper(string.repeat(\"x\", 4096));");

        Assert.Multiple(() =>
        {
            Assert.That(shortString.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(longString.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(longString.Instructions, Is.GreaterThan(shortString.Instructions + 20));
        });
    }

    [Test]
    public void ExcessiveStaticMemberChainFailsCompilationWithoutRecursion()
    {
        var source = "root" + string.Concat(Enumerable.Repeat(".member", 2_000)) + "();";
        VodkaCompilationResult compilation = default!;

        Assert.DoesNotThrow(() => compilation = VodkaCompiler.Compile(source));
        Assert.Multiple(() =>
        {
            Assert.That(compilation.Succeeded, Is.False);
            Assert.That(compilation.Diagnostics.Any(diagnostic =>
                diagnostic.Code == Content.Server._Whiskey.VodkaCode.Frontend.VodkaDiagnosticCode.UnexpectedToken), Is.True);
        });
    }

    [Test]
    public void StringDataStackAndOutputLimitsFailClosed()
    {
        var stringLimit = Run(
            "console.write(string.repeat(\"ab\", 20));",
            VodkaRuntimeLimits.Default with { MaxStringBytes = 16 });
        var outputLimit = Run(
            "console.write(\"123456789\");",
            VodkaRuntimeLimits.Default with { MaxOutputBytes = 8 });
        var stackLimit = Run(
            "stack.push(1); stack.push(2); stack.push(3);",
            VodkaRuntimeLimits.Default with { MaxCompatibilityStack = 2 });
        var variableLimit = Run(
            "let a = 1; let b = 2;",
            VodkaRuntimeLimits.Default with { MaxVariables = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(stringLimit.ErrorCode, Is.EqualTo("string-limit-exceeded"));
            Assert.That(outputLimit.ErrorCode, Is.EqualTo("output-limit-exceeded"));
            Assert.That(stackLimit.ErrorCode, Is.EqualTo("stack-limit-exceeded"));
            Assert.That(variableLimit.ErrorCode, Is.EqualTo("data-limit-exceeded"));
        });
    }

    [Test]
    public void RandomArgumentsStringsAndCompatibilityStackHaveStableBehavior()
    {
        const string source = """
            console.writeln(args.count());
            console.writeln(args.get(1));
            console.writeln(string.length("A😀B"));
            console.writeln(string.slice("A😀B", 1, 1));
            console.writeln(string.contains("vodka", "od"));
            console.writeln(string.upper("mix"));
            stack.push(7);
            stack.dup();
            console.writeln(stack.depth());
            stack.pop();
            stack.drop();
            console.writeln(rand(100));
            console.writeln(rand(100));
            """;

        var first = Run(source, arguments: ["zero", "one"], seed: 0xD00D);
        var second = Run(source, arguments: ["zero", "one"], seed: 0xD00D);
        var different = Run(source, arguments: ["zero", "one"], seed: 0xD00E);

        Assert.Multiple(() =>
        {
            Assert.That(first.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(first.Output, Is.EqualTo(second.Output));
            Assert.That(first.Output, Does.StartWith("2\none\n3\n😀\ntrue\nMIX\n2\n7\n"));
            Assert.That(different.Output, Is.Not.EqualTo(first.Output));
        });
    }

    [Test]
    public void FilePredicatesUseOnlyTheNarrowHostAndDoNotLeakDeniedPaths()
    {
        const string source = """
            console.writeln(fs.exists("/visible"));
            console.writeln(fs.is_file("/visible"));
            console.writeln(fs.is_directory("/directory"));
            console.writeln(fs.is_executable("/program"));
            console.writeln(fs.exists("/denied"));
            """;
        var host = new TestHost();

        var result = Run(source, host: host);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(VodkaExecutionState.Exited));
            Assert.That(result.Output, Is.EqualTo("true\ntrue\ntrue\ntrue\nfalse\n"));
            Assert.That(host.Calls, Is.EqualTo(new[]
            {
                "fs.exists:/visible",
                "fs.is_file:/visible",
                "fs.is_directory:/directory",
                "fs.is_executable:/program",
                "fs.exists:/denied",
            }));
        });
    }

    [Test]
    public void CancellationAndLogicalTimeoutStopOnlyTheCurrentMachine()
    {
        var compilation = Compile("while (true) { let value = 1; }");
        var host = new TestHost();
        var machine = new VodkaVirtualMachine(
            compilation,
            VodkaRuntimeLimits.Default with { LogicalTimeout = TimeSpan.FromSeconds(2) },
            host);

        var first = machine.ExecuteSlice(8);
        host.CurrentTime = TimeSpan.FromSeconds(3);
        var timedOut = machine.ExecuteSlice(8);

        var secondMachine = new VodkaVirtualMachine(compilation, VodkaRuntimeLimits.Default, new TestHost());
        secondMachine.Cancel();
        var cancelled = secondMachine.ExecuteSlice(8);

        Assert.Multiple(() =>
        {
            Assert.That(first.State, Is.EqualTo(VodkaExecutionState.Yielded));
            Assert.That(timedOut.ErrorCode, Is.EqualTo("logical-timeout"));
            Assert.That(cancelled.State, Is.EqualTo(VodkaExecutionState.Cancelled));
            Assert.That(cancelled.ExitCode, Is.EqualTo(130));
        });
    }

    [Test]
    public void FiftyEmbeddedProgramsCompileAndCompleteWithinBounds()
    {
        var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.Contains(".Fixtures.Runtime.", StringComparison.Ordinal)
                           && name.EndsWith(VodkaCodeSpecification.FileExtension, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(resources, Has.Length.EqualTo(50));
        foreach (var resource in resources)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            Assert.That(stream, Is.Not.Null, resource);
            using var reader = new StreamReader(stream!);
            var result = Run(reader.ReadToEnd(), host: new TestHost());
            Assert.Multiple(() =>
            {
                Assert.That(result.State, Is.AnyOf(VodkaExecutionState.Exited, VodkaExecutionState.Returned), resource);
                Assert.That(result.Error, Is.Empty, resource);
                Assert.That(result.Instructions, Is.LessThan(10_000), resource);
            });
        }
    }

    private static TestResult Run(
        string source,
        VodkaRuntimeLimits? limits = null,
        int slice = 64,
        IReadOnlyList<string>? arguments = null,
        ulong seed = 1,
        TestHost? host = null)
    {
        host ??= new TestHost();
        var machine = new VodkaVirtualMachine(
            Compile(source),
            limits ?? VodkaRuntimeLimits.Default,
            host,
            arguments,
            seed);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        VodkaSliceResult result = default;
        for (var index = 0; index < 100_000; index++)
        {
            result = machine.ExecuteSlice(slice);
            stdout.Append(result.StandardOutput);
            stderr.Append(result.StandardError);
            if (result.State is not (VodkaExecutionState.Ready or VodkaExecutionState.Yielded))
                break;
        }
        return new TestResult(
            result.State,
            result.ExitCode,
            result.ReturnValue,
            stdout.ToString(),
            stderr.ToString(),
            result.ErrorCode,
            machine.InstructionsConsumed);
    }

    private static VodkaCompiledProgram Compile(string source)
    {
        var result = VodkaCompiler.Compile(source);
        Assert.That(result.Succeeded, Is.True,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToTerminalMessage())));
        return result.Program!;
    }

    private readonly record struct TestResult(
        VodkaExecutionState State,
        int ExitCode,
        VodkaValue ReturnValue,
        string Output,
        string Error,
        string ErrorCode,
        int Instructions);

    private sealed class TestHost : IVodkaRuntimeHost
    {
        public TimeSpan CurrentTime;
        public TimeSpan Now => CurrentTime;
        public readonly List<string> Calls = [];

        public VodkaHostCallResult Invoke(string name, IReadOnlyList<VodkaValue> arguments)
        {
            if (!name.StartsWith("fs.", StringComparison.Ordinal))
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.UnknownFunction, "unknown function");
            if (arguments.Count != 1 || arguments[0].Kind != VodkaValueKind.String)
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.InvalidArguments, "expected path");
            var path = arguments[0].Text;
            Calls.Add($"{name}:{path}");
            if (path == "/denied" || path == "/missing")
                return VodkaHostCallResult.Success(VodkaValue.FromBoolean(false));
            var value = name switch
            {
                "fs.exists" => true,
                "fs.is_file" => path == "/visible",
                "fs.is_directory" => path == "/directory",
                "fs.is_executable" => path == "/program",
                _ => false,
            };
            return VodkaHostCallResult.Success(VodkaValue.FromBoolean(value));
        }
    }
}
