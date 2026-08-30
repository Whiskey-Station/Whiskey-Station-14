// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using System.Linq;
using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Hardware;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineHardwarePrimitivesTest
{
    [Test]
    public void TerminalInputValidationRejectsUnboundedAndMultilineData()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DwaineHardwareSystem.TryValidateInput("status", 16, out var valid), Is.True);
            Assert.That(valid, Is.EqualTo("status"));
            Assert.That(DwaineHardwareSystem.TryValidateInput(string.Empty, 16, out _), Is.False);
            Assert.That(DwaineHardwareSystem.TryValidateInput("0123456789", 4, out _), Is.False);
            Assert.That(DwaineHardwareSystem.TryValidateInput(
                new string('x', DwaineTerminalComponent.HardMaxInputLength + 1),
                int.MaxValue,
                out _), Is.False);
            Assert.That(DwaineHardwareSystem.TryValidateInput("one\ntwo", 16, out _), Is.False);
            Assert.That(DwaineHardwareSystem.TryValidateInput("one\0two", 16, out _), Is.False);
        });
    }

    [Test]
    public void ServerOutputBufferEnforcesBothBounds()
    {
        var buffer = new DwaineBoundedTextBuffer(3, 8);
        buffer.Add("one");
        buffer.Add("two");
        buffer.Add("three");

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Snapshot(), Is.EqualTo(new[] { "two", "three" }));
            Assert.That(buffer.Count, Is.LessThanOrEqualTo(3));
            Assert.That(buffer.CharacterCount, Is.LessThanOrEqualTo(8));
        });

        buffer.Add("0123456789");
        Assert.That(buffer.Snapshot(), Is.EqualTo(new[] { "01234567" }));
    }

    [Test]
    public void ClientMessagesContainNoClaimedAuthority()
    {
        var inputFields = typeof(DwaineTerminalInputMessage)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(field => field.Name)
            .ToArray();
        var toggleFields = typeof(DwaineTerminalTogglePowerMessage)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(field => field.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(inputFields, Is.EqualTo(new[] { nameof(DwaineTerminalInputMessage.Text) }));
            Assert.That(toggleFields, Is.Empty);
        });
    }
}
