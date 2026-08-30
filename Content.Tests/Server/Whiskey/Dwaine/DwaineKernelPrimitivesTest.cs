// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Kernel;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineKernelPrimitivesTest
{
    [Test]
    public void ServiceRegistryIsBoundedAndShutsDownInReverseOrder()
    {
        var calls = new List<string>();
        var registry = new DwaineKernelServiceRegistry(2);
        var first = new TestService(_ => calls.Add("first"));
        var second = new TestService(_ =>
        {
            calls.Add("second");
            throw new InvalidOperationException("contained by registry");
        });

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryRegister("first", first), Is.True);
            Assert.That(registry.TryRegister("second", second), Is.True);
            Assert.That(registry.TryRegister("third", first), Is.False);
            Assert.That(registry.TryRegister("INVALID NAME", first), Is.False);
            Assert.That(registry.TryRegister("first", first), Is.False);
        });

        var context = new DwaineKernelShutdownContext(
            EntityUid.Invalid,
            7,
            DwaineKernelShutdownReason.Reboot);
        var failures = registry.ShutdownAll(context);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(failures, Has.Length.EqualTo(1));
            Assert.That(failures[0].ServiceName, Is.EqualTo("second"));
            Assert.That(failures[0].ErrorCode, Is.EqualTo("shutdown-failed"));
            Assert.That(registry.Count, Is.Zero);
        });
    }

    [Test]
    public void ServiceRegistryShutdownRejectsReentrantMutationAndRunsSnapshotOnce()
    {
        var calls = new List<string>();
        var registry = new DwaineKernelServiceRegistry(3);
        var first = new TestService(_ => calls.Add("first"));
        var mutationResults = new List<bool>();
        var second = new TestService(context =>
        {
            calls.Add("second");
            mutationResults.Add(registry.TryUnregister("first"));
            mutationResults.Add(registry.TryRegister("late", first));
            Assert.That(registry.ShutdownAll(context), Is.Empty);
        });

        Assert.That(registry.TryRegister("first", first), Is.True);
        Assert.That(registry.TryRegister("second", second), Is.True);
        var context = new DwaineKernelShutdownContext(
            EntityUid.Invalid,
            8,
            DwaineKernelShutdownReason.Requested);

        Assert.That(registry.ShutdownAll(context), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(mutationResults, Is.EqualTo(new[] { false, false }));
            Assert.That(registry.Count, Is.Zero);
            Assert.That(registry.ShutdownAll(context), Is.Empty);
        });
    }

    [Test]
    public void SystemClockUsesOnlyObservedGameTime()
    {
        var clock = new DwaineSystemClock();
        clock.StartBoot(TimeSpan.FromSeconds(10), 4);
        clock.Observe(TimeSpan.FromSeconds(15));
        var running = clock.Snapshot();
        clock.Stop(TimeSpan.FromSeconds(17));
        clock.Observe(TimeSpan.FromSeconds(30));
        var stopped = clock.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(running.Now, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(running.Uptime, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(running.BootGeneration, Is.EqualTo(4));
            Assert.That(running.Running, Is.True);
            Assert.That(stopped.Now, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(stopped.Uptime, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(stopped.Running, Is.False);
        });
    }

    [Test]
    public void DiagnosticBufferBoundsAndNormalizesPlayerFacingText()
    {
        var diagnostics = new DwaineBootDiagnosticBuffer(2);
        diagnostics.Add(TimeSpan.Zero, DwaineSystemState.PowerOnSelfTest, "first", "line one");
        diagnostics.Add(TimeSpan.Zero, DwaineSystemState.Bootloader, "second", "line\r\ntwo");
        diagnostics.Add(TimeSpan.Zero, DwaineSystemState.SystemReady, new string('c', 80), new string('m', 300));
        var snapshot = diagnostics.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Has.Length.EqualTo(2));
            Assert.That(snapshot[0].Message, Is.EqualTo("line  two"));
            Assert.That(snapshot[1].Code, Has.Length.EqualTo(DwaineBootDiagnosticBuffer.HardMaxCodeLength));
            Assert.That(snapshot[1].Message, Has.Length.EqualTo(DwaineBootDiagnosticBuffer.HardMaxMessageLength));
        });
    }

    private sealed class TestService(Action<DwaineKernelShutdownContext> shutdown) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            shutdown(context);
        }
    }
}
