// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._Whiskey.Dwaine.Identity;

public readonly record struct DwainePrincipalId(ulong Value)
{
    public static readonly DwainePrincipalId System = new(0);
    public bool IsValid => Value != 0;
}

public readonly record struct DwaineGroupId(ulong Value)
{
    public static readonly DwaineGroupId System = new(0);
    public static readonly DwaineGroupId Users = new(1);
    public static readonly DwaineGroupId Operators = new(2);
}

public readonly record struct DwaineIdentitySessionId(ulong Value)
{
    public bool IsValid => Value != 0;
}

[Flags]
public enum DwaineIdentityPermission : ushort
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Execute = 1 << 2,
    ChangeMode = 1 << 3,
    ChangeOwner = 1 << 4,
    ManageUsers = 1 << 5,
    ManageGroups = 1 << 6,
    InspectSessions = 1 << 7,
    All = Read | Write | Execute | ChangeMode | ChangeOwner | ManageUsers | ManageGroups | InspectSessions,
}

public enum DwaineIdentityResult : byte
{
    Success,
    InvalidName,
    InvalidCredential,
    AlreadyExists,
    AccountLimit,
    GroupLimit,
    SessionLimit,
    UnknownAccount,
    UnknownGroup,
    Disabled,
    AccessDenied,
    SessionNotFound,
    SessionExpired,
    Throttled,
}

public readonly record struct DwaineAccountSnapshot(
    DwainePrincipalId Principal,
    string Name,
    bool Temporary,
    bool Enabled,
    IReadOnlySet<DwaineGroupId> Groups);

public readonly record struct DwaineIdentitySessionSnapshot(
    DwaineIdentitySessionId Session,
    DwainePrincipalId Principal,
    ulong Terminal,
    TimeSpan CreatedAt,
    TimeSpan ExpiresAt,
    bool Temporary);

internal sealed class DwaineAccount
{
    public required DwainePrincipalId Principal;
    public required string Name;
    public required bool Temporary;
    public bool Enabled = true;
    public byte[] PasswordSalt = [];
    public byte[] PasswordHash = [];
    public readonly HashSet<DwaineGroupId> Groups = [];
}

internal sealed class DwaineIdentitySession
{
    public required DwaineIdentitySessionId Id;
    public required DwainePrincipalId Principal;
    public required ulong Terminal;
    public required TimeSpan CreatedAt;
    public required TimeSpan ExpiresAt;
    public required bool Temporary;
}

/// <summary>
/// Bounded, server-only account and login registry. Credentials and session identifiers never enter a net component.
/// </summary>
public sealed class DwaineIdentityStore
{
    public const int HardMaxAccounts = 4096;
    public const int HardMaxGroups = 256;
    public const int HardMaxSessions = 2048;
    public const int HardMaxNameLength = 32;
    public const int HardMaxPasswordLength = 256;
    public static readonly TimeSpan MaximumSessionLifetime = TimeSpan.FromHours(24);

    private const int PasswordIterations = 100_000;
    private const int PasswordHashLength = 32;
    private const int PasswordSaltLength = 16;

    private readonly Dictionary<DwainePrincipalId, DwaineAccount> _accounts = new();
    private readonly Dictionary<string, DwainePrincipalId> _accountsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DwaineGroupId, string> _groups = new();
    private readonly Dictionary<DwaineIdentitySessionId, DwaineIdentitySession> _sessions = new();
    private readonly Dictionary<ulong, DwaineIdentitySessionId> _sessionsByTerminal = new();
    private readonly Dictionary<DwainePrincipalId, (int Failures, TimeSpan NextAttempt)> _elevationThrottle = new();
    private readonly int _accountCapacity;
    private readonly int _groupCapacity;
    private readonly int _sessionCapacity;
    private ulong _nextPrincipal = 1;
    private ulong _nextGroup = 3;
    private ulong _nextSession = 1;

    public int AccountCount => _accounts.Count;
    public int SessionCount => _sessions.Count;

    public DwaineIdentityStore(int accountCapacity = 512, int groupCapacity = 64, int sessionCapacity = 256)
    {
        if (accountCapacity is <= 0 or > HardMaxAccounts)
            throw new ArgumentOutOfRangeException(nameof(accountCapacity));
        if (groupCapacity is < 3 or > HardMaxGroups)
            throw new ArgumentOutOfRangeException(nameof(groupCapacity));
        if (sessionCapacity is <= 0 or > HardMaxSessions)
            throw new ArgumentOutOfRangeException(nameof(sessionCapacity));

        _accountCapacity = accountCapacity;
        _groupCapacity = groupCapacity;
        _sessionCapacity = sessionCapacity;
        _groups.Add(DwaineGroupId.System, "system");
        _groups.Add(DwaineGroupId.Users, "users");
        _groups.Add(DwaineGroupId.Operators, "operators");
    }

    public DwaineIdentityResult TryCreateAccount(
        string name,
        string password,
        bool systemOperator,
        out DwaineAccountSnapshot account)
    {
        account = default;
        if (!IsValidName(name))
            return DwaineIdentityResult.InvalidName;
        if (!IsValidPassword(password))
            return DwaineIdentityResult.InvalidCredential;
        if (_accountsByName.ContainsKey(name))
            return DwaineIdentityResult.AlreadyExists;
        if (_accounts.Count >= _accountCapacity || !TryAllocatePrincipal(out var principal))
            return DwaineIdentityResult.AccountLimit;

        var salt = RandomNumberGenerator.GetBytes(PasswordSaltLength);
        var created = new DwaineAccount
        {
            Principal = principal,
            Name = name,
            Temporary = false,
            PasswordSalt = salt,
            PasswordHash = HashPassword(password, salt),
        };
        created.Groups.Add(DwaineGroupId.Users);
        if (systemOperator)
            created.Groups.Add(DwaineGroupId.Operators);

        _accounts.Add(principal, created);
        _accountsByName.Add(name, principal);
        account = Snapshot(created);
        return DwaineIdentityResult.Success;
    }

    public DwaineIdentityResult TryCreateTemporarySession(
        ulong terminal,
        TimeSpan now,
        TimeSpan lifetime,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (terminal == 0)
            return DwaineIdentityResult.AccessDenied;
        if (_accounts.Count >= _accountCapacity || !TryAllocatePrincipal(out var principal))
            return DwaineIdentityResult.AccountLimit;

        var name = $"guest-{principal.Value}";
        var account = new DwaineAccount
        {
            Principal = principal,
            Name = name,
            Temporary = true,
        };
        account.Groups.Add(DwaineGroupId.Users);
        _accounts.Add(principal, account);
        _accountsByName.Add(name, principal);

        var result = TryCreateSession(account, terminal, now, lifetime, out session);
        if (result == DwaineIdentityResult.Success)
            return result;

        _accounts.Remove(principal);
        _accountsByName.Remove(name);
        return result;
    }

    public DwaineIdentityResult TryLogin(
        string name,
        string password,
        ulong terminal,
        TimeSpan now,
        TimeSpan lifetime,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (!_accountsByName.TryGetValue(name, out var principal)
            || !_accounts.TryGetValue(principal, out var account)
            || account.Temporary)
        {
            return DwaineIdentityResult.InvalidCredential;
        }

        if (!account.Enabled)
            return DwaineIdentityResult.Disabled;
        if (!VerifyPassword(account, password))
            return DwaineIdentityResult.InvalidCredential;

        return TryCreateSession(account, terminal, now, lifetime, out session);
    }

    public DwaineIdentityResult TryElevate(
        DwaineIdentitySessionId sessionId,
        string name,
        string password,
        TimeSpan now,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        var sessionResult = TryGetSession(sessionId, now, out var current);
        if (sessionResult != DwaineIdentityResult.Success)
            return sessionResult;
        if (!_accountsByName.TryGetValue(name, out var principal)
            || !_accounts.TryGetValue(principal, out var account)
            || account.Temporary
            || !account.Enabled)
        {
            return DwaineIdentityResult.InvalidCredential;
        }
        if (_elevationThrottle.TryGetValue(principal, out var throttle)
            && now < throttle.NextAttempt)
        {
            return DwaineIdentityResult.Throttled;
        }
        if (!VerifyPassword(account, password))
        {
            var failures = Math.Min(throttle.Failures + 1, 5);
            _elevationThrottle[principal] = (
                failures,
                now + TimeSpan.FromSeconds(1 << (failures - 1)));
            return DwaineIdentityResult.InvalidCredential;
        }
        _elevationThrottle.Remove(principal);

        var live = _sessions[sessionId];
        live.Principal = principal;
        live.Temporary = false;
        session = Snapshot(live);

        if (current.Temporary)
            RemoveTemporaryAccount(current.Principal);
        return DwaineIdentityResult.Success;
    }

    public DwaineIdentityResult TryGetSession(
        DwaineIdentitySessionId sessionId,
        TimeSpan now,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (!_sessions.TryGetValue(sessionId, out var live))
            return DwaineIdentityResult.SessionNotFound;
        if (now < live.ExpiresAt)
        {
            session = Snapshot(live);
            return DwaineIdentityResult.Success;
        }

        RemoveSession(live);
        return DwaineIdentityResult.SessionExpired;
    }

    public bool TryGetAccount(DwainePrincipalId principal, out DwaineAccountSnapshot account)
    {
        if (_accounts.TryGetValue(principal, out var stored))
        {
            account = Snapshot(stored);
            return true;
        }

        account = default;
        return false;
    }

    public bool TryGetAccount(string name, out DwaineAccountSnapshot account)
    {
        if (_accountsByName.TryGetValue(name, out var principal))
            return TryGetAccount(principal, out account);

        account = default;
        return false;
    }

    public bool TryGetGroup(string name, out DwaineGroupId group)
    {
        foreach (var (candidate, candidateName) in _groups)
        {
            if (!string.Equals(candidateName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            group = candidate;
            return true;
        }

        group = default;
        return false;
    }

    public DwaineIdentitySessionSnapshot[] GetSessions(TimeSpan now)
    {
        ExpireSessions(now);
        return _sessions.Values
            .OrderBy(session => session.Terminal)
            .Select(Snapshot)
            .ToArray();
    }

    public DwaineIdentityResult TryGetSessionForTerminal(
        ulong terminal,
        TimeSpan now,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        return _sessionsByTerminal.TryGetValue(terminal, out var sessionId)
            ? TryGetSession(sessionId, now, out session)
            : DwaineIdentityResult.SessionNotFound;
    }

    public bool Logout(DwaineIdentitySessionId sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        RemoveSession(session);
        return true;
    }

    public bool DisconnectTerminal(ulong terminal)
    {
        return _sessionsByTerminal.TryGetValue(terminal, out var session) && Logout(session);
    }

    public int RevokeAllSessions()
    {
        var sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
            RemoveSession(session);
        return sessions.Length;
    }

    public int ExpireSessions(TimeSpan now)
    {
        var expired = _sessions.Values.Where(session => now >= session.ExpiresAt).ToArray();
        foreach (var session in expired)
            RemoveSession(session);
        return expired.Length;
    }

    public DwaineIdentityResult TrySetAccountEnabled(
        DwainePrincipalId actor,
        DwainePrincipalId target,
        bool enabled)
    {
        if (!HasPermission(actor, DwaineIdentityPermission.ManageUsers))
            return DwaineIdentityResult.AccessDenied;
        if (!_accounts.TryGetValue(target, out var account))
            return DwaineIdentityResult.UnknownAccount;

        account.Enabled = enabled;
        if (!enabled)
        {
            foreach (var session in _sessions.Values.Where(entry => entry.Principal == target).ToArray())
                RemoveSession(session);
        }

        return DwaineIdentityResult.Success;
    }

    public DwaineIdentityResult TryDeleteAccount(DwainePrincipalId actor, DwainePrincipalId target)
    {
        if (!HasPermission(actor, DwaineIdentityPermission.ManageUsers))
            return DwaineIdentityResult.AccessDenied;
        if (!_accounts.TryGetValue(target, out var account))
            return DwaineIdentityResult.UnknownAccount;

        foreach (var session in _sessions.Values.Where(entry => entry.Principal == target).ToArray())
            RemoveSession(session);
        _accounts.Remove(target);
        _accountsByName.Remove(account.Name);
        _elevationThrottle.Remove(target);
        return DwaineIdentityResult.Success;
    }

    public DwaineIdentityResult TryCreateGroup(DwainePrincipalId actor, string name, out DwaineGroupId group)
    {
        group = default;
        if (!HasPermission(actor, DwaineIdentityPermission.ManageGroups))
            return DwaineIdentityResult.AccessDenied;
        if (!IsValidName(name))
            return DwaineIdentityResult.InvalidName;
        if (_groups.Values.Contains(name, StringComparer.OrdinalIgnoreCase))
            return DwaineIdentityResult.AlreadyExists;
        if (_groups.Count >= _groupCapacity || !TryAllocateGroup(out group))
            return DwaineIdentityResult.GroupLimit;

        _groups.Add(group, name);
        return DwaineIdentityResult.Success;
    }

    public DwaineIdentityResult TrySetGroupMembership(
        DwainePrincipalId actor,
        DwainePrincipalId target,
        DwaineGroupId group,
        bool member)
    {
        if (!HasPermission(actor, DwaineIdentityPermission.ManageGroups))
            return DwaineIdentityResult.AccessDenied;
        if (!_accounts.TryGetValue(target, out var account))
            return DwaineIdentityResult.UnknownAccount;
        if (!_groups.ContainsKey(group))
            return DwaineIdentityResult.UnknownGroup;
        if (group == DwaineGroupId.Users && !member)
            return DwaineIdentityResult.AccessDenied;

        if (member)
            account.Groups.Add(group);
        else
            account.Groups.Remove(group);
        return DwaineIdentityResult.Success;
    }

    public bool HasPermission(DwainePrincipalId principal, DwaineIdentityPermission permission)
    {
        if (principal == DwainePrincipalId.System)
            return true;
        return _accounts.TryGetValue(principal, out var account)
               && account.Enabled
               && account.Groups.Contains(DwaineGroupId.Operators);
    }

    public bool IsInGroup(DwainePrincipalId principal, DwaineGroupId group)
    {
        return principal == DwainePrincipalId.System && group == DwaineGroupId.System
               || _accounts.TryGetValue(principal, out var account) && account.Groups.Contains(group);
    }

    public bool GroupExists(DwaineGroupId group)
    {
        return _groups.ContainsKey(group);
    }

    public DwaineProcessOwner ToProcessOwner(DwainePrincipalId principal)
    {
        return new DwaineProcessOwner(principal.Value);
    }

    private DwaineIdentityResult TryCreateSession(
        DwaineAccount account,
        ulong terminal,
        TimeSpan now,
        TimeSpan lifetime,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (terminal == 0)
            return DwaineIdentityResult.AccessDenied;
        if (_sessionsByTerminal.ContainsKey(terminal))
            DisconnectTerminal(terminal);
        if (_sessions.Count >= _sessionCapacity || !TryAllocateSession(out var sessionId))
            return DwaineIdentityResult.SessionLimit;

        lifetime = lifetime <= TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : TimeSpan.FromTicks(Math.Min(lifetime.Ticks, MaximumSessionLifetime.Ticks));
        var created = new DwaineIdentitySession
        {
            Id = sessionId,
            Principal = account.Principal,
            Terminal = terminal,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
            Temporary = account.Temporary,
        };
        _sessions.Add(sessionId, created);
        _sessionsByTerminal.Add(terminal, sessionId);
        session = Snapshot(created);
        return DwaineIdentityResult.Success;
    }

    private void RemoveSession(DwaineIdentitySession session)
    {
        _sessions.Remove(session.Id);
        _sessionsByTerminal.Remove(session.Terminal);
        if (session.Temporary)
            RemoveTemporaryAccount(session.Principal);
    }

    private void RemoveTemporaryAccount(DwainePrincipalId principal)
    {
        if (!_accounts.TryGetValue(principal, out var account) || !account.Temporary)
            return;

        _accounts.Remove(principal);
        _accountsByName.Remove(account.Name);
    }

    private bool TryAllocatePrincipal(out DwainePrincipalId principal)
    {
        for (var attempt = 0; attempt <= _accounts.Count; attempt++)
        {
            var value = _nextPrincipal++;
            if (_nextPrincipal == 0)
                _nextPrincipal = 1;
            principal = new DwainePrincipalId(value);
            if (principal.IsValid && !_accounts.ContainsKey(principal))
                return true;
        }

        principal = default;
        return false;
    }

    private bool TryAllocateGroup(out DwaineGroupId group)
    {
        for (var attempt = 0; attempt <= _groups.Count; attempt++)
        {
            var value = _nextGroup++;
            if (_nextGroup < 3)
                _nextGroup = 3;
            group = new DwaineGroupId(value);
            if (!_groups.ContainsKey(group))
                return true;
        }

        group = default;
        return false;
    }

    private bool TryAllocateSession(out DwaineIdentitySessionId session)
    {
        for (var attempt = 0; attempt <= _sessions.Count; attempt++)
        {
            var value = _nextSession++;
            if (_nextSession == 0)
                _nextSession = 1;
            session = new DwaineIdentitySessionId(value);
            if (session.IsValid && !_sessions.ContainsKey(session))
                return true;
        }

        session = default;
        return false;
    }

    private static bool VerifyPassword(DwaineAccount account, string password)
    {
        if (!IsValidPassword(password) || account.PasswordHash.Length != PasswordHashLength)
            return false;
        var candidate = HashPassword(password, account.PasswordSalt);
        return CryptographicOperations.FixedTimeEquals(candidate, account.PasswordHash);
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            PasswordHashLength);
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > HardMaxNameLength)
            return false;
        foreach (var character in name)
        {
            if (!(character is >= 'a' and <= 'z'
                  or >= 'A' and <= 'Z'
                  or >= '0' and <= '9'
                  or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidPassword(string? password)
    {
        return !string.IsNullOrEmpty(password)
               && password.Length <= HardMaxPasswordLength
               && password.IndexOf('\0') < 0;
    }

    private static DwaineAccountSnapshot Snapshot(DwaineAccount account)
    {
        return new DwaineAccountSnapshot(
            account.Principal,
            account.Name,
            account.Temporary,
            account.Enabled,
            new HashSet<DwaineGroupId>(account.Groups));
    }

    private static DwaineIdentitySessionSnapshot Snapshot(DwaineIdentitySession session)
    {
        return new DwaineIdentitySessionSnapshot(
            session.Id,
            session.Principal,
            session.Terminal,
            session.CreatedAt,
            session.ExpiresAt,
            session.Temporary);
    }
}

/// <summary>
/// Centralized UNIX-like mode evaluation shared by shell, syscalls and service adapters.
/// </summary>
public static class DwaineFileAccessPolicy
{
    public static bool CanAccess(
        DwainePrincipalId principal,
        IReadOnlySet<DwaineGroupId> groups,
        in DwaineVfsMetadata metadata,
        DwaineIdentityPermission permission)
    {
        if (principal == DwainePrincipalId.System)
            return true;

        var mode = metadata.Mode;
        var requested = permission & (DwaineIdentityPermission.Read
                                      | DwaineIdentityPermission.Write
                                      | DwaineIdentityPermission.Execute);
        if (requested == DwaineIdentityPermission.None)
            return false;

        DwaineVfsMode read;
        DwaineVfsMode write;
        DwaineVfsMode execute;
        if (metadata.Owner == principal.Value)
        {
            read = DwaineVfsMode.OwnerRead;
            write = DwaineVfsMode.OwnerWrite;
            execute = DwaineVfsMode.OwnerExecute;
        }
        else if (groups.Contains(new DwaineGroupId(metadata.Group)))
        {
            read = DwaineVfsMode.GroupRead;
            write = DwaineVfsMode.GroupWrite;
            execute = DwaineVfsMode.GroupExecute;
        }
        else
        {
            read = DwaineVfsMode.OtherRead;
            write = DwaineVfsMode.OtherWrite;
            execute = DwaineVfsMode.OtherExecute;
        }

        return (!requested.HasFlag(DwaineIdentityPermission.Read) || mode.HasFlag(read))
               && (!requested.HasFlag(DwaineIdentityPermission.Write) || mode.HasFlag(write))
               && (!requested.HasFlag(DwaineIdentityPermission.Execute) || mode.HasFlag(execute));
    }

    public static bool CanChangeMode(DwainePrincipalId principal, in DwaineVfsMetadata metadata, bool isOperator)
    {
        return principal == DwainePrincipalId.System || isOperator || metadata.Owner == principal.Value;
    }

    public static bool CanChangeOwner(DwainePrincipalId principal, bool isOperator)
    {
        return principal == DwainePrincipalId.System || isOperator;
    }
}
