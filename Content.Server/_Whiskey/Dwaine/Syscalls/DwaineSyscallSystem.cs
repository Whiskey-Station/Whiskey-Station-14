// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Shell;
using Content.Server._Whiskey.Dwaine.Storage;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Syscalls;
using Robust.Shared.Timing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.Dwaine.Syscalls;

/// <summary>
/// Explicit, non-reflective syscall dispatcher. Caller PID, principal, session, cwd and device entities
/// are derived from server-owned process state for every call.
/// </summary>
public sealed partial class DwaineSyscallSystem : EntitySystem
{
    private static readonly TimeSpan AuthenticationCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReplyLifetime = TimeSpan.FromSeconds(30);
    private const int MaximumSpawnArguments = 16;

    [Dependency] private DwaineDeviceSystem _devices = default!;
    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineProcessSystem _processes = default!;
    [Dependency] private DwaineStorageSystem _storage = default!;
    [Dependency] private DwaineTerminalTransportSystem _transport = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineSyscallComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineSyscallRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineSyscallRuntimeComponent, DwaineProcessStateChangedEvent>(OnProcessStateChanged);
        SubscribeLocalEvent<DwaineSyscallRuntimeComponent, DwaineProcessRemovedEvent>(OnProcessRemoved);
    }

    internal DwaineSyscallResult Execute(
        EntityUid mainframe,
        DwaineProcessId processId,
        DwaineSyscallId id,
        IReadOnlyList<DwaineSyscallValue> arguments,
        IDwaineSyscallProgramBridge? bridge = null)
    {
        if (!TryBuildContext(mainframe, processId, arguments, out var context, out var failure))
            return failure;

        return id switch
        {
            DwaineSyscallId.MessageTerminal => MessageTerminal(context, arguments),
            DwaineSyscallId.UserLogin => UserLogin(context, arguments),
            DwaineSyscallId.UserGroup => UserGroup(context, arguments),
            DwaineSyscallId.UserList => UserList(context, arguments),
            DwaineSyscallId.UserMessage => UserMessage(context, arguments),
            DwaineSyscallId.UserInput => Fail(DwaineSyscallStatus.AccessDenied, "user input is restricted to trusted drivers"),
            DwaineSyscallId.DeviceMessage => DeviceMessage(context, arguments),
            DwaineSyscallId.DeviceList => DeviceList(context, arguments),
            DwaineSyscallId.DeviceGet => DeviceGet(context, arguments),
            DwaineSyscallId.DeviceScan => DeviceScan(context, arguments),
            DwaineSyscallId.Exit => Exit(arguments),
            DwaineSyscallId.TaskSpawn => TaskSpawn(context, arguments, bridge),
            DwaineSyscallId.TaskFork => TaskFork(arguments, bridge),
            DwaineSyscallId.TaskKill => TaskKill(context, arguments),
            DwaineSyscallId.TaskList => TaskList(context, arguments),
            DwaineSyscallId.FileGet => FileGet(context, arguments),
            DwaineSyscallId.FileKill => FileKill(context, arguments),
            DwaineSyscallId.FileMode => FileMode(context, arguments),
            DwaineSyscallId.FileOwner => FileOwner(context, arguments),
            DwaineSyscallId.FileWrite => FileWrite(context, arguments),
            DwaineSyscallId.ConfigurationGet => ConfigurationGet(context, arguments),
            DwaineSyscallId.Mount => Mount(context, arguments),
            DwaineSyscallId.TaskExitMessage
                or DwaineSyscallId.ReceiveFileMessage
                or DwaineSyscallId.BreakMessage
                or DwaineSyscallId.ReplyMessage => Fail(DwaineSyscallStatus.UnknownCall, "message ID is not callable"),
            _ => Fail(DwaineSyscallStatus.UnknownCall, "unknown syscall"),
        };
    }

    public DwaineSyscallStatus TryDeliverTrustedInput(EntityUid mainframe, EntityUid terminal, string text)
        => MapDevice(_devices.TryInjectTerminalInput(mainframe, terminal, text));

    public DwaineSyscallStatus TryBreak(
        EntityUid mainframe,
        DwaineProcessId requester,
        DwaineProcessId target)
    {
        if (!_processes.TryGetProcess(mainframe, requester, out var caller)
            || !_processes.TryGetProcess(mainframe, target, out var child)
            || child.ParentId != caller.ProcessId)
        {
            return DwaineSyscallStatus.AccessDenied;
        }
        _processes.TrySendKernelMessage(mainframe, target, DwaineKernelMessageType.Break, requester.Value.ToString(CultureInfo.InvariantCulture));
        return MapProcess(_processes.TryKill(mainframe, requester, target));
    }

    public DwaineSyscallStatus TrySendFileNotification(
        EntityUid mainframe,
        DwaineProcessId sender,
        DwaineProcessId target,
        string path)
    {
        if (!TryBuildContext(mainframe, sender, [DwaineSyscallValue.FromString(path)], out var context, out var failure))
            return failure.Status;
        if (!_processes.TryGetProcess(mainframe, target, out var recipient)
            || recipient.Owner != context.Process.Owner
            || recipient.ProcessId == sender)
        {
            return DwaineSyscallStatus.AccessDenied;
        }
        var read = context.Files.TryReadText(context.Principal, path, context.WorkingDirectory, out var content);
        if (read != DwaineVfsResult.Success)
            return MapVfs(read);
        if (path.Length + content.Length + 2 > DwaineProcessMailbox.HardMaxPayloadLength)
            return DwaineSyscallStatus.LimitExceeded;
        var copiedPayload = path + "\n" + content;
        return MapMessage(_processes.TrySendKernelMessage(
            mainframe,
            target,
            DwaineKernelMessageType.ReceiveFile,
            copiedPayload));
    }

    public DwaineSyscallStatus TryOpenReply(
        EntityUid mainframe,
        DwaineProcessId requester,
        DwaineProcessId responder,
        out DwaineRequestCorrelationId correlation)
    {
        correlation = default;
        if (!TryGetRuntime(mainframe, out _, out var runtime)
            || !_processes.TryGetProcess(mainframe, requester, out var requestProcess)
            || !_processes.TryGetProcess(mainframe, responder, out var responseProcess)
            || requestProcess.Owner != responseProcess.Owner
            || runtime.PendingReplies.Count >= 256)
        {
            return DwaineSyscallStatus.AccessDenied;
        }
        PruneReplies(runtime);
        for (var attempt = 0; attempt <= runtime.PendingReplies.Count; attempt++)
        {
            var value = runtime.NextCorrelation++;
            if (runtime.NextCorrelation == 0)
                runtime.NextCorrelation = 1;
            var candidate = new DwaineRequestCorrelationId(value);
            if (!candidate.IsValid || runtime.PendingReplies.ContainsKey(candidate))
                continue;
            correlation = candidate;
            runtime.PendingReplies.Add(candidate, new DwainePendingReply(requester, responder, _timing.CurTime + ReplyLifetime));
            return DwaineSyscallStatus.Success;
        }
        return DwaineSyscallStatus.LimitExceeded;
    }

    public DwaineSyscallStatus TryReply(
        EntityUid mainframe,
        DwaineProcessId responder,
        DwaineRequestCorrelationId correlation,
        string payload)
    {
        if (!TryGetRuntime(mainframe, out _, out var runtime))
            return DwaineSyscallStatus.MainframeUnavailable;
        PruneReplies(runtime);
        if (!runtime.PendingReplies.TryGetValue(correlation, out var pending)
            || pending.Responder != responder)
        {
            return DwaineSyscallStatus.AccessDenied;
        }
        runtime.PendingReplies.Remove(correlation);
        return MapMessage(_processes.TrySendKernelMessage(
            mainframe,
            pending.Requester,
            DwaineKernelMessageType.Reply,
            payload,
            correlation));
    }

    private DwaineSyscallResult MessageTerminal(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (!OneString(args, out var text) || context.Process.TerminalSession is not { } terminal)
            return Invalid("MSG_TERM expects one text string and a bound terminal");
        if (!TryValidateTerminalIdentity(context, terminal, out var session))
            return Fail(DwaineSyscallStatus.AccessDenied, "terminal session identity changed");
        return _transport.WriteOutput(context.Mainframe, session, text)
            ? DwaineSyscallResult.Success()
            : Fail(DwaineSyscallStatus.Offline, "terminal unavailable");
    }

    private DwaineSyscallResult UserLogin(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 2 || !TryString(args[0], out var name) || !TryString(args[1], out var password)
            || context.Process.TerminalSession is not { } terminal)
        {
            return Invalid("ULOGIN expects username and password strings");
        }
        if (!TryValidateTerminalIdentity(context, terminal, out var session))
            return Fail(DwaineSyscallStatus.AccessDenied, "terminal session identity changed");
        if (context.Runtime.NextAuthenticationAt.TryGetValue(context.Process.ProcessId, out var next)
            && _timing.CurTime < next)
        {
            return Fail(DwaineSyscallStatus.RateLimited, "authentication is rate limited");
        }
        context.Runtime.NextAuthenticationAt[context.Process.ProcessId] = _timing.CurTime + AuthenticationCooldown;
        var result = _identities.TryLogin(context.Mainframe, session, name, password, out _);
        return result == DwaineIdentityResult.Success
            ? DwaineSyscallResult.Success(DwaineSyscallValue.FromBoolean(true))
            : Fail(result == DwaineIdentityResult.Throttled ? DwaineSyscallStatus.RateLimited : DwaineSyscallStatus.AccessDenied,
                "invalid credentials");
    }

    private static DwaineSyscallResult UserGroup(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 3
            || !TryString(args[0], out var accountName)
            || !TryString(args[1], out var groupName)
            || !TryBoolean(args[2], out var member))
        {
            return Invalid("UGROUP expects account, group and membership");
        }
        if (!context.Identities.TryGetAccount(accountName, out var account)
            || !context.Identities.TryGetGroup(groupName, out var group))
        {
            return Fail(DwaineSyscallStatus.NotFound, "account or group not found");
        }
        var result = context.Identities.TrySetGroupMembership(context.Principal, account.Principal, group, member);
        return result == DwaineIdentityResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapIdentity(result), "group update denied");
    }

    private DwaineSyscallResult UserList(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 0)
            return Invalid("ULIST takes no arguments");
        var inspectAll = context.Identities.HasPermission(context.Principal, DwaineIdentityPermission.InspectSessions);
        var lines = new List<string>();
        foreach (var session in context.Identities.GetSessions(_timing.CurTime))
        {
            if (!inspectAll && session.Principal != context.Principal)
                continue;
            if (!context.Identities.TryGetAccount(session.Principal, out var account))
                continue;
            lines.Add(inspectAll
                ? $"{account.Name}\t{(account.Temporary ? "temporary" : "authenticated")}"
                : account.Name);
        }
        return BoundedString(context, string.Join('\n', lines));
    }

    private DwaineSyscallResult UserMessage(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 2 || !TryString(args[0], out var targetName) || !TryString(args[1], out var message)
            || string.IsNullOrWhiteSpace(message))
        {
            return Invalid("UMSG expects target user and message");
        }
        if (!context.Identities.TryGetAccount(context.Principal, out var sender)
            || !context.Identities.TryGetAccount(targetName, out var target)
            || target.Principal == context.Principal)
        {
            return Fail(DwaineSyscallStatus.NotFound, "message target unavailable");
        }
        foreach (var session in context.Identities.GetSessions(_timing.CurTime))
        {
            if (session.Principal != target.Principal)
                continue;
            var transportSession = new DwaineSessionId(session.Terminal);
            if (TryComp<DwaineShellRuntimeComponent>(context.Mainframe, out var shells)
                && shells.Sessions.TryGetValue(transportSession, out var shell)
                && !shell.MessagesEnabled)
            {
                continue;
            }
            if (_transport.WriteOutput(context.Mainframe, transportSession, $"message from {sender.Name}: {message}"))
                return DwaineSyscallResult.Success();
        }
        return Fail(DwaineSyscallStatus.Offline, "message target unavailable");
    }

    private DwaineSyscallResult DeviceMessage(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 3
            || args[0].Kind != DwaineSyscallValueKind.DeviceHandle
            || !TryString(args[1], out var command)
            || !TryString(args[2], out var payload))
        {
            return Invalid("DMSG expects device handle, command and payload");
        }
        var response = _devices.TryMessage(
            context.Mainframe,
            context.Process.ProcessId,
            context.Principal,
            args[0].DeviceHandle,
            command,
            payload);
        return response.Succeeded
            ? BoundedString(context, response.Payload)
            : Fail(MapDevice(response.Result), $"device message failed: {response.Result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult DeviceList(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        string? tag = null;
        if (args.Count == 1 && !TryString(args[0], out tag))
            return Invalid("DLIST tag must be a string");
        if (args.Count > 1)
            return Invalid("DLIST accepts at most one tag");
        var lines = _devices.ListDevices(context.Mainframe, context.Process.ProcessId, context.Principal, tag)
            .Select(device => $"{device.Address}\t{device.Tag}\t{device.DriverId}\t{device.Status.ToString().ToLowerInvariant()}");
        return BoundedString(context, string.Join('\n', lines));
    }

    private DwaineSyscallResult DeviceGet(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 2 || !TryString(args[0], out var address) || !TryInteger(args[1], out var requestedValue)
            || requestedValue is <= 0 or > ushort.MaxValue)
        {
            return Invalid("DGET expects address and capability mask");
        }
        var requested = (DwaineDeviceCapability) requestedValue;
        var result = _devices.TryAcquire(
            context.Mainframe,
            context.Process.ProcessId,
            context.Principal,
            address,
            requested,
            out var handle);
        return result == DwaineDeviceResult.Success
            ? DwaineSyscallResult.Success(DwaineSyscallValue.FromDeviceHandle(handle))
            : Fail(MapDevice(result), $"device acquisition failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult DeviceScan(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 0)
            return Invalid("DSCAN takes no arguments");
        var result = _devices.TryScan(
            context.Mainframe,
            context.Process.ProcessId,
            context.Principal,
            out var count);
        return result == DwaineDeviceResult.Success
            ? DwaineSyscallResult.Success(DwaineSyscallValue.FromInteger(count))
            : Fail(MapDevice(result), $"device scan failed: {result.ToString().ToLowerInvariant()}");
    }

    private static DwaineSyscallResult Exit(IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count == 0)
            return DwaineSyscallResult.Exit(0);
        if (args.Count != 1 || !TryInteger(args[0], out var code) || code is < int.MinValue or > int.MaxValue)
            return Invalid("EXIT code must be a signed 32-bit integer");
        return DwaineSyscallResult.Exit((int) code);
    }

    private static DwaineSyscallResult TaskSpawn(
        in DwaineSyscallContext context,
        IReadOnlyList<DwaineSyscallValue> args,
        IDwaineSyscallProgramBridge? bridge)
    {
        if (bridge is null || args.Count < 1 || args.Count > MaximumSpawnArguments + 1 || !TryString(args[0], out var path))
            return Invalid("TSPAWN expects an executable path and optional string arguments");
        var arguments = new string[args.Count - 1];
        for (var index = 1; index < args.Count; index++)
        {
            if (!TryString(args[index], out arguments[index - 1]))
                return Invalid("TSPAWN arguments must be strings");
        }
        if (context.Files.CheckExecute(context.Principal, path, context.WorkingDirectory) != DwaineVfsResult.Success)
            return Fail(DwaineSyscallStatus.AccessDenied, "executable permission denied");
        return bridge.Spawn(path, arguments);
    }

    private static DwaineSyscallResult TaskFork(
        IReadOnlyList<DwaineSyscallValue> args,
        IDwaineSyscallProgramBridge? bridge)
    {
        if (bridge is null || args.Count > MaximumSpawnArguments)
            return Invalid("TFORK received invalid arguments");
        var arguments = new string[args.Count];
        for (var index = 0; index < args.Count; index++)
        {
            if (!TryString(args[index], out arguments[index]))
                return Invalid("TFORK arguments must be strings");
        }
        return bridge.Fork(arguments);
    }

    private DwaineSyscallResult TaskKill(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 1 || !TryInteger(args[0], out var target) || target <= 0)
            return Invalid("TKILL expects a positive PID");
        var result = _processes.TryKill(context.Mainframe, context.Process.ProcessId, new DwaineProcessId((ulong) target));
        return result == DwaineProcessControlResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapProcess(result), $"process kill failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult TaskList(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 0)
            return Invalid("TLIST takes no arguments");
        var caller = context.Process.ProcessId;
        var lines = _processes.GetProcessTable(context.Mainframe)
            .Where(process => process.ParentId == caller)
            .OrderBy(process => process.ProcessId.Value)
            .Select(process => $"{process.ProcessId.Value}\t{process.State}\t{process.Program.Id}");
        return BoundedString(context, string.Join('\n', lines));
    }

    private static DwaineSyscallResult FileGet(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (!OneString(args, out var path))
            return Invalid("FGET expects one path string");
        var result = context.Files.TryReadText(context.Principal, path, context.WorkingDirectory, out var text);
        return result == DwaineVfsResult.Success
            ? BoundedString(context, text)
            : Fail(MapVfs(result), $"file read failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult FileKill(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count is < 1 or > 2 || !TryString(args[0], out var path)
            || args.Count == 2 && !TryBoolean(args[1], out _))
        {
            return Invalid("FKILL expects path and optional recursive flag");
        }
        var recursive = args.Count == 2 && args[1].Boolean;
        var result = context.Files.TryDelete(context.Principal, path, context.WorkingDirectory, recursive, _timing.CurTime);
        return result == DwaineVfsResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapVfs(result), $"file delete failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult FileMode(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 2 || !TryString(args[0], out var path) || !TryInteger(args[1], out var rawMode)
            || rawMode is < 0 or > (long) (DwaineVfsMode.OwnerAll | DwaineVfsMode.GroupAll | DwaineVfsMode.OtherAll))
        {
            return Invalid("FMODE expects path and valid mode bits");
        }
        var result = context.Files.TryChangeMode(
            context.Principal,
            path,
            context.WorkingDirectory,
            (DwaineVfsMode) rawMode,
            _timing.CurTime);
        return result == DwaineVfsResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapVfs(result), $"mode update failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult FileOwner(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count is < 2 or > 3 || !TryString(args[0], out var path) || !TryString(args[1], out var ownerName))
            return Invalid("FOWNER expects path, owner and optional group");
        if (!context.Identities.TryGetAccount(ownerName, out var owner))
            return Fail(DwaineSyscallStatus.NotFound, "owner not found");
        DwaineGroupId? group = null;
        if (args.Count == 3)
        {
            if (!TryString(args[2], out var groupName) || !context.Identities.TryGetGroup(groupName, out var selected))
                return Fail(DwaineSyscallStatus.NotFound, "group not found");
            group = selected;
        }
        var result = context.Files.TryChangeOwner(
            context.Principal,
            path,
            context.WorkingDirectory,
            owner.Principal,
            group,
            _timing.CurTime);
        return result == DwaineVfsResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapVfs(result), $"owner update failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult FileWrite(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 4
            || !TryString(args[0], out var path)
            || !TryString(args[1], out var text)
            || !TryBoolean(args[2], out var append)
            || !TryBoolean(args[3], out var replace))
        {
            return Invalid("FWRITE expects path, text, append and replace");
        }
        var stat = context.Files.TryStat(context.Principal, path, context.WorkingDirectory, out _);
        DwaineVfsResult result;
        if (stat == DwaineVfsResult.Success)
        {
            if (!append && !replace)
                return Fail(DwaineSyscallStatus.Conflict, "destination already exists");
            result = context.Files.TryWriteText(
                context.Principal,
                path,
                context.WorkingDirectory,
                text,
                append,
                _timing.CurTime);
        }
        else if (stat is DwaineVfsResult.NotFound or DwaineVfsResult.BrokenLink)
        {
            result = context.Files.TryCreateText(
                context.Principal,
                path,
                context.WorkingDirectory,
                text,
                null,
                _timing.CurTime);
        }
        else
        {
            result = stat;
        }
        return result == DwaineVfsResult.Success
            ? DwaineSyscallResult.Success()
            : Fail(MapVfs(result), $"file write failed: {result.ToString().ToLowerInvariant()}");
    }

    private static DwaineSyscallResult ConfigurationGet(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (!OneString(args, out var name)
            || name.Length == 0
            || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            return Invalid("CONFGET expects one configuration file name");
        }
        var result = context.Files.TryReadText(context.Principal, $"/conf/{name}", context.WorkingDirectory, out var text);
        return result == DwaineVfsResult.Success
            ? BoundedString(context, text)
            : Fail(MapVfs(result), $"configuration read failed: {result.ToString().ToLowerInvariant()}");
    }

    private DwaineSyscallResult Mount(in DwaineSyscallContext context, IReadOnlyList<DwaineSyscallValue> args)
    {
        if (args.Count != 2 || args[0].Kind != DwaineSyscallValueKind.DeviceHandle || !TryString(args[1], out var path))
            return Invalid("MOUNT expects storage handle and mount path");
        if (!context.Identities.HasPermission(context.Principal, DwaineIdentityPermission.Write))
            return Fail(DwaineSyscallStatus.AccessDenied, "mount permission denied");
        var resolve = _devices.TryResolveEntity(
            context.Mainframe,
            context.Process.ProcessId,
            context.Principal,
            args[0].DeviceHandle,
            DwaineDeviceCapability.Mount,
            out var media);
        if (resolve != DwaineDeviceResult.Success)
            return Fail(MapDevice(resolve), $"mount device unavailable: {resolve.ToString().ToLowerInvariant()}");
        var result = _storage.TryMount(context.Mainframe, media, path);
        return result.Succeeded
            ? DwaineSyscallResult.Success()
            : Fail(DwaineSyscallStatus.StorageFailure, $"mount failed: {result.Result.ToString().ToLowerInvariant()}");
    }

    private bool TryBuildContext(
        EntityUid mainframe,
        DwaineProcessId processId,
        IReadOnlyList<DwaineSyscallValue> arguments,
        out DwaineSyscallContext context,
        out DwaineSyscallResult failure)
    {
        context = default;
        failure = default;
        if (!TryGetRuntime(mainframe, out var config, out var runtime)
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem)
            || !_identities.TryGetStore(mainframe, out var identities))
        {
            failure = Fail(DwaineSyscallStatus.MainframeUnavailable, "syscall service unavailable");
            return false;
        }
        if (!_processes.TryGetProcess(mainframe, processId, out var process)
            || process.State is DwaineProcessState.Exited or DwaineProcessState.Faulted)
        {
            failure = Fail(DwaineSyscallStatus.InvalidCaller, "calling process unavailable");
            return false;
        }
        var principal = new DwainePrincipalId(process.Owner.Value);
        if (!principal.IsValid || !identities.TryGetAccount(principal, out var account) || !account.Enabled)
        {
            failure = Fail(DwaineSyscallStatus.InvalidCaller, "calling principal unavailable");
            return false;
        }
        var limits = DwaineSyscallLimits.FromComponent(config);
        if (!ValidateArguments(arguments, limits))
        {
            failure = Fail(DwaineSyscallStatus.LimitExceeded, "syscall argument limit exceeded");
            return false;
        }
        var cwd = DwaineFileSystemSystem.ToVfsHandle(process.WorkingDirectory);
        context = new DwaineSyscallContext(
            mainframe,
            process,
            principal,
            identities,
            new DwaineAuthorizedFileSystem(fileSystem, identities),
            cwd,
            runtime,
            limits);
        return true;
    }

    private bool TryValidateTerminalIdentity(
        in DwaineSyscallContext context,
        DwaineProcessTerminalSession terminal,
        out DwaineSessionId transportSession)
    {
        transportSession = new DwaineSessionId(terminal.Value);
        return _identities.TryGetSession(context.Mainframe, transportSession, out var identity) == DwaineIdentityResult.Success
               && identity.Principal == context.Principal;
    }

    private void OnKernelReady(Entity<DwaineSyscallComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineSyscallRuntimeComponent>(ent, out var runtime))
            return;
        Cleanup(runtime);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (!_kernel.TryRegisterService(ent.Owner, "syscalls", new SyscallKernelService(this, ent.Owner, args.BootGeneration)))
        {
            Cleanup(runtime);
            _kernel.Panic(ent.Owner, "syscall-service-registration");
        }
    }

    private static void OnRuntimeShutdown(Entity<DwaineSyscallRuntimeComponent> ent, ref ComponentShutdown args)
        => Cleanup(ent.Comp);

    private void OnProcessStateChanged(Entity<DwaineSyscallRuntimeComponent> ent, ref DwaineProcessStateChangedEvent args)
    {
        if (!ent.Comp.Online
            || args.Current is not (DwaineProcessState.Exited or DwaineProcessState.Faulted)
            || !_processes.TryGetProcess(ent.Owner, args.ProcessId, out var process)
            || process.ParentId is not { } parent)
        {
            return;
        }
        var payload = $"{process.ProcessId.Value}:{process.ExitCode ?? -1}";
        _processes.TrySendKernelMessage(ent.Owner, parent, DwaineKernelMessageType.TaskExit, payload);
    }

    private static void OnProcessRemoved(Entity<DwaineSyscallRuntimeComponent> ent, ref DwaineProcessRemovedEvent args)
    {
        var removed = args.ProcessId;
        ent.Comp.NextAuthenticationAt.Remove(removed);
        foreach (var correlation in ent.Comp.PendingReplies
                     .Where(pair => pair.Value.Requester == removed || pair.Value.Responder == removed)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            ent.Comp.PendingReplies.Remove(correlation);
        }
    }

    private bool TryGetRuntime(
        EntityUid mainframe,
        out DwaineSyscallComponent config,
        out DwaineSyscallRuntimeComponent runtime)
    {
        config = null!;
        runtime = null!;
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineSyscallComponent>(mainframe, out var foundConfig)
            || !TryComp<DwaineSyscallRuntimeComponent>(mainframe, out var foundRuntime))
        {
            return false;
        }
        config = foundConfig;
        runtime = foundRuntime;
        return runtime.Online
               && runtime.BootGeneration != 0
               && _kernel.GetState(mainframe) == DwaineSystemState.SystemReady;
    }

    private static void Cleanup(DwaineSyscallRuntimeComponent runtime)
    {
        runtime.NextAuthenticationAt.Clear();
        runtime.PendingReplies.Clear();
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private void PruneReplies(DwaineSyscallRuntimeComponent runtime)
    {
        foreach (var correlation in runtime.PendingReplies
                     .Where(pair => _timing.CurTime >= pair.Value.ExpiresAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            runtime.PendingReplies.Remove(correlation);
        }
    }

    private static bool ValidateArguments(IReadOnlyList<DwaineSyscallValue> arguments, DwaineSyscallLimits limits)
    {
        if (arguments is null || arguments.Count > limits.MaxArguments)
            return false;
        var characters = 0;
        foreach (var value in arguments)
        {
            if (value.Kind == DwaineSyscallValueKind.String)
            {
                if (value.Text is null || value.Text.IndexOf('\0') >= 0)
                    return false;
                characters += value.Text.Length;
                if (characters > limits.MaxArgumentCharacters)
                    return false;
            }
            else if (value.Kind == DwaineSyscallValueKind.DeviceHandle && !value.DeviceHandle.IsValid)
            {
                return false;
            }
        }
        return true;
    }

    private static DwaineSyscallResult BoundedString(in DwaineSyscallContext context, string value)
        => value.Length <= context.Limits.MaxResultCharacters
            ? DwaineSyscallResult.Success(DwaineSyscallValue.FromString(value))
            : Fail(DwaineSyscallStatus.LimitExceeded, "syscall result limit exceeded");

    private static bool OneString(IReadOnlyList<DwaineSyscallValue> args, out string value)
    {
        value = string.Empty;
        return args.Count == 1 && TryString(args[0], out value);
    }

    private static bool TryString(DwaineSyscallValue value, out string text)
    {
        text = value.Text ?? string.Empty;
        return value.Kind == DwaineSyscallValueKind.String;
    }

    private static bool TryInteger(DwaineSyscallValue value, out long integer)
    {
        integer = value.Integer;
        return value.Kind == DwaineSyscallValueKind.Integer;
    }

    private static bool TryBoolean(DwaineSyscallValue value, out bool boolean)
    {
        boolean = value.Boolean;
        return value.Kind == DwaineSyscallValueKind.Boolean;
    }

    private static DwaineSyscallResult Invalid(string message) => Fail(DwaineSyscallStatus.InvalidArguments, message);
    private static DwaineSyscallResult Fail(DwaineSyscallStatus status, string message) => DwaineSyscallResult.Failure(status, message);

    private static DwaineSyscallStatus MapIdentity(DwaineIdentityResult result) => result switch
    {
        DwaineIdentityResult.Success => DwaineSyscallStatus.Success,
        DwaineIdentityResult.UnknownAccount or DwaineIdentityResult.UnknownGroup => DwaineSyscallStatus.NotFound,
        DwaineIdentityResult.Throttled => DwaineSyscallStatus.RateLimited,
        DwaineIdentityResult.AccountLimit or DwaineIdentityResult.GroupLimit or DwaineIdentityResult.SessionLimit => DwaineSyscallStatus.LimitExceeded,
        _ => DwaineSyscallStatus.AccessDenied,
    };

    private static DwaineSyscallStatus MapVfs(DwaineVfsResult result) => result switch
    {
        DwaineVfsResult.Success => DwaineSyscallStatus.Success,
        DwaineVfsResult.NotFound or DwaineVfsResult.BrokenLink => DwaineSyscallStatus.NotFound,
        DwaineVfsResult.AccessDenied or DwaineVfsResult.ReadOnly => DwaineSyscallStatus.AccessDenied,
        DwaineVfsResult.AlreadyExists => DwaineSyscallStatus.Conflict,
        DwaineVfsResult.NodeLimit or DwaineVfsResult.DataLimit or DwaineVfsResult.ChildLimit => DwaineSyscallStatus.LimitExceeded,
        _ => DwaineSyscallStatus.FileSystemFailure,
    };

    private static DwaineSyscallStatus MapDevice(DwaineDeviceResult result) => result switch
    {
        DwaineDeviceResult.Success => DwaineSyscallStatus.Success,
        DwaineDeviceResult.MainframeUnavailable => DwaineSyscallStatus.MainframeUnavailable,
        DwaineDeviceResult.AccessDenied => DwaineSyscallStatus.AccessDenied,
        DwaineDeviceResult.NotFound => DwaineSyscallStatus.NotFound,
        DwaineDeviceResult.StaleHandle => DwaineSyscallStatus.StaleHandle,
        DwaineDeviceResult.Offline => DwaineSyscallStatus.Offline,
        DwaineDeviceResult.Unsupported => DwaineSyscallStatus.Unsupported,
        DwaineDeviceResult.RateLimited => DwaineSyscallStatus.RateLimited,
        DwaineDeviceResult.CapacityReached => DwaineSyscallStatus.LimitExceeded,
        DwaineDeviceResult.DuplicateAddress => DwaineSyscallStatus.Conflict,
        _ => DwaineSyscallStatus.InvalidArguments,
    };

    private static DwaineSyscallStatus MapProcess(DwaineProcessControlResult result) => result switch
    {
        DwaineProcessControlResult.Success => DwaineSyscallStatus.Success,
        DwaineProcessControlResult.MainframeUnavailable => DwaineSyscallStatus.MainframeUnavailable,
        DwaineProcessControlResult.ProcessNotFound => DwaineSyscallStatus.NotFound,
        DwaineProcessControlResult.AccessDenied => DwaineSyscallStatus.AccessDenied,
        _ => DwaineSyscallStatus.ProcessFailure,
    };

    private static DwaineSyscallStatus MapMessage(DwaineProcessMessageResult result) => result switch
    {
        DwaineProcessMessageResult.Success => DwaineSyscallStatus.Success,
        DwaineProcessMessageResult.MainframeUnavailable => DwaineSyscallStatus.MainframeUnavailable,
        DwaineProcessMessageResult.ProcessNotFound => DwaineSyscallStatus.NotFound,
        DwaineProcessMessageResult.AccessDenied => DwaineSyscallStatus.AccessDenied,
        DwaineProcessMessageResult.MailboxFull => DwaineSyscallStatus.LimitExceeded,
        _ => DwaineSyscallStatus.InvalidArguments,
    };

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (TryComp<DwaineSyscallRuntimeComponent>(mainframe, out var runtime)
            && runtime.BootGeneration == generation)
        {
            Cleanup(runtime);
        }
    }

    private sealed class SyscallKernelService(DwaineSyscallSystem system, EntityUid mainframe, ulong generation)
        : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }

    private readonly record struct DwaineSyscallContext(
        EntityUid Mainframe,
        DwaineProcessSnapshot Process,
        DwainePrincipalId Principal,
        DwaineIdentityStore Identities,
        DwaineAuthorizedFileSystem Files,
        DwaineVfsNodeHandle WorkingDirectory,
        DwaineSyscallRuntimeComponent Runtime,
        DwaineSyscallLimits Limits);
}
