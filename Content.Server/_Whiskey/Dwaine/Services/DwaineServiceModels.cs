// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Identity;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Services;

public enum DwaineServiceStatus : byte
{
    Success,
    Unavailable,
    InvalidArguments,
    AccessDenied,
    NotFound,
    Conflict,
    CapacityReached,
    FileSystemFailure,
}

public readonly record struct DwaineServiceResponse(DwaineServiceStatus Status, string Output)
{
    public bool Succeeded => Status == DwaineServiceStatus.Success;

    public static DwaineServiceResponse Success(string output = "")
        => new(DwaineServiceStatus.Success, output);

    public static DwaineServiceResponse Failure(DwaineServiceStatus status, string output)
        => new(status, output);
}

public readonly record struct DwaineMailMessage(
    ulong Id,
    DwainePrincipalId Recipient,
    string Sender,
    string Subject,
    string Body,
    TimeSpan ReceivedAt);

public readonly record struct DwaineServiceLogEntry(
    ulong Sequence,
    TimeSpan Time,
    string Actor,
    string Service,
    string Operation,
    DwaineServiceStatus Status,
    string Detail);

public readonly record struct DwaineServiceMetrics(
    int MailMessages,
    int Mailboxes,
    int LogEntries,
    ulong Calls,
    ulong Failures);

public readonly record struct DwaineServiceLimits(
    int MaxMailMessages,
    int MaxMailPerUser,
    int MaxMailSubjectCharacters,
    int MaxMailBodyCharacters,
    int MaxLogEntries,
    int MaxServiceOutputCharacters);

/// <summary>
/// Persistent, bounded service data owned by one mainframe. The store contains no EntityUid,
/// network session, password or host object and survives kernel reboot while runtime leases do not.
/// </summary>
public sealed class DwaineServiceStore
{
    private readonly Dictionary<DwainePrincipalId, List<DwaineMailMessage>> _mail = [];
    private readonly Queue<DwaineServiceLogEntry> _logs = [];
    private readonly DwaineServiceLimits _limits;
    private ulong _nextMailId = 1;
    private ulong _nextLogSequence = 1;
    private int _mailCount;
    private ulong _calls;
    private ulong _failures;

    public DwaineServiceStore(DwaineServiceLimits limits)
    {
        if (limits.MaxMailMessages <= 0
            || limits.MaxMailPerUser <= 0
            || limits.MaxMailPerUser > limits.MaxMailMessages
            || limits.MaxMailSubjectCharacters <= 0
            || limits.MaxMailBodyCharacters <= 0
            || limits.MaxLogEntries <= 0
            || limits.MaxServiceOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }
        _limits = limits;
    }

    public DwaineServiceStatus TrySendMail(
        string sender,
        IReadOnlyCollection<DwainePrincipalId> recipients,
        string subject,
        string body,
        TimeSpan now)
    {
        var distinct = recipients.Where(recipient => recipient.IsValid).Distinct().ToArray();
        if (distinct.Length == 0
            || !ValidText(sender, 32, false)
            || !ValidText(subject, _limits.MaxMailSubjectCharacters, false)
            || !ValidText(body, _limits.MaxMailBodyCharacters, true))
        {
            return DwaineServiceStatus.InvalidArguments;
        }
        if (distinct.Length > _limits.MaxMailMessages - _mailCount)
            return DwaineServiceStatus.CapacityReached;
        foreach (var recipient in distinct)
        {
            if (_mail.GetValueOrDefault(recipient)?.Count >= _limits.MaxMailPerUser)
                return DwaineServiceStatus.CapacityReached;
        }

        foreach (var recipient in distinct)
        {
            if (!_mail.TryGetValue(recipient, out var mailbox))
            {
                mailbox = [];
                _mail.Add(recipient, mailbox);
            }
            mailbox.Add(new DwaineMailMessage(AllocateMailId(), recipient, sender, subject, body, now));
            _mailCount++;
        }
        return DwaineServiceStatus.Success;
    }

    public DwaineMailMessage[] ListMail(DwainePrincipalId recipient)
        => _mail.TryGetValue(recipient, out var mailbox)
            ? mailbox.OrderBy(message => message.Id).ToArray()
            : [];

    public DwaineServiceStatus TryReadMail(DwainePrincipalId recipient, ulong id, out DwaineMailMessage message)
    {
        message = default;
        if (!_mail.TryGetValue(recipient, out var mailbox))
            return DwaineServiceStatus.NotFound;
        foreach (var candidate in mailbox)
        {
            if (candidate.Id != id)
                continue;
            message = candidate;
            return DwaineServiceStatus.Success;
        }
        return DwaineServiceStatus.NotFound;
    }

    public DwaineServiceStatus TryDeleteMail(DwainePrincipalId recipient, ulong id)
    {
        if (!_mail.TryGetValue(recipient, out var mailbox))
            return DwaineServiceStatus.NotFound;
        var index = mailbox.FindIndex(message => message.Id == id);
        if (index < 0)
            return DwaineServiceStatus.NotFound;
        mailbox.RemoveAt(index);
        _mailCount--;
        if (mailbox.Count == 0)
            _mail.Remove(recipient);
        return DwaineServiceStatus.Success;
    }

    public void Record(
        TimeSpan now,
        string actor,
        string service,
        string operation,
        DwaineServiceStatus status,
        string detail = "")
    {
        if (_calls < ulong.MaxValue)
            _calls++;
        if (status != DwaineServiceStatus.Success && _failures < ulong.MaxValue)
            _failures++;
        _logs.Enqueue(new DwaineServiceLogEntry(
            AllocateLogSequence(),
            now,
            SafeLabel(actor),
            SafeLabel(service),
            SafeLabel(operation),
            status,
            SafeDetail(detail)));
        while (_logs.Count > _limits.MaxLogEntries)
            _logs.Dequeue();
    }

    public DwaineServiceLogEntry[] GetLogs(int count)
    {
        var bounded = Math.Clamp(count, 1, _limits.MaxLogEntries);
        return _logs.TakeLast(bounded).ToArray();
    }

    public DwaineServiceMetrics GetMetrics()
        => new(_mailCount, _mail.Count, _logs.Count, _calls, _failures);

    private ulong AllocateMailId()
    {
        var id = _nextMailId++;
        if (_nextMailId == 0)
            _nextMailId = 1;
        return id;
    }

    private ulong AllocateLogSequence()
    {
        var sequence = _nextLogSequence++;
        if (_nextLogSequence == 0)
            _nextLogSequence = 1;
        return sequence;
    }

    private static string SafeLabel(string value)
        => ValidText(value, 64, false) ? value : "invalid";

    private static string SafeDetail(string value)
        => value.Length == 0 ? string.Empty : ValidText(value, 1024, false) ? value : "invalid";

    private static bool ValidText(string? value, int maximum, bool multiline)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.IndexOf('\0') >= 0)
            return false;
        foreach (var character in value)
        {
            if (char.IsControl(character) && character != '\t' && (!multiline || character != '\n'))
                return false;
        }
        return true;
    }
}
