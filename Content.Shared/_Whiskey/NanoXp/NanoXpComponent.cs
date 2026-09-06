// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Whiskey.NanoXp;

public static class NanoXpLimits
{
    public const int MaxAddressLength = 64;
    public const int MaxPasswordLength = 256;
    public const int MaxSubjectLength = 80;
    public const int MaxBodyLength = 1000;
    public const int MaxMailboxMessages = 64;
    public const int MaxDirectoryEntries = 256;
}

[Serializable, NetSerializable]
public enum NanoXpUiKey : byte
{
    Key,
}

public enum NanoXpDeviceKind : byte
{
    Computer,
    Pda,
}

public enum NanoXpNotice : byte
{
    None,
    Enrolled,
    LoggedIn,
    LoggedOut,
    InvalidCredential,
    IdentityRequired,
    AccessDenied,
    InvalidMail,
    UnknownRecipient,
    MailboxFull,
    MailSent,
    RateLimited,
    Offline,
}

[RegisterComponent, NetworkedComponent]
public sealed partial class NanoXpDeviceComponent : Component
{
    [DataField]
    public NanoXpDeviceKind Kind = NanoXpDeviceKind.Computer;
}

[Serializable, NetSerializable]
public sealed class NanoXpMailEntry
{
    public readonly ulong Id;
    public readonly string Sender;
    public readonly string Subject;
    public readonly string Body;
    public readonly long SentAtSeconds;

    public NanoXpMailEntry(ulong id, string sender, string subject, string body, long sentAtSeconds)
    {
        Id = id;
        Sender = sender;
        Subject = subject;
        Body = body;
        SentAtSeconds = sentAtSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class NanoXpDirectoryEntry
{
    public readonly string Address;
    public readonly string DisplayName;
    public readonly string Department;

    public NanoXpDirectoryEntry(string address, string displayName, string department)
    {
        Address = address;
        DisplayName = displayName;
        Department = department;
    }
}

[Serializable, NetSerializable]
public sealed class NanoXpUserInterfaceState
{
    public readonly NanoXpDeviceKind DeviceKind;
    public readonly string DeviceName;
    public readonly string NetworkName;
    public readonly bool Online;
    public readonly bool CanEnroll;
    public readonly string SuggestedAddress;
    public readonly bool Authenticated;
    public readonly string Address;
    public readonly string DisplayName;
    public readonly string JobTitle;
    public readonly string Department;
    public readonly bool DepartmentAuthorized;
    public readonly bool DwaineAvailable;
    public readonly NanoXpNotice Notice;
    public readonly NanoXpMailEntry[] Inbox;
    public readonly NanoXpDirectoryEntry[] Directory;

    public NanoXpUserInterfaceState(
        NanoXpDeviceKind deviceKind,
        string deviceName,
        string networkName,
        bool online,
        bool canEnroll,
        string suggestedAddress,
        bool authenticated,
        string address,
        string displayName,
        string jobTitle,
        string department,
        bool departmentAuthorized,
        bool dwaineAvailable,
        NanoXpNotice notice,
        NanoXpMailEntry[] inbox,
        NanoXpDirectoryEntry[] directory)
    {
        DeviceKind = deviceKind;
        DeviceName = deviceName;
        NetworkName = networkName;
        Online = online;
        CanEnroll = canEnroll;
        SuggestedAddress = suggestedAddress;
        Authenticated = authenticated;
        Address = address;
        DisplayName = displayName;
        JobTitle = jobTitle;
        Department = department;
        DepartmentAuthorized = departmentAuthorized;
        DwaineAvailable = dwaineAvailable;
        Notice = notice;
        Inbox = inbox;
        Directory = directory;
    }
}

[Serializable, NetSerializable]
public sealed class NanoXpStateMessage(NanoXpUserInterfaceState state) : BoundUserInterfaceMessage
{
    public readonly NanoXpUserInterfaceState State = state;
}

[Serializable, NetSerializable]
public sealed class NanoXpRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class NanoXpEnrollMessage(string password) : BoundUserInterfaceMessage
{
    public readonly string Password = password;
}

[Serializable, NetSerializable]
public sealed class NanoXpLoginMessage(string address, string password) : BoundUserInterfaceMessage
{
    public readonly string Address = address;
    public readonly string Password = password;
}

[Serializable, NetSerializable]
public sealed class NanoXpLogoutMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class NanoXpSendMailMessage(string recipient, string subject, string body) : BoundUserInterfaceMessage
{
    public readonly string Recipient = recipient;
    public readonly string Subject = subject;
    public readonly string Body = body;
}

[Serializable, NetSerializable]
public sealed class NanoXpLaunchDwaineMessage : BoundUserInterfaceMessage;
