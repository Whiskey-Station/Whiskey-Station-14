// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Shell;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineShellPrimitivesTest
{
    private static readonly DwaineShellLimits Limits = new(
        2048,
        128,
        8,
        32,
        64,
        64,
        8192,
        16_384,
        4,
        64);

    [Test]
    public void ParserHandlesQuotesEscapesSubstitutionAndBoundedOperators()
    {
        var parser = new DwaineShellParser(Limits);
        var result = parser.Parse("echo \"two words\" 'raw $HOME' escaped\\ value $(echo nested) | grep two && echo ok >> out");

        Assert.That(result.Succeeded, Is.True, result.Diagnostic?.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(result.Line!.Pipelines, Has.Count.EqualTo(2));
            Assert.That(result.Line.Pipelines[0].Commands, Has.Count.EqualTo(2));
            Assert.That(result.Line.Pipelines[1].Condition, Is.EqualTo(DwaineShellChainCondition.OnSuccess));
            Assert.That(result.Line.Pipelines[0].Commands[0].Words[1].Text, Is.EqualTo("two words"));
            Assert.That(result.Line.Pipelines[0].Commands[0].Words[2].Segments.Single().Expand, Is.False);
            Assert.That(result.Line.Pipelines[1].Commands[0].Redirections.Single().Kind,
                Is.EqualTo(DwaineShellRedirectionKind.Append));
        });

        Assert.Multiple(() =>
        {
            Assert.That(parser.Parse("echo 'unterminated").Succeeded, Is.False);
            Assert.That(parser.Parse("echo trailing\\").Succeeded, Is.False);
            Assert.That(parser.Parse("echo x |").Succeeded, Is.False);
            Assert.That(parser.Parse("echo x & echo y").Succeeded, Is.False);
            Assert.That(parser.Parse("echo $(echo ')')").Succeeded, Is.True);
        });

        var tiny = new DwaineShellParser(Limits with { MaxTokens = 3 });
        Assert.That(tiny.Parse("echo one two three").Diagnostic?.Message, Does.Contain("token limit"));
    }

    [Test]
    public void EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection()
    {
        var host = new TestHost();
        var session = host.CreateShellSession(Limits);
        var engine = new DwaineShellEngine(Limits);

        Assert.That(engine.Execute("set GREETING=hello", session, host).ExitCode, Is.Zero);
        var pipeline = engine.Execute("echo \"$GREETING world\" | grep world", session, host);
        var conditional = engine.Execute("if 1 -eq 2 && echo no || echo yes", session, host);
        var substitution = engine.Execute("echo $(echo \"nested ) value\")", session, host);
        var redirect = engine.Execute("echo first > note; echo second >> note; cat < note", session, host);

        Assert.Multiple(() =>
        {
            Assert.That(pipeline.StandardOutput, Is.EqualTo("hello world\n"));
            Assert.That(conditional.StandardOutput, Is.EqualTo("yes\n"));
            Assert.That(substitution.StandardOutput, Is.EqualTo("nested ) value\n"));
            Assert.That(redirect.StandardOutput, Is.EqualTo("first\nsecond\n"));
            Assert.That(session.TryGetEnvironment("GREETING", out var greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("hello"));
            Assert.That(engine.Execute("unset HOME", session, host).ExitCode, Is.EqualTo(1));
            Assert.That(engine.Execute("clear", session, host).ClearScreen, Is.True);
            Assert.That(host.ClearCount, Is.EqualTo(1));
        });

        var tinyEnvironmentLimits = Limits with { MaxEnvironmentCharacters = 45 };
        var tinyEnvironment = host.CreateShellSession(tinyEnvironmentLimits);
        var tinyEnvironmentEngine = new DwaineShellEngine(tinyEnvironmentLimits);
        Assert.That(tinyEnvironmentEngine.Execute("set TOO_LARGE=1234567890", tinyEnvironment, host).ExitCode,
            Is.EqualTo(2));
    }

    [Test]
    public void UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable()
    {
        var host = new TestHost();
        var session = host.CreateShellSession(Limits);
        var engine = new DwaineShellEngine(Limits);

        AssertSuccess(engine.Execute("mkdir -p docs/sub", session, host));
        AssertSuccess(engine.Execute("echo alpha > docs/sub/a", session, host));
        Assert.That(host.FileSystem.TryCreate(
            "docs/record",
            session.WorkingDirectory,
            new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Record,
                Owner = host.Identity.Principal.Value,
                Group = DwaineGroupId.Users.Value,
                Fields = new Dictionary<string, string> { ["subject"] = "alpha record" },
            },
            host.Now,
            out _), Is.EqualTo(DwaineVfsResult.Success));
        var recursiveGrep = engine.Execute("grep -r alpha docs", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(recursiveGrep.StandardOutput, Does.Contain("docs/sub/a:alpha"));
            Assert.That(recursiveGrep.StandardOutput, Does.Contain("docs/record:subject=alpha record"));
        });
        AssertSuccess(engine.Execute("ln docs docs/sub/back", session, host));
        AssertSuccess(engine.Execute("cp docs copy", session, host));
        AssertSuccess(engine.Execute("mv copy/sub/a copy/sub/b", session, host));
        AssertSuccess(engine.Execute("ln copy/sub/b copy/link", session, host));
        Assert.That(engine.Execute("cat copy/link", session, host).StandardOutput, Is.EqualTo("alpha\n"));
        Assert.That(engine.Execute("ls -l copy", session, host).StandardOutput, Does.Contain("link"));
        AssertSuccess(engine.Execute("chmod 600 copy/sub/b", session, host));
        AssertSuccess(engine.Execute("tar -c packed copy", session, host));
        Assert.That(engine.Execute("tar -t packed", session, host).StandardOutput, Does.Contain("sub"));

        Assert.That(host.FileSystem.TryResolve("copy", session.WorkingDirectory, out var copy),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(host.FileSystem.TryGetSnapshot(copy, out var copied), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(copied.Metadata.Owner, Is.EqualTo(host.Identity.Principal.Value));

        var request = engine.Execute("rm -r -i copy", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(request.StandardOutput, Does.Contain("rm --confirm"));
            Assert.That(host.FileSystem.TryResolve("copy", session.WorkingDirectory, out _),
                Is.EqualTo(DwaineVfsResult.Success));
        });
        AssertSuccess(engine.Execute("rm --confirm", session, host));
        Assert.That(host.FileSystem.TryResolve("copy", session.WorkingDirectory, out _),
            Is.EqualTo(DwaineVfsResult.NotFound));
        Assert.That(engine.Execute("rm -r /", session, host).StandardError, Does.Contain("root is protected"));
    }

    [Test]
    public void CredentialsManualsAndPrivilegeBoundariesDoNotLeak()
    {
        var host = new TestHost();
        var session = host.CreateShellSession(Limits);
        var engine = new DwaineShellEngine(Limits);

        AssertSuccess(engine.Execute("echo private > note", session, host));
        AssertSuccess(engine.Execute("echo grouped > group-note", session, host));
        Assert.That(host.FileSystem.TryResolve("group-note", session.WorkingDirectory, out var groupNote),
            Is.EqualTo(DwaineVfsResult.Success));
        host.FileSystem.TryGetSnapshot(groupNote, out var groupedSnapshot);
        Assert.That(host.FileSystem.TrySetMetadata(
            groupNote,
            groupedSnapshot.Metadata.Owner,
            DwaineGroupId.System.Value,
            groupedSnapshot.Metadata.Mode,
            host.Now), Is.EqualTo(DwaineVfsResult.Success));
        AssertSuccess(engine.Execute("chmod 600 note", session, host));
        var elevated = engine.Execute("echo before; su operator operator-password", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(elevated.TerminateProcess, Is.True);
            Assert.That(host.Identity.Principal, Is.EqualTo(host.Operator.Principal));
        });

        var history = engine.Execute("history", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(history.StandardOutput, Does.Contain("<redacted credential command>"));
            Assert.That(history.StandardOutput, Does.Not.Contain("operator-password"));
            Assert.That(engine.Execute("chown bob:users note", session, host).ExitCode, Is.Zero);
            Assert.That(engine.Execute("chown bob group-note", session, host).ExitCode, Is.Zero);
        });

        var variableHost = new TestHost();
        var variableSession = variableHost.CreateShellSession(Limits);
        var variableEngine = new DwaineShellEngine(Limits);
        AssertSuccess(variableEngine.Execute("set AUTH=su", variableSession, variableHost));
        variableEngine.Execute("$AUTH operator operator-password", variableSession, variableHost);
        Assert.That(variableEngine.Execute("history", variableSession, variableHost).StandardOutput,
            Does.Not.Contain("operator-password"));
        Assert.That(host.FileSystem.TryResolve("note", session.WorkingDirectory, out var note),
            Is.EqualTo(DwaineVfsResult.Success));
        host.FileSystem.TryGetSnapshot(note, out var noteSnapshot);
        Assert.That(noteSnapshot.Metadata.Owner, Is.EqualTo(host.Bob.Principal.Value));
        host.FileSystem.TryGetSnapshot(groupNote, out groupedSnapshot);
        Assert.That(groupedSnapshot.Metadata.Group, Is.EqualTo(DwaineGroupId.System.Value));

        foreach (var command in engine.CommandNames)
            Assert.That(engine.Execute($"help {command}", session, host).ExitCode, Is.Zero, command);
        Assert.That(engine.Execute("missing-command", session, host).ExitCode, Is.EqualTo(127));
    }

    [Test]
    public void NestedEvaluationOutputRegexAndLogicalWaitRemainBounded()
    {
        var host = new TestHost();
        var session = host.CreateShellSession(Limits);
        var engine = new DwaineShellEngine(Limits);

        var hostile = engine.Execute("while 64 while 64 echo x", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(hostile.TerminateProcess, Is.True);
            Assert.That(hostile.StandardError, Does.Contain("command budget exceeded"));
            Assert.That(hostile.StandardOutput.Length, Is.LessThanOrEqualTo(Limits.MaxOutputCharacters));
        });

        var smallLimits = Limits with { MaxInputLength = 256, MaxOutputCharacters = 32 };
        var smallSession = host.CreateShellSession(smallLimits);
        var smallEngine = new DwaineShellEngine(smallLimits);
        var bounded = smallEngine.Execute($"echo {new string('x', 100)}", smallSession, host);
        Assert.Multiple(() =>
        {
            Assert.That(bounded.StandardOutput, Has.Length.EqualTo(32));
            Assert.That(bounded.ExitCode, Is.EqualTo(1));
            Assert.That(bounded.StandardError, Does.Contain("output limit exceeded"));
        });
        Assert.That(smallEngine.Execute("echo x | grep -E '['", smallSession, host).StandardError,
            Does.Contain("invalid regular expression"));

        AssertSuccess(smallEngine.Execute("sleep 5", smallSession, host));
        Assert.That(smallEngine.Execute("echo early", smallSession, host).StandardError,
            Does.Contain("logical clock"));
        host.Advance(TimeSpan.FromSeconds(5));
        Assert.That(smallEngine.Execute("echo ready", smallSession, host).StandardOutput, Is.EqualTo("ready\n"));
    }

    [Test]
    public void RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries()
    {
        var host = new TestHost();
        var session = host.CreateShellSession(Limits);
        var engine = new DwaineShellEngine(Limits);

        Assert.Multiple(() =>
        {
            Assert.That(engine.Execute("pwd", session, host).StandardOutput, Is.EqualTo("/home/alex\n"));
            Assert.That(engine.Execute("date", session, host).StandardOutput, Is.EqualTo("T+00:00:00.000\n"));
            Assert.That(engine.Execute("getopt ab: -a -b value rest", session, host).StandardOutput,
                Is.EqualTo("option a\noption b=value\narg rest\n"));
            Assert.That(engine.Execute("eval echo safe", session, host).StandardOutput, Is.EqualTo("safe\n"));
            Assert.That(engine.Execute("while 3 echo loop", session, host).StandardOutput,
                Is.EqualTo("loop\nloop\nloop\n"));
            Assert.That(engine.Execute("break", session, host).StandardError, Does.Contain("not inside a loop"));
            Assert.That(engine.Execute("if no = yes; else && echo branch", session, host).StandardOutput,
                Is.EqualTo("branch\n"));
            Assert.That(engine.Execute("who", session, host).StandardOutput, Does.Contain("alex"));
            Assert.That(engine.Execute("talk alex hello", session, host).StandardOutput, Does.Contain("sent to alex"));
            Assert.That(engine.Execute("mesg n; mesg", session, host).StandardOutput, Is.EqualTo("is n\n"));
            Assert.That(engine.Execute("mount", session, host).ExitCode, Is.Zero);
            Assert.That(engine.Execute("scnt", session, host).ExitCode, Is.EqualTo(127));
        });

        var logout = engine.Execute("logout", session, host);
        Assert.Multiple(() =>
        {
            Assert.That(logout.TerminateProcess, Is.True);
            Assert.That(logout.ExitCode, Is.Zero);
            Assert.That(host.Identity.Temporary, Is.True);
        });

        var throttledHost = new TestHost();
        var throttledSession = throttledHost.CreateShellSession(Limits);
        var throttledEngine = new DwaineShellEngine(Limits);
        Assert.That(throttledEngine.Execute("su operator wrong-password", throttledSession, throttledHost).ExitCode,
            Is.EqualTo(1));
        Assert.That(throttledHost.ElevateCalls, Is.EqualTo(1));
        Assert.That(throttledEngine.Execute("su operator wrong-password", throttledSession, throttledHost).StandardError,
            Does.Contain("temporarily throttled"));
        Assert.That(throttledHost.ElevateCalls, Is.EqualTo(1));
        throttledHost.Advance(TimeSpan.FromSeconds(1));
        throttledEngine.Execute("su operator wrong-password", throttledSession, throttledHost);
        Assert.That(throttledHost.ElevateCalls, Is.EqualTo(2));
    }

    [Test]
    public void ElevationBackoffSurvivesTerminalReconnects()
    {
        var identities = new DwaineIdentityStore();
        identities.TryCreateAccount("operator", "operator-password", true, out _);
        identities.TryCreateTemporarySession(10, TimeSpan.Zero, TimeSpan.FromHours(1), out var first);
        Assert.That(identities.TryElevate(first.Session, "operator", "wrong-password", TimeSpan.Zero, out _),
            Is.EqualTo(DwaineIdentityResult.InvalidCredential));
        identities.DisconnectTerminal(10);
        identities.TryCreateTemporarySession(11, TimeSpan.Zero, TimeSpan.FromHours(1), out var second);

        Assert.Multiple(() =>
        {
            Assert.That(identities.TryElevate(second.Session, "operator", "operator-password", TimeSpan.Zero, out _),
                Is.EqualTo(DwaineIdentityResult.Throttled));
            Assert.That(identities.TryElevate(second.Session, "operator", "operator-password", TimeSpan.FromSeconds(1), out _),
                Is.EqualTo(DwaineIdentityResult.Success));
        });
    }

    private static void AssertSuccess(DwaineShellExecutionResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }

    private sealed class TestHost : IDwaineShellHost
    {
        private DwaineIdentitySessionSnapshot _identity;

        public TimeSpan Now { get; private set; }
        public DwaineIdentitySessionSnapshot Identity => _identity;
        public DwaineIdentityStore Identities { get; } = new();
        public DwaineVirtualFileSystem FileSystem { get; }
        public DwaineAuthorizedFileSystem Files { get; }
        public DwaineAccountSnapshot Alex { get; }
        public DwaineAccountSnapshot Bob { get; }
        public DwaineAccountSnapshot Operator { get; }
        public int ClearCount { get; private set; }
        public int ElevateCalls { get; private set; }

        public TestHost()
        {
            Identities.TryCreateAccount("alex", "alex-password", false, out var alex);
            Identities.TryCreateAccount("bob", "bob-password", false, out var bob);
            Identities.TryCreateAccount("operator", "operator-password", true, out var systemOperator);
            Alex = alex;
            Bob = bob;
            Operator = systemOperator;
            Assert.That(Identities.TryLogin("alex", "alex-password", 1, Now, TimeSpan.FromHours(1), out _identity),
                Is.EqualTo(DwaineIdentityResult.Success));

            FileSystem = new DwaineVirtualFileSystem(new DwaineFileSystemComponent(), Now);
            Assert.That(FileSystem.TryCreate(
                "/home/alex",
                FileSystem.Root,
                new DwaineVfsCreateRequest
                {
                    Kind = DwaineVfsNodeKind.Directory,
                    Owner = Alex.Principal.Value,
                    Group = DwaineGroupId.Users.Value,
                    Mode = DwaineVfsMode.OwnerAll | DwaineVfsMode.GroupReadExecute,
                },
                Now,
                out _), Is.EqualTo(DwaineVfsResult.Success));
            Files = new DwaineAuthorizedFileSystem(FileSystem, Identities);
        }

        public DwaineShellSession CreateShellSession(DwaineShellLimits limits)
        {
            var shell = new DwaineShellSession(limits);
            shell.InitializeEnvironment("alex", "/home/alex");
            Assert.That(FileSystem.TryResolve("/home/alex", FileSystem.Root, out shell.WorkingDirectory),
                Is.EqualTo(DwaineVfsResult.Success));
            return shell;
        }

        public void Advance(TimeSpan amount)
        {
            Now += amount;
        }

        public DwaineVfsResult TryGetPath(DwaineVfsNodeHandle handle, out string path)
        {
            return FileSystem.TryGetPath(handle, out path);
        }

        public DwaineVfsResult TryCanonicalize(string path, DwaineVfsNodeHandle workingDirectory, out string canonical)
        {
            return FileSystem.TryCanonicalize(path, workingDirectory, out canonical);
        }

        public DwaineIdentityResult TryElevate(string name, string password, out DwaineIdentitySessionSnapshot session)
        {
            ElevateCalls++;
            var result = Identities.TryElevate(_identity.Session, name, password, Now, out session);
            if (result == DwaineIdentityResult.Success)
                _identity = session;
            return result;
        }

        public DwaineIdentityResult TryLogout(out DwaineIdentitySessionSnapshot session)
        {
            var terminal = _identity.Terminal;
            if (!Identities.Logout(_identity.Session))
            {
                session = default;
                return DwaineIdentityResult.SessionNotFound;
            }
            var result = Identities.TryCreateTemporarySession(terminal, Now, TimeSpan.FromHours(1), out session);
            if (result == DwaineIdentityResult.Success)
                _identity = session;
            return result;
        }

        public IReadOnlyList<DwaineShellUserEntry> GetUsers()
        {
            return Identities.GetSessions(Now)
                .Select(entry => new DwaineShellUserEntry(
                    Identities.TryGetAccount(entry.Principal, out var account) ? account.Name : "unknown",
                    entry.Temporary,
                    true))
                .ToArray();
        }

        public DwaineShellHostResult Talk(string target, string message)
        {
            return DwaineShellHostResult.Success($"sent to {target}: {message}\n");
        }

        public DwaineShellHostResult Mount(string label, string path)
        {
            return DwaineShellHostResult.Failure("mount: no test media\n");
        }

        public DwaineShellHostResult Unmount(string label)
        {
            return DwaineShellHostResult.Failure("mount: no test media\n");
        }

        public DwaineShellHostResult ListMedia()
        {
            return DwaineShellHostResult.Success();
        }

        public void ClearScreen()
        {
            ClearCount++;
        }
    }
}
