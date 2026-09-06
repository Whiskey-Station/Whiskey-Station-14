// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.NanoXp;
using Content.Shared._Whiskey.NanoXp;
using NUnit.Framework;
using System;

namespace Content.Tests.Server.Whiskey.NanoXp;

[TestFixture]
public sealed class NanoXpAccountStoreTest
{
    [Test]
    public void PdaEnrollmentDerivesUniqueStationAddressesAndBindsIdentity()
    {
        var store = new NanoXpAccountStore();
        Assert.That(
            store.TryEnroll("id-1", "Dwaiane Álvarez", "Engineer", "Engineering", ["Engineering"], "safe pass", out var first),
            Is.EqualTo(NanoXpAccountResult.Success));
        Assert.That(
            store.TryEnroll("id-2", "Dwaiane Alvarez", "Engineer", "Engineering", ["Engineering"], "other pass", out var second),
            Is.EqualTo(NanoXpAccountResult.Success));

        Assert.Multiple(() =>
        {
            Assert.That(first.Address, Is.EqualTo("dwaiane-alvarez@gmail.nano"));
            Assert.That(second.Address, Is.EqualTo("dwaiane-alvarez2@gmail.nano"));
            Assert.That(store.TryGetByIdentity("id-1", out var bound), Is.True);
            Assert.That(bound.Principal, Is.EqualTo(first.Principal));
            Assert.That(bound.AccessTags, Does.Contain("Engineering"));
            Assert.That(store.AccountCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void CredentialsStayServerSideAndSessionsAreBoundedAndRevocable()
    {
        var store = new NanoXpAccountStore(2, 1);
        store.TryEnroll("id-1", "Alex", "Doctor", "Medical", ["Medical"], "correct horse", out var account);

        Assert.That(
            store.TryLogin(account.Address, "wrong", 1, TimeSpan.Zero, TimeSpan.FromHours(1), out _),
            Is.EqualTo(NanoXpAccountResult.InvalidCredential));
        Assert.That(
            store.TryLogin(account.Address, "correct horse", 1, TimeSpan.Zero, TimeSpan.FromHours(1), out var session),
            Is.EqualTo(NanoXpAccountResult.Success));
        Assert.That(
            store.TryLogin(account.Address, "correct horse", 2, TimeSpan.Zero, TimeSpan.FromHours(1), out _),
            Is.EqualTo(NanoXpAccountResult.SessionLimit));
        Assert.That(store.TryGetLiveSession(session, TimeSpan.FromMinutes(59), out var live), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(live.Address, Is.EqualTo(account.Address));
            Assert.That(store.Disconnect(session), Is.True);
            Assert.That(store.SessionCount, Is.Zero);
        });
    }

    [Test]
    public void GMailIsStationLocalValidatedAndMailboxBounded()
    {
        var store = new NanoXpAccountStore();
        store.TryEnroll("id-1", "Sender", "Assistant", "General", [], "one", out var sender);
        store.TryEnroll("id-2", "Recipient", "Scientist", "Science", ["Research"], "two", out var recipient);

        Assert.Multiple(() =>
        {
            Assert.That(
                store.TrySendMail(sender.Principal, "missing@gmail.nano", "Hello", "Body", 1),
                Is.EqualTo(NanoXpAccountResult.UnknownRecipient));
            Assert.That(
                store.TrySendMail(sender.Principal, recipient.Address, string.Empty, "Body", 1),
                Is.EqualTo(NanoXpAccountResult.InvalidMail));
            Assert.That(
                store.TrySendMail(sender.Principal, recipient.Address, "Hello", "Body", 1),
                Is.EqualTo(NanoXpAccountResult.Success));
        });

        for (var i = 1; i < NanoXpLimits.MaxMailboxMessages; i++)
        {
            Assert.That(
                store.TrySendMail(sender.Principal, recipient.Address, $"Message {i}", "Body", i + 1),
                Is.EqualTo(NanoXpAccountResult.Success));
        }

        Assert.Multiple(() =>
        {
            Assert.That(store.GetInbox(recipient.Principal), Has.Length.EqualTo(NanoXpLimits.MaxMailboxMessages));
            Assert.That(store.GetInbox(recipient.Principal)[0].Subject, Is.EqualTo("Message 63"));
            Assert.That(
                store.TrySendMail(sender.Principal, recipient.Address, "Overflow", "Body", 100),
                Is.EqualTo(NanoXpAccountResult.MailboxFull));
        });
    }

    [Test]
    public void AddressParserRejectsForeignNetworksAndOversizedInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NanoXpAccountStore.TryNormalizeAddress("ALEX", out var local), Is.True);
            Assert.That(local, Is.EqualTo("alex@gmail.nano"));
            Assert.That(NanoXpAccountStore.TryNormalizeAddress("alex@example.com", out _), Is.False);
            Assert.That(
                NanoXpAccountStore.TryNormalizeAddress(new string('a', NanoXpLimits.MaxAddressLength + 1), out _),
                Is.False);
        });
    }
}
