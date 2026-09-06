// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Identity;
using Content.Shared._Whiskey.NanoXp;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.NanoXp;

public enum NanoXpAccountResult : byte
{
    Success,
    InvalidCredential,
    InvalidIdentity,
    AlreadyExists,
    AccountLimit,
    SessionLimit,
    UnknownRecipient,
    InvalidMail,
    MailboxFull,
}

public readonly record struct NanoXpAccountSnapshot(
    DwainePrincipalId Principal,
    string IdentityKey,
    string Address,
    string DisplayName,
    string JobTitle,
    string Department,
    IReadOnlySet<string> AccessTags);

public readonly record struct NanoXpSessionSnapshot(
    DwaineIdentitySessionId Session,
    DwainePrincipalId Principal,
    ulong Terminal);

public readonly record struct NanoXpStoredMail(
    ulong Id,
    string Sender,
    string Subject,
    string Body,
    long SentAtSeconds);

internal sealed class NanoXpAccountProfile
{
    public required DwainePrincipalId Principal;
    public required string IdentityKey;
    public required string Address;
    public required string DisplayName;
    public required string JobTitle;
    public required string Department;
    public readonly HashSet<string> AccessTags = new(StringComparer.Ordinal);
    public readonly List<NanoXpStoredMail> Inbox = [];
}

/// <summary>
/// Bounded station-local account, session, directory and G-Mail store.
/// Password hashing and constant-time verification are delegated to the DWAINE identity primitive.
/// </summary>
public sealed class NanoXpAccountStore
{
    public const string MailDomain = "gmail.nano";
    public const int HardMaxAccounts = NanoXpLimits.MaxDirectoryEntries;
    public const int HardMaxSessions = 512;

    private readonly DwaineIdentityStore _identities;
    private readonly Dictionary<DwainePrincipalId, NanoXpAccountProfile> _profiles = new();
    private readonly Dictionary<string, DwainePrincipalId> _profilesByIdentity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DwainePrincipalId> _profilesByAddress = new(StringComparer.OrdinalIgnoreCase);
    private ulong _nextMailId = 1;

    public int AccountCount => _profiles.Count;
    public int SessionCount => _identities.SessionCount;

    public NanoXpAccountStore(int accountCapacity = HardMaxAccounts, int sessionCapacity = HardMaxSessions)
    {
        if (accountCapacity is <= 0 or > HardMaxAccounts)
            throw new ArgumentOutOfRangeException(nameof(accountCapacity));
        if (sessionCapacity is <= 0 or > HardMaxSessions)
            throw new ArgumentOutOfRangeException(nameof(sessionCapacity));

        _identities = new DwaineIdentityStore(accountCapacity, 3, sessionCapacity);
    }

    public NanoXpAccountResult TryEnroll(
        string identityKey,
        string displayName,
        string jobTitle,
        string department,
        IEnumerable<string> accessTags,
        string password,
        out NanoXpAccountSnapshot account)
    {
        account = default;
        if (!IsValidIdentity(identityKey, displayName))
            return NanoXpAccountResult.InvalidIdentity;
        if (_profilesByIdentity.TryGetValue(identityKey, out var existing))
        {
            account = Snapshot(_profiles[existing]);
            return NanoXpAccountResult.AlreadyExists;
        }
        if (_profiles.Count >= HardMaxAccounts)
            return NanoXpAccountResult.AccountLimit;

        var localName = AllocateLocalName(displayName);
        var identityResult = _identities.TryCreateAccount(localName, password, false, out var identity);
        if (identityResult != DwaineIdentityResult.Success)
            return MapIdentityResult(identityResult);

        var profile = new NanoXpAccountProfile
        {
            Principal = identity.Principal,
            IdentityKey = identityKey,
            Address = AddressFor(localName),
            DisplayName = displayName.Trim(),
            JobTitle = NormalizeLabel(jobTitle),
            Department = NormalizeLabel(department),
        };
        profile.AccessTags.UnionWith(accessTags.Where(IsValidAccessTag));
        _profiles.Add(profile.Principal, profile);
        _profilesByIdentity.Add(profile.IdentityKey, profile.Principal);
        _profilesByAddress.Add(profile.Address, profile.Principal);
        account = Snapshot(profile);
        return NanoXpAccountResult.Success;
    }

    public NanoXpAccountResult TryLogin(
        string address,
        string password,
        ulong terminal,
        TimeSpan now,
        TimeSpan lifetime,
        out NanoXpSessionSnapshot session)
    {
        session = default;
        if (!TryNormalizeAddress(address, out var normalized)
            || !_profilesByAddress.TryGetValue(normalized, out var principal)
            || !_profiles.TryGetValue(principal, out var profile))
        {
            return NanoXpAccountResult.InvalidCredential;
        }

        var localName = profile.Address[..profile.Address.IndexOf('@')];
        var result = _identities.TryLogin(localName, password, terminal, now, lifetime, out var identitySession);
        if (result != DwaineIdentityResult.Success)
            return MapIdentityResult(result);

        session = new NanoXpSessionSnapshot(identitySession.Session, identitySession.Principal, identitySession.Terminal);
        return NanoXpAccountResult.Success;
    }

    public bool TryGetLiveSession(NanoXpSessionSnapshot session, TimeSpan now, out NanoXpAccountSnapshot account)
    {
        account = default;
        if (_identities.TryGetSession(session.Session, now, out var live) != DwaineIdentityResult.Success
            || live.Principal != session.Principal
            || live.Terminal != session.Terminal
            || !_profiles.TryGetValue(live.Principal, out var profile))
        {
            return false;
        }

        account = Snapshot(profile);
        return true;
    }

    public bool Disconnect(NanoXpSessionSnapshot session)
        => _identities.DisconnectTerminal(session.Terminal);

    public bool TryGetByIdentity(string identityKey, out NanoXpAccountSnapshot account)
    {
        account = default;
        if (!_profilesByIdentity.TryGetValue(identityKey, out var principal)
            || !_profiles.TryGetValue(principal, out var profile))
        {
            return false;
        }

        account = Snapshot(profile);
        return true;
    }

    public void RefreshProfile(
        NanoXpAccountSnapshot account,
        string displayName,
        string jobTitle,
        string department,
        IEnumerable<string> accessTags)
    {
        if (!_profiles.TryGetValue(account.Principal, out var profile))
            return;

        profile.DisplayName = string.IsNullOrWhiteSpace(displayName) ? profile.DisplayName : displayName.Trim();
        profile.JobTitle = NormalizeLabel(jobTitle);
        profile.Department = NormalizeLabel(department);
        profile.AccessTags.Clear();
        profile.AccessTags.UnionWith(accessTags.Where(IsValidAccessTag));
    }

    public NanoXpAccountResult TrySendMail(
        DwainePrincipalId sender,
        string recipient,
        string subject,
        string body,
        long sentAtSeconds)
    {
        if (!_profiles.TryGetValue(sender, out var senderProfile))
            return NanoXpAccountResult.InvalidCredential;
        if (!TryNormalizeAddress(recipient, out var normalized)
            || !_profilesByAddress.TryGetValue(normalized, out var recipientPrincipal)
            || !_profiles.TryGetValue(recipientPrincipal, out var recipientProfile))
        {
            return NanoXpAccountResult.UnknownRecipient;
        }
        if (!IsValidMail(subject, body))
            return NanoXpAccountResult.InvalidMail;
        if (recipientProfile.Inbox.Count >= NanoXpLimits.MaxMailboxMessages)
            return NanoXpAccountResult.MailboxFull;

        recipientProfile.Inbox.Add(new NanoXpStoredMail(
            AllocateMailId(),
            senderProfile.Address,
            subject.Trim(),
            body.Trim(),
            Math.Max(0, sentAtSeconds)));
        return NanoXpAccountResult.Success;
    }

    public NanoXpStoredMail[] GetInbox(DwainePrincipalId principal)
    {
        if (!_profiles.TryGetValue(principal, out var profile))
            return [];

        return profile.Inbox
            .OrderByDescending(mail => mail.Id)
            .ToArray();
    }

    public NanoXpAccountSnapshot[] GetDirectory()
        => _profiles.Values
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Address, StringComparer.OrdinalIgnoreCase)
            .Select(Snapshot)
            .ToArray();

    public string SuggestAddress(string displayName)
        => AddressFor(FindAvailableLocalName(displayName));

    private string AllocateLocalName(string displayName)
    {
        var localName = FindAvailableLocalName(displayName);
        return localName;
    }

    private string FindAvailableLocalName(string displayName)
    {
        var root = BuildLocalName(displayName);
        var candidate = root;
        for (var suffix = 2; suffix <= HardMaxAccounts + 1; suffix++)
        {
            if (!_profilesByAddress.ContainsKey(AddressFor(candidate)))
                return candidate;

            var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
            var rootLength = Math.Min(root.Length, DwaineIdentityStore.HardMaxNameLength - suffixText.Length);
            candidate = root[..rootLength] + suffixText;
        }

        return $"crew{_profiles.Count + 1}";
    }

    public static string BuildLocalName(string displayName)
    {
        var normalized = (displayName ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(DwaineIdentityStore.HardMaxNameLength);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(lower);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');

            if (builder.Length >= DwaineIdentityStore.HardMaxNameLength)
                break;
        }

        var localName = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(localName) ? "crew" : localName;
    }

    public static bool TryNormalizeAddress(string address, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(address) || address.Length > NanoXpLimits.MaxAddressLength)
            return false;

        var trimmed = address.Trim();
        var separator = trimmed.IndexOf('@');
        var localName = separator < 0 ? trimmed : trimmed[..separator];
        if (separator >= 0 && !string.Equals(trimmed[(separator + 1)..], MailDomain, StringComparison.OrdinalIgnoreCase))
            return false;
        if (localName.Length is 0 or > DwaineIdentityStore.HardMaxNameLength
            || localName.Any(character => !(character is >= 'a' and <= 'z'
                                             or >= 'A' and <= 'Z'
                                             or >= '0' and <= '9'
                                             or '.' or '_' or '-')))
        {
            return false;
        }

        normalized = AddressFor(localName.ToLowerInvariant());
        return true;
    }

    private static string AddressFor(string localName)
        => $"{localName}@{MailDomain}";

    private static bool IsValidIdentity(string identityKey, string displayName)
        => !string.IsNullOrWhiteSpace(identityKey)
           && identityKey.Length <= 64
           && !string.IsNullOrWhiteSpace(displayName)
           && displayName.Length <= 64;

    private static bool IsValidAccessTag(string tag)
        => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 64;

    private static bool IsValidMail(string subject, string body)
        => !string.IsNullOrWhiteSpace(subject)
           && subject.Length <= NanoXpLimits.MaxSubjectLength
           && !string.IsNullOrWhiteSpace(body)
           && body.Length <= NanoXpLimits.MaxBodyLength
           && subject.IndexOf('\0') < 0
           && body.IndexOf('\0') < 0;

    private static string NormalizeLabel(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim()[..Math.Min(value.Trim().Length, 96)];

    private ulong AllocateMailId()
    {
        var id = _nextMailId++;
        if (_nextMailId == 0)
            _nextMailId = 1;
        return id;
    }

    private static NanoXpAccountSnapshot Snapshot(NanoXpAccountProfile profile)
        => new(
            profile.Principal,
            profile.IdentityKey,
            profile.Address,
            profile.DisplayName,
            profile.JobTitle,
            profile.Department,
            new HashSet<string>(profile.AccessTags, StringComparer.Ordinal));

    private static NanoXpAccountResult MapIdentityResult(DwaineIdentityResult result)
        => result switch
        {
            DwaineIdentityResult.Success => NanoXpAccountResult.Success,
            DwaineIdentityResult.AccountLimit => NanoXpAccountResult.AccountLimit,
            DwaineIdentityResult.SessionLimit => NanoXpAccountResult.SessionLimit,
            _ => NanoXpAccountResult.InvalidCredential,
        };
}
