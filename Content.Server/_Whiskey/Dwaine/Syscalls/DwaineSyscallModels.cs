// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.Dwaine.Syscalls;

/// <summary>
/// Stable behavioral IDs corresponding to the audited DWAINE ABI. Message-only IDs are intentionally
/// present but rejected by the callable dispatcher.
/// </summary>
public enum DwaineSyscallId : byte
{
    MessageTerminal = 1,
    UserLogin = 2,
    UserGroup = 3,
    UserList = 4,
    UserMessage = 5,
    UserInput = 6,
    DeviceMessage = 7,
    DeviceList = 8,
    DeviceGet = 9,
    DeviceScan = 10,
    Exit = 11,
    TaskSpawn = 12,
    TaskFork = 13,
    TaskKill = 14,
    TaskList = 15,
    TaskExitMessage = 16,
    FileGet = 17,
    FileKill = 18,
    FileMode = 19,
    FileOwner = 20,
    FileWrite = 21,
    ConfigurationGet = 22,
    Mount = 23,
    ReceiveFileMessage = 24,
    BreakMessage = 25,
    ReplyMessage = 30,
}

public enum DwaineSyscallStatus : byte
{
    Success,
    UnknownCall,
    MainframeUnavailable,
    InvalidCaller,
    InvalidArguments,
    AccessDenied,
    NotFound,
    Conflict,
    StaleHandle,
    Offline,
    Unsupported,
    RateLimited,
    LimitExceeded,
    FileSystemFailure,
    ProcessFailure,
    StorageFailure,
}

public enum DwaineSyscallValueKind : byte
{
    Null,
    Integer,
    Boolean,
    String,
    DeviceHandle,
}

public readonly record struct DwaineSyscallValue
{
    public DwaineSyscallValueKind Kind { get; }
    public long Integer { get; }
    public bool Boolean { get; }
    public string Text { get; }
    public DwaineDeviceHandle DeviceHandle { get; }

    private DwaineSyscallValue(
        DwaineSyscallValueKind kind,
        long integer,
        bool boolean,
        string text,
        DwaineDeviceHandle deviceHandle)
    {
        Kind = kind;
        Integer = integer;
        Boolean = boolean;
        Text = text;
        DeviceHandle = deviceHandle;
    }

    public static DwaineSyscallValue Null => new(DwaineSyscallValueKind.Null, 0, false, string.Empty, default);
    public static DwaineSyscallValue FromInteger(long value) => new(DwaineSyscallValueKind.Integer, value, false, string.Empty, default);
    public static DwaineSyscallValue FromBoolean(bool value) => new(DwaineSyscallValueKind.Boolean, 0, value, string.Empty, default);
    public static DwaineSyscallValue FromString(string value) => new(DwaineSyscallValueKind.String, 0, false, value, default);
    public static DwaineSyscallValue FromDeviceHandle(DwaineDeviceHandle value) => new(DwaineSyscallValueKind.DeviceHandle, 0, false, string.Empty, value);
}

public enum DwaineSyscallEffect : byte
{
    None,
    ExitProcess,
}

public readonly record struct DwaineSyscallResult(
    DwaineSyscallStatus Status,
    DwaineSyscallValue Value,
    string Error,
    DwaineSyscallEffect Effect = DwaineSyscallEffect.None,
    int ExitCode = 0)
{
    public bool Succeeded => Status == DwaineSyscallStatus.Success;

    public static DwaineSyscallResult Success(DwaineSyscallValue value = default)
        => new(DwaineSyscallStatus.Success, value, string.Empty);

    public static DwaineSyscallResult Failure(DwaineSyscallStatus status, string error)
        => new(status, DwaineSyscallValue.Null, error);

    public static DwaineSyscallResult Exit(int code)
        => new(DwaineSyscallStatus.Success, DwaineSyscallValue.Null, string.Empty, DwaineSyscallEffect.ExitProcess, code);
}

internal interface IDwaineSyscallProgramBridge
{
    DwaineSyscallResult Spawn(string path, IReadOnlyList<string> arguments);
    DwaineSyscallResult Fork(IReadOnlyList<string> arguments);
}

internal readonly record struct DwaineSyscallLimits(
    int MaxArguments,
    int MaxArgumentCharacters,
    int MaxResultCharacters)
{
    public static DwaineSyscallLimits FromComponent(Content.Shared._Whiskey.Dwaine.Syscalls.DwaineSyscallComponent component)
        => new(
            Math.Clamp(component.MaxArguments, 1, Content.Shared._Whiskey.Dwaine.Syscalls.DwaineSyscallComponent.HardMaxArguments),
            Math.Clamp(component.MaxArgumentCharacters, 1, Content.Shared._Whiskey.Dwaine.Syscalls.DwaineSyscallComponent.HardMaxArgumentCharacters),
            Math.Clamp(component.MaxResultCharacters, 1, Content.Shared._Whiskey.Dwaine.Syscalls.DwaineSyscallComponent.HardMaxResultCharacters));
}
