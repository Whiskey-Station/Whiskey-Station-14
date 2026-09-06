// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Content.Server._Whiskey.Dwaine.Process;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineProcessPrimitivesTest
{
    [Test]
    public void TextStreamsRejectOverflowWithoutLosingUnreadData()
    {
        var stream = new DwaineProcessTextStream(2, 5);

        Assert.Multiple(() =>
        {
            Assert.That(stream.TryWrite("ab"), Is.True);
            Assert.That(stream.TryWrite("cde"), Is.True);
            Assert.That(stream.TryWrite(string.Empty), Is.False);
            Assert.That(stream.TryWrite("overflow"), Is.False);
            Assert.That(stream.Count, Is.EqualTo(2));
            Assert.That(stream.CharacterCount, Is.EqualTo(5));
        });

        Assert.That(stream.TryRead(out var first), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("ab"));
            Assert.That(stream.CharacterCount, Is.EqualTo(3));
            Assert.That(stream.TryWrite("xy"), Is.True);
            Assert.That(stream.Snapshot(), Is.EqualTo(new[] { "cde", "xy" }));
        });
    }

    [Test]
    public void EnvironmentsAreBoundedValidatedAndClonedByValue()
    {
        var environment = new DwaineProcessEnvironment(2, 20);
        Assert.Multiple(() =>
        {
            Assert.That(environment.TrySet("PATH", "/bin"), Is.True);
            Assert.That(environment.TrySet("1BAD", "x"), Is.False);
            Assert.That(environment.TrySet("BAD-NAME", "x"), Is.False);
            Assert.That(environment.TrySet("USER", "ada"), Is.True);
            Assert.That(environment.TrySet("THIRD", "x"), Is.False);
        });

        var clone = environment.Clone();
        Assert.That(clone.TrySet("USER", "grace"), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(environment.TryGet("USER", out var original), Is.True);
            Assert.That(original, Is.EqualTo("ada"));
            Assert.That(clone.TryGet("USER", out var copied), Is.True);
            Assert.That(copied, Is.EqualTo("grace"));
            Assert.That(environment.CharacterCount, Is.LessThanOrEqualTo(20));
            Assert.That(clone.CharacterCount, Is.LessThanOrEqualTo(20));
        });
    }

    [Test]
    public void MailboxesRejectMalformedAndOverCapacityMessages()
    {
        var mailbox = new DwaineProcessMailbox(1, 16);
        var sender = new DwaineProcessId(7);

        Assert.Multiple(() =>
        {
            Assert.That(mailbox.IsValidMessage("reply.ok", "value"), Is.True);
            Assert.That(mailbox.IsValidMessage("INVALID", "value"), Is.False);
            Assert.That(mailbox.IsValidMessage("reply", "bad\0payload"), Is.False);
            Assert.That(mailbox.TryWrite(new DwaineProcessMessage(sender, "reply", "value", TimeSpan.Zero)), Is.True);
            Assert.That(mailbox.TryWrite(new DwaineProcessMessage(sender, "reply", "second", TimeSpan.Zero)), Is.False);
        });

        Assert.That(mailbox.TryRead(out var message), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(message.Sender, Is.EqualTo(sender));
            Assert.That(message.Type, Is.EqualTo("reply"));
            Assert.That(message.Payload, Is.EqualTo("value"));
            Assert.That(mailbox.CharacterCount, Is.Zero);
        });
    }
}
