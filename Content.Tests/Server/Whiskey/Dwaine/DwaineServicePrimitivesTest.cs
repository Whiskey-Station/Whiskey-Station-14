// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Services;
using NUnit.Framework;
using System;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class DwaineServicePrimitivesTest
{
    private static DwaineServiceLimits Limits(int total = 4, int perUser = 2, int logs = 3)
        => new(total, perUser, 16, 32, logs, 128);

    [Test]
    public void EmailDeliveryIsAtomicOwnerScopedAndBounded()
    {
        var store = new DwaineServiceStore(Limits());
        var alice = new DwainePrincipalId(1);
        var bob = new DwainePrincipalId(2);

        Assert.That(store.TrySendMail("sender", [alice, bob, alice], "subject", "body", TimeSpan.Zero),
            Is.EqualTo(DwaineServiceStatus.Success));
        Assert.Multiple(() =>
        {
            Assert.That(store.ListMail(alice), Has.Length.EqualTo(1));
            Assert.That(store.ListMail(bob), Has.Length.EqualTo(1));
            Assert.That(store.TryReadMail(bob, store.ListMail(alice)[0].Id, out _),
                Is.EqualTo(DwaineServiceStatus.NotFound));
        });

        Assert.That(store.TrySendMail("sender", [alice], "next", "body", TimeSpan.Zero),
            Is.EqualTo(DwaineServiceStatus.Success));
        Assert.That(store.TrySendMail("sender", [alice, new DwainePrincipalId(3)], "overflow", "body", TimeSpan.Zero),
            Is.EqualTo(DwaineServiceStatus.CapacityReached));
        Assert.That(store.ListMail(new DwainePrincipalId(3)), Is.Empty,
            "a rejected multi-recipient delivery must not partially mutate another mailbox");
    }

    [Test]
    public void LogsRotateAndMetricsSaturateWithinConfiguredBounds()
    {
        var store = new DwaineServiceStore(Limits(logs: 3));
        for (var index = 0; index < 10; index++)
        {
            store.Record(
                TimeSpan.FromSeconds(index),
                "operator",
                "diagnostics",
                "snapshot",
                index % 2 == 0 ? DwaineServiceStatus.Success : DwaineServiceStatus.AccessDenied);
        }

        var logs = store.GetLogs(99);
        var metrics = store.GetMetrics();
        Assert.Multiple(() =>
        {
            Assert.That(logs, Has.Length.EqualTo(3));
            Assert.That(logs[0].Sequence, Is.EqualTo(8));
            Assert.That(logs[2].Sequence, Is.EqualTo(10));
            Assert.That(metrics.LogEntries, Is.EqualTo(3));
            Assert.That(metrics.Calls, Is.EqualTo(10));
            Assert.That(metrics.Failures, Is.EqualTo(5));
        });
    }

    [Test]
    public void InvalidOrOversizedMessagesNeverMutateStore()
    {
        var store = new DwaineServiceStore(Limits());
        var recipient = new DwainePrincipalId(1);
        Assert.Multiple(() =>
        {
            Assert.That(store.TrySendMail("sender", [recipient], new string('x', 17), "body", TimeSpan.Zero),
                Is.EqualTo(DwaineServiceStatus.InvalidArguments));
            Assert.That(store.TrySendMail("sender", [recipient], "subject", new string('x', 33), TimeSpan.Zero),
                Is.EqualTo(DwaineServiceStatus.InvalidArguments));
            Assert.That(store.TrySendMail("sender", [recipient], "subject", "bad\0body", TimeSpan.Zero),
                Is.EqualTo(DwaineServiceStatus.InvalidArguments));
            Assert.That(store.GetMetrics().MailMessages, Is.Zero);
        });
    }
}
