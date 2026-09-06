// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineIdentityPrimitivesTest
{
    [Test]
    public void CredentialsSessionsAndExpiryAreServerAuthoritative()
    {
        var identities = new DwaineIdentityStore();
        Assert.That(identities.TryCreateAccount("alex", "correct horse", false, out var account), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.That(identities.TryLogin("alex", "wrong", 10, TimeSpan.Zero, TimeSpan.FromMinutes(5), out _), Is.EqualTo(DwaineIdentityResult.InvalidCredential));
        Assert.That(identities.TryLogin("alex", "correct horse", 10, TimeSpan.Zero, TimeSpan.FromMinutes(5), out var login), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(login.Principal, Is.EqualTo(account.Principal));
            Assert.That(login.Terminal, Is.EqualTo(10));
            Assert.That(identities.TryGetSession(login.Session, TimeSpan.FromMinutes(4), out _), Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identities.TryGetSession(login.Session, TimeSpan.FromMinutes(5), out _), Is.EqualTo(DwaineIdentityResult.SessionExpired));
            Assert.That(identities.SessionCount, Is.Zero);
        });
    }

    [Test]
    public void TemporarySessionsAreRevokedOnDisconnectAndElevation()
    {
        var identities = new DwaineIdentityStore();
        identities.TryCreateAccount("operator", "safe-password", true, out var operatorAccount);
        Assert.That(identities.TryCreateTemporarySession(20, TimeSpan.Zero, TimeSpan.FromHours(1), out var guest), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.That(identities.TryElevate(guest.Session, "operator", "safe-password", TimeSpan.Zero, out var elevated), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(elevated.Principal, Is.EqualTo(operatorAccount.Principal));
            Assert.That(elevated.Temporary, Is.False);
            Assert.That(identities.TryGetAccount(guest.Principal, out _), Is.False);
            Assert.That(identities.DisconnectTerminal(20), Is.True);
            Assert.That(identities.SessionCount, Is.Zero);
        });
    }

    [Test]
    public void OperatorsManageGroupsAndDisabledUsersLoseSessions()
    {
        var identities = new DwaineIdentityStore();
        identities.TryCreateAccount("operator", "safe-password", true, out var sysop);
        identities.TryCreateAccount("user", "user-password", false, out var user);
        identities.TryLogin("user", "user-password", 30, TimeSpan.Zero, TimeSpan.FromHours(1), out _);

        Assert.That(identities.TryCreateGroup(user.Principal, "engineering", out _), Is.EqualTo(DwaineIdentityResult.AccessDenied));
        Assert.That(identities.TryCreateGroup(sysop.Principal, "engineering", out var group), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.That(identities.TrySetGroupMembership(sysop.Principal, user.Principal, group, true), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.That(identities.IsInGroup(user.Principal, group), Is.True);
        Assert.That(identities.TrySetAccountEnabled(sysop.Principal, user.Principal, false), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(identities.SessionCount, Is.Zero);
            Assert.That(identities.TryLogin("user", "user-password", 30, TimeSpan.Zero, TimeSpan.FromHours(1), out _), Is.EqualTo(DwaineIdentityResult.Disabled));
        });
    }

    [Test]
    public void DeletingUsersRevokesSessionsAndDoesNotDeleteOnDeniedRequests()
    {
        var identities = new DwaineIdentityStore();
        identities.TryCreateAccount("operator", "safe-password", true, out var sysop);
        identities.TryCreateAccount("user", "user-password", false, out var user);
        identities.TryLogin("user", "user-password", 31, TimeSpan.Zero, TimeSpan.FromHours(1), out _);

        Assert.That(identities.TryDeleteAccount(user.Principal, sysop.Principal), Is.EqualTo(DwaineIdentityResult.AccessDenied));
        Assert.That(identities.TryGetAccount(sysop.Principal, out _), Is.True);
        Assert.That(identities.TryDeleteAccount(sysop.Principal, user.Principal), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(identities.TryGetAccount(user.Principal, out _), Is.False);
            Assert.That(identities.TryGetSessionForTerminal(31, TimeSpan.Zero, out _), Is.EqualTo(DwaineIdentityResult.SessionNotFound));
            Assert.That(identities.SessionCount, Is.Zero);
        });
    }

    [Test]
    public void UnixModesSelectOwnerGroupAndOtherBits()
    {
        var metadata = new DwaineVfsMetadata(
            10,
            20,
            DwaineVfsMode.OwnerRead | DwaineVfsMode.OwnerWrite | DwaineVfsMode.GroupRead | DwaineVfsMode.OtherExecute,
            DwaineVfsNodeFlags.None,
            TimeSpan.Zero,
            TimeSpan.Zero);
        var groupMember = new HashSet<DwaineGroupId> { new(20) };

        Assert.Multiple(() =>
        {
            Assert.That(DwaineFileAccessPolicy.CanAccess(new DwainePrincipalId(10), new HashSet<DwaineGroupId>(), metadata, DwaineIdentityPermission.Read | DwaineIdentityPermission.Write), Is.True);
            Assert.That(DwaineFileAccessPolicy.CanAccess(new DwainePrincipalId(11), groupMember, metadata, DwaineIdentityPermission.Read), Is.True);
            Assert.That(DwaineFileAccessPolicy.CanAccess(new DwainePrincipalId(11), groupMember, metadata, DwaineIdentityPermission.Write), Is.False);
            Assert.That(DwaineFileAccessPolicy.CanAccess(new DwainePrincipalId(12), new HashSet<DwaineGroupId>(), metadata, DwaineIdentityPermission.Execute), Is.True);
            Assert.That(DwaineFileAccessPolicy.CanAccess(DwainePrincipalId.System, new HashSet<DwaineGroupId>(), metadata, DwaineIdentityPermission.Write), Is.True);
        });
    }

    [Test]
    public void NamesCapacityAndTerminalReplacementStayBounded()
    {
        var identities = new DwaineIdentityStore(2, 3, 1);
        Assert.That(identities.TryCreateAccount("bad name", "password", false, out _), Is.EqualTo(DwaineIdentityResult.InvalidName));
        identities.TryCreateAccount("first", "password", false, out _);
        Assert.That(identities.TryLogin("first", "password", 40, TimeSpan.Zero, TimeSpan.FromHours(99), out var first), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.That(first.ExpiresAt, Is.EqualTo(DwaineIdentityStore.MaximumSessionLifetime));
        Assert.That(identities.TryLogin("first", "password", 40, TimeSpan.Zero, TimeSpan.FromMinutes(1), out var replacement), Is.EqualTo(DwaineIdentityResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(replacement.Session, Is.Not.EqualTo(first.Session));
            Assert.That(identities.SessionCount, Is.EqualTo(1));
            Assert.That(identities.TryCreateAccount("second", "password", false, out _), Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identities.TryCreateAccount("third", "password", false, out _), Is.EqualTo(DwaineIdentityResult.AccountLimit));
        });
    }

    [Test]
    public void AuthorizedVfsEnforcesReadWriteChmodAndChown()
    {
        var identities = new DwaineIdentityStore();
        identities.TryCreateAccount("operator", "safe-password", true, out var sysop);
        identities.TryCreateAccount("owner", "owner-password", false, out var owner);
        identities.TryCreateAccount("other", "other-password", false, out var other);
        var fileSystem = new DwaineVirtualFileSystem(new DwaineFileSystemComponent(), TimeSpan.Zero);
        Assert.That(fileSystem.TryCreate(
            "/home/note",
            fileSystem.Root,
            new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Text,
                Owner = owner.Principal.Value,
                Group = DwaineGroupId.System.Value,
                Mode = DwaineVfsMode.OwnerRead | DwaineVfsMode.OwnerWrite | DwaineVfsMode.OtherRead,
                Text = "initial",
            },
            TimeSpan.Zero,
            out _), Is.EqualTo(DwaineVfsResult.Success));
        var authorized = new DwaineAuthorizedFileSystem(fileSystem, identities);

        Assert.Multiple(() =>
        {
            Assert.That(authorized.TryReadText(other.Principal, "/home/note", fileSystem.Root, out var text), Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(text, Is.EqualTo("initial"));
            Assert.That(authorized.TryWriteText(other.Principal, "/home/note", fileSystem.Root, "forged", false, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.AccessDenied));
            Assert.That(authorized.TryWriteText(owner.Principal, "/home/note", fileSystem.Root, "owned", false, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(authorized.TryChangeMode(other.Principal, "/home/note", fileSystem.Root, DwaineVfsMode.OtherAll, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.AccessDenied));
            Assert.That(authorized.TryChangeMode(new DwainePrincipalId(999), "/home/note", fileSystem.Root, DwaineVfsMode.OtherAll, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.AccessDenied));
            Assert.That(authorized.TryChangeMode(owner.Principal, "/home/note", fileSystem.Root, DwaineVfsMode.OwnerRead, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(authorized.TryChangeOwner(owner.Principal, "/home/note", fileSystem.Root, other.Principal, DwaineGroupId.Users, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.AccessDenied));
            Assert.That(authorized.TryChangeOwner(sysop.Principal, "/home/note", fileSystem.Root, other.Principal, DwaineGroupId.Users, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
        });
    }
}
