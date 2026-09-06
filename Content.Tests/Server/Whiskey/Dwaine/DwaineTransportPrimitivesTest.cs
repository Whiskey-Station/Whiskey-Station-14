// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Reflection;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Hardware;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineTransportPrimitivesTest
{
    [Test]
    public void SessionIdentityNeverAppearsInClientMessages()
    {
        var connectFields = typeof(DwaineTerminalConnectMessage)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(field => field.Name)
            .ToArray();
        var disconnectFields = typeof(DwaineTerminalDisconnectMessage)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(connectFields, Is.EqualTo(new[] { nameof(DwaineTerminalConnectMessage.Target) }));
            Assert.That(disconnectFields, Is.Empty);
            Assert.That(typeof(DwaineSessionId).Namespace, Does.StartWith("Content.Server"));
        });
    }

    [Test]
    public void BoundedTransportBufferCanBeConsumedWithoutLosingAccounting()
    {
        var buffer = new DwaineBoundedTextBuffer(2, 8);
        buffer.Add("one");
        buffer.Add("two");

        Assert.That(buffer.TryDequeue(out var first), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("one"));
            Assert.That(buffer.CharacterCount, Is.EqualTo(3));
            Assert.That(buffer.TryDequeue(out var second), Is.True);
            Assert.That(second, Is.EqualTo("two"));
            Assert.That(buffer.TryDequeue(out _), Is.False);
            Assert.That(buffer.CharacterCount, Is.Zero);
        });
    }
}
