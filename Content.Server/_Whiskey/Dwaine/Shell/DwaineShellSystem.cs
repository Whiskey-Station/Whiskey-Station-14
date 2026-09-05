// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Network;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Services;
using Content.Server._Whiskey.Dwaine.Storage;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Shell;
using Content.Server._Whiskey.VodkaCode.Runtime;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.Dwaine.Shell;

/// <summary>
/// Binds interactive shell processes to authenticated transport sessions.
/// Input, identity, cwd, process IDs and output routing are revalidated on the server.
/// </summary>
public sealed partial class DwaineShellSystem : EntitySystem
{
    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineDeviceSystem _devices = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineCommunicationSystem _communications = default!;
    [Dependency] private DwaineNetworkSystem _network = default!;
    [Dependency] private DwaineProcessSystem _processes = default!;
    [Dependency] private DwaineServiceSystem _services = default!;
    [Dependency] private DwaineStorageSystem _storage = default!;
    [Dependency] private DwaineTerminalTransportSystem _transport = default!;
    [Dependency] private VodkaRuntimeSystem _vodka = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _activeMainframes = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineShellComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineShellRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineShellRuntimeComponent, DwaineMainframeSessionConnectedEvent>(OnSessionConnected);
        SubscribeLocalEvent<DwaineShellRuntimeComponent, DwaineMainframeSessionDisconnectedEvent>(OnSessionDisconnected);
        SubscribeLocalEvent<DwaineShellRuntimeComponent, DwaineMainframeInputReceivedEvent>(OnInput);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var mainframe in _activeMainframes.ToArray())
        {
            if (TerminatingOrDeleted(mainframe)
                || !TryComp<DwaineShellRuntimeComponent>(mainframe, out var runtime)
                || !runtime.Online)
            {
                _activeMainframes.Remove(mainframe);
                continue;
            }

            // Transport and identity subscribe to the same connection event. Reconcile here so
            // initialization remains correct regardless of EntitySystem subscription order.
            if (TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var transport))
            {
                foreach (var transportSession in transport.Sessions.Keys)
                {
                    if (!runtime.Sessions.ContainsKey(transportSession))
                        TryEnsureShell(mainframe, runtime, transportSession, true);
                }
            }

            foreach (var (transportSession, shell) in runtime.Sessions.ToArray())
                FlushProcess(mainframe, runtime, transportSession, shell);
        }
    }

    private void OnKernelReady(Entity<DwaineShellComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineShellRuntimeComponent>(ent, out var runtime))
            return;

        CleanupSessions(ent.Owner, runtime);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        _activeMainframes.Add(ent.Owner);
        if (!_kernel.TryRegisterService(
                ent.Owner,
                "shell",
                new ShellKernelService(this, ent.Owner, args.BootGeneration)))
        {
            runtime.Online = false;
            runtime.BootGeneration = 0;
            _activeMainframes.Remove(ent.Owner);
            _kernel.Panic(ent.Owner, "shell-service-registration");
            return;
        }

        if (!TryComp<DwaineMainframeRuntimeComponent>(ent, out var transport))
            return;
        foreach (var session in transport.Sessions.Keys)
            TryEnsureShell(ent.Owner, runtime, session, true);
    }

    private void OnRuntimeShutdown(Entity<DwaineShellRuntimeComponent> ent, ref ComponentShutdown args)
    {
        CleanupSessions(ent.Owner, ent.Comp);
        ent.Comp.Online = false;
        ent.Comp.BootGeneration = 0;
        _activeMainframes.Remove(ent.Owner);
    }

    private void OnSessionConnected(
        Entity<DwaineShellRuntimeComponent> ent,
        ref DwaineMainframeSessionConnectedEvent args)
    {
        TryEnsureShell(ent.Owner, ent.Comp, args.Session, true);
    }

    private void OnSessionDisconnected(
        Entity<DwaineShellRuntimeComponent> ent,
        ref DwaineMainframeSessionDisconnectedEvent args)
    {
        RemoveSession(ent.Owner, ent.Comp, args.Session);
    }

    private void OnInput(Entity<DwaineShellRuntimeComponent> ent, ref DwaineMainframeInputReceivedEvent args)
    {
        if (ent.Comp.Sessions.TryGetValue(args.Session, out var existingShell)
            && existingShell.ProcessId is { } existingProcessId
            && (!_processes.TryGetProcess(ent.Owner, existingProcessId, out var existingProcess)
                || IsTerminal(existingProcess.State)))
        {
            // Drain the completed process before replacement so a fast next input cannot discard
            // its final stdout/stderr or identity-transition prompt.
            FlushProcess(ent.Owner, ent.Comp, args.Session, existingShell);
        }
        if (!ent.Comp.Online || !TryEnsureShell(ent.Owner, ent.Comp, args.Session, false))
        {
            _transport.WriteOutput(ent.Owner, args.Session, "shell: service unavailable");
            return;
        }

        var input = args.Text;
        if (_transport.TryReadInput(ent.Owner, args.Session, out var queued))
            input = queued;
        var shell = ent.Comp.Sessions[args.Session];
        if (shell.ProcessId is not { } processId || !_processes.TryWriteInput(ent.Owner, processId, input))
        {
            _transport.WriteOutput(ent.Owner, args.Session, "shell: process unavailable");
            shell.ProcessId = null;
            return;
        }

        shell.PromptPending = true;
    }

    private bool TryEnsureShell(
        EntityUid mainframe,
        DwaineShellRuntimeComponent runtime,
        DwaineSessionId transportSession,
        bool announce)
    {
        if (!runtime.Online
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady
            || !TryComp<DwaineShellComponent>(mainframe, out var config)
            || _identities.TryGetSession(mainframe, transportSession, out var identity) != DwaineIdentityResult.Success
            || !_identities.TryGetStore(mainframe, out var identities)
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
        {
            return false;
        }

        if (!runtime.Sessions.TryGetValue(transportSession, out var shell))
        {
            shell = new DwaineShellSession(DwaineShellLimits.FromComponent(config));
            runtime.Sessions.Add(transportSession, shell);
        }

        var owner = identities.ToProcessOwner(identity.Principal);
        if (shell.ProcessId is { } existingId
            && _processes.TryGetProcess(mainframe, existingId, out var existing)
            && !IsTerminal(existing.State)
            && existing.Owner == owner)
        {
            return true;
        }

        if (shell.ProcessId is { } staleId)
            _processes.TryKillAsOwner(mainframe, DwaineProcessOwner.System, staleId);
        shell.ProcessId = null;

        if (!identities.TryGetAccount(identity.Principal, out var account))
            return false;
        var home = account.Temporary ? "/home" : $"/home/{account.Name}";
        EnsureHome(fileSystem, account, home, _timing.CurTime);
        if (fileSystem.TryResolve(home, fileSystem.Root, out var homeHandle) == DwaineVfsResult.Success)
            shell.WorkingDirectory = homeHandle;
        else
            shell.WorkingDirectory = fileSystem.Root;
        shell.InitializeEnvironment(account.Name, home);

        var limits = DwaineShellLimits.FromComponent(config);
        var host = new ShellHost(this, mainframe, transportSession, identities, fileSystem);
        var engine = new DwaineShellEngine(limits);
        var spawn = _processes.TrySpawn(
            mainframe,
            new DwaineProcessSpawnRequest
            {
                Owner = owner,
                Program = new DwaineProgramDescriptor("dwaine.shell", "DWAINE interactive shell"),
                Implementation = new DwaineShellProcessProgram(engine, shell, host),
                WorkingDirectory = DwaineFileSystemSystem.ToWorkingDirectory(shell.WorkingDirectory),
                TerminalSession = new DwaineProcessTerminalSession(transportSession.Value),
                Environment = shell.Environment,
            },
            out var processId);
        if (spawn != DwaineProcessSpawnResult.Success)
            return false;

        shell.ProcessId = processId;
        shell.ProcessOwner = owner;
        if (announce)
        {
            _transport.WriteOutput(mainframe, transportSession, "DWAINE ready — type help for commands");
            WritePrompt(mainframe, transportSession, shell, identity, fileSystem);
        }
        return true;
    }

    private void FlushProcess(
        EntityUid mainframe,
        DwaineShellRuntimeComponent runtime,
        DwaineSessionId transportSession,
        DwaineShellSession shell)
    {
        if (shell.ProcessId is not { } processId)
        {
            TryEnsureShell(mainframe, runtime, transportSession, false);
            if (shell.ProcessId is not { } replacementId)
                return;
            processId = replacementId;
        }

        while (_processes.TryReadOutput(mainframe, processId, out var output))
            WriteLines(mainframe, transportSession, output);
        while (_processes.TryReadError(mainframe, processId, out var error))
            WriteLines(mainframe, transportSession, error);

        if (!_processes.TryGetProcess(mainframe, processId, out var process))
        {
            shell.ProcessId = null;
            TryEnsureShell(mainframe, runtime, transportSession, false);
            if (shell.ProcessId is not { } replacementId
                || !_processes.TryGetProcess(mainframe, replacementId, out process))
            {
                return;
            }
        }

        if (process.State == DwaineProcessState.Faulted)
        {
            _transport.WriteOutput(mainframe, transportSession, $"shell: process terminated: {process.ErrorCode}");
            shell.ProcessId = null;
        }
        else if (process.State == DwaineProcessState.Exited)
        {
            shell.ProcessId = null;
        }

        if (!shell.PromptPending || _timing.CurTime < shell.SleepingUntil)
            return;
        if (shell.ProcessId is not null && process.State != DwaineProcessState.Waiting)
            return;
        if (_identities.TryGetSession(mainframe, transportSession, out var identity) != DwaineIdentityResult.Success
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
        {
            return;
        }

        if (shell.ProcessId is null)
        {
            if (!TryEnsureShell(mainframe, runtime, transportSession, false))
                return;
        }
        WritePrompt(mainframe, transportSession, shell, identity, fileSystem);
        shell.PromptPending = false;
    }

    private void WritePrompt(
        EntityUid mainframe,
        DwaineSessionId transportSession,
        DwaineShellSession shell,
        DwaineIdentitySessionSnapshot identity,
        DwaineVirtualFileSystem fileSystem)
    {
        var user = _identities.TryGetStore(mainframe, out var identities)
                   && identities.TryGetAccount(identity.Principal, out var account)
            ? account.Name
            : "unknown";
        var path = fileSystem.TryGetPath(shell.WorkingDirectory, out var current) == DwaineVfsResult.Success
            ? current
            : "/";
        _transport.WriteOutput(mainframe, transportSession, $"{user}@dwaine:{path}$");
    }

    private void WriteLines(EntityUid mainframe, DwaineSessionId transportSession, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var count = normalized.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        for (var index = 0; index < count; index++)
            _transport.WriteOutput(mainframe, transportSession, lines[index]);
    }

    private static void EnsureHome(
        DwaineVirtualFileSystem fileSystem,
        DwaineAccountSnapshot account,
        string home,
        TimeSpan now)
    {
        if (account.Temporary || fileSystem.TryResolve(home, fileSystem.Root, out _) == DwaineVfsResult.Success)
            return;
        fileSystem.TryCreate(
            home,
            fileSystem.Root,
            new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Directory,
                Owner = account.Principal.Value,
                Group = DwaineGroupId.Users.Value,
                Mode = DwaineVfsMode.OwnerAll | DwaineVfsMode.GroupReadExecute,
            },
            now,
            out _);
    }

    private void RemoveSession(
        EntityUid mainframe,
        DwaineShellRuntimeComponent runtime,
        DwaineSessionId transportSession)
    {
        if (!runtime.Sessions.Remove(transportSession, out var shell))
            return;
        if (shell.ProcessId is { } processId)
            _processes.TryKillAsOwner(mainframe, DwaineProcessOwner.System, processId);
    }

    private void CleanupSessions(EntityUid mainframe, DwaineShellRuntimeComponent runtime)
    {
        foreach (var transportSession in runtime.Sessions.Keys.ToArray())
            RemoveSession(mainframe, runtime, transportSession);
        runtime.Sessions.Clear();
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong bootGeneration)
    {
        if (!TryComp<DwaineShellRuntimeComponent>(mainframe, out var runtime)
            || runtime.BootGeneration != bootGeneration)
        {
            return;
        }
        CleanupSessions(mainframe, runtime);
        runtime.Online = false;
        runtime.BootGeneration = 0;
        _activeMainframes.Remove(mainframe);
    }

    private static bool IsTerminal(DwaineProcessState state)
    {
        return state is DwaineProcessState.Exited or DwaineProcessState.Faulted;
    }

    private sealed class ShellKernelService(
        DwaineShellSystem system,
        EntityUid mainframe,
        ulong bootGeneration) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == bootGeneration)
                system.OnKernelServiceShutdown(mainframe, bootGeneration);
        }
    }

    private sealed class ShellHost(
        DwaineShellSystem system,
        EntityUid mainframe,
        DwaineSessionId transportSession,
        DwaineIdentityStore identities,
        DwaineVirtualFileSystem fileSystem) : IDwaineShellHost, IDwaineVodkaShellHost, IDwaineNetworkShellHost, IDwaineServiceShellHost
    {
        public TimeSpan Now => system._timing.CurTime;
        public DwaineIdentitySessionSnapshot Identity =>
            system._identities.TryGetSession(mainframe, transportSession, out var identity)
                == DwaineIdentityResult.Success
                ? identity
                : throw new InvalidOperationException("identity session is no longer valid");
        public DwaineIdentityStore Identities => identities;
        public DwaineAuthorizedFileSystem Files { get; } = new(fileSystem, identities);

        public DwaineVfsResult TryGetPath(DwaineVfsNodeHandle handle, out string path)
        {
            return fileSystem.TryGetPath(handle, out path);
        }

        public DwaineVfsResult TryCanonicalize(
            string path,
            DwaineVfsNodeHandle workingDirectory,
            out string canonical)
        {
            return fileSystem.TryCanonicalize(path, workingDirectory, out canonical);
        }

        public DwaineIdentityResult TryElevate(
            string name,
            string password,
            out DwaineIdentitySessionSnapshot session)
        {
            return identities.TryElevate(Identity.Session, name, password, Now, out session);
        }

        public DwaineIdentityResult TryBootstrap(
            string name,
            string password,
            out DwaineIdentitySessionSnapshot session)
        {
            return identities.TryBootstrapOperator(Identity.Session, name, password, Now, out session);
        }

        public DwaineIdentityResult TryCreateAccount(
            string name,
            string password,
            out DwaineAccountSnapshot account)
        {
            return identities.TryCreateManagedAccount(Identity.Principal, name, password, out account);
        }

        public DwaineIdentityResult TryLogout(out DwaineIdentitySessionSnapshot session)
        {
            var result = system._identities.TryLogout(mainframe, transportSession);
            if (result == DwaineIdentityResult.Success)
                return system._identities.TryGetSession(mainframe, transportSession, out session);
            session = default;
            return result;
        }

        public IReadOnlyList<DwaineShellUserEntry> GetUsers()
        {
            var users = new List<DwaineShellUserEntry>();
            var shellRuntime = system.CompOrNull<DwaineShellRuntimeComponent>(mainframe);
            foreach (var session in identities.GetSessions(Now))
            {
                if (!identities.TryGetAccount(session.Principal, out var account))
                    continue;
                var messages = shellRuntime?.Sessions.TryGetValue(new DwaineSessionId(session.Terminal), out var shell) == true
                               && shell.MessagesEnabled;
                users.Add(new DwaineShellUserEntry(account.Name, account.Temporary, messages));
            }
            return users.OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public DwaineShellHostResult Talk(string target, string message)
        {
            if (message.Length == 0
                || message.Length > 1024
                || message.Any(character => character is '\r' or '\n' or '\0'
                                            || char.IsControl(character) && character != '\t'))
            {
                return DwaineShellHostResult.Failure("talk: message limit exceeded\n");
            }
            var shellRuntime = system.CompOrNull<DwaineShellRuntimeComponent>(mainframe);
            if (shellRuntime is null)
                return DwaineShellHostResult.Failure("talk: shell service unavailable\n");
            foreach (var session in identities.GetSessions(Now))
            {
                if (!identities.TryGetAccount(session.Principal, out var account)
                    || !string.Equals(account.Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var targetSession = new DwaineSessionId(session.Terminal);
                if (!shellRuntime.Sessions.TryGetValue(targetSession, out var targetShell)
                    || !targetShell.MessagesEnabled)
                {
                    return DwaineShellHostResult.Failure("talk: target is not accepting messages\n");
                }
                var sender = identities.TryGetAccount(Identity.Principal, out var senderAccount)
                    ? senderAccount.Name
                    : "unknown";
                return system._transport.WriteOutput(mainframe, targetSession, $"message from {sender}: {message}")
                    ? DwaineShellHostResult.Success()
                    : DwaineShellHostResult.Failure("talk: target disconnected\n");
            }
            return DwaineShellHostResult.Failure("talk: user not found\n");
        }

        public DwaineShellHostResult Mount(string label, string path)
        {
            if (!identities.HasPermission(Identity.Principal, DwaineIdentityPermission.Write))
                return DwaineShellHostResult.Failure("mount: permission denied\n");
            var media = FindMedia(label);
            if (media is null)
                return DwaineShellHostResult.Failure("mount: media not found\n");
            return StorageResult("mount", system._storage.TryMount(mainframe, media.Value.Media, path));
        }

        public DwaineShellHostResult Unmount(string label)
        {
            if (!identities.HasPermission(Identity.Principal, DwaineIdentityPermission.Write))
                return DwaineShellHostResult.Failure("mount: permission denied\n");
            var media = FindMedia(label);
            if (media is null)
                return DwaineShellHostResult.Failure("mount: media not found\n");
            return StorageResult("mount", system._storage.TryUnmount(mainframe, media.Value.Media));
        }

        public DwaineShellHostResult ListMedia()
        {
            var media = system._storage.GetInsertedMedia(mainframe);
            var lines = media.Select(snapshot =>
                $"{snapshot.Label} {snapshot.Kind} {(snapshot.MountedOn == mainframe ? snapshot.MountPath : "unmounted")} {(snapshot.ReadOnly ? "ro" : "rw")}");
            return DwaineShellHostResult.Success(string.Join('\n', lines) + (media.Length > 0 ? "\n" : string.Empty));
        }

        public void ClearScreen()
        {
            if (system.TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var runtime)
                && runtime.Sessions.TryGetValue(transportSession, out var session))
            {
                session.Output.Clear();
            }
        }

        public DwaineShellProgramStartResult TryStartVodka(
            DwaineProcessId parent,
            DwaineVfsNodeHandle workingDirectory,
            string path,
            IReadOnlyList<string> arguments)
        {
            var started = system._vodka.TryStart(
                mainframe,
                Identity.Principal,
                parent,
                DwaineFileSystemSystem.ToWorkingDirectory(workingDirectory),
                path,
                arguments,
                true);
            return new DwaineShellProgramStartResult(started.Succeeded, started.ProcessId, started.Error);
        }

        public bool TryTakeVodkaOutput(DwaineProcessId processId, out DwaineShellProgramOutput output)
        {
            output = default;
            if (!system._vodka.TryTakeCapturedOutput(mainframe, processId, out var captured))
                return false;
            output = new DwaineShellProgramOutput(
                captured.StandardOutput,
                captured.StandardError,
                captured.ExitCode,
                captured.ErrorCode);
            return true;
        }

        public DwaineShellHostResult Network(IReadOnlyList<string> arguments, DwaineVfsNodeHandle workingDirectory)
        {
            if (arguments.Count == 1 && arguments[0] == "address")
            {
                var result = system._network.GetNode(mainframe, out var node);
                return result == DwaineNetworkResult.Success
                    ? DwaineShellHostResult.Success(node.Address.Value + "\n")
                    : NetworkFailure(result);
            }
            if (arguments.Count == 1 && arguments[0] == "status")
            {
                var result = system._network.GetNode(mainframe, out var node);
                return result == DwaineNetworkResult.Success
                    ? DwaineShellHostResult.Success(
                        $"{node.Address.Value} {node.NetworkId} {node.Adapter.ToString().ToLowerInvariant()} " +
                        $"{node.Frequency} {node.Channel} online\n")
                    : NetworkFailure(result);
            }
            if (arguments.Count is 1 or 2 && arguments[0] == "discover")
            {
                var result = system._network.Discover(
                    mainframe,
                    arguments.Count == 2 ? arguments[1] : null,
                    out var nodes);
                if (result != DwaineNetworkResult.Success)
                    return NetworkFailure(result);
                var lines = nodes.Select(node =>
                    $"{node.Address.Value}\t{string.Join(',', node.Tags)}\t{node.Adapter.ToString().ToLowerInvariant()}");
                return DwaineShellHostResult.Success(string.Join('\n', lines) + (nodes.Length > 0 ? "\n" : string.Empty));
            }
            if (arguments.Count == 2 && arguments[0] == "ping")
            {
                var result = system._network.TryRequest(
                    mainframe,
                    arguments[1],
                    "dwaine.ping",
                    string.Empty,
                    out var correlation);
                if (result is not (DwaineNetworkResult.Success or DwaineNetworkResult.Pending))
                    return NetworkFailure(result);
                result = system._network.TryTakeReply(mainframe, correlation, out var reply);
                return result == DwaineNetworkResult.Success
                    ? DwaineShellHostResult.Success(reply + "\n")
                    : NetworkFailure(result);
            }
            if (arguments.Count >= 4 && arguments[0] == "send")
            {
                var result = system._communications.TrySend(
                    mainframe,
                    Identity.Principal,
                    arguments[1],
                    arguments[2],
                    string.Join(' ', arguments.Skip(3)));
                return result == DwaineNetworkResult.Success
                    ? DwaineShellHostResult.Success("sent\n")
                    : NetworkFailure(result);
            }
            if (arguments.Count == 4 && arguments[0] == "sendfile")
            {
                var result = system._communications.TrySendFile(
                    mainframe,
                    Identity.Principal,
                    arguments[1],
                    arguments[2],
                    arguments[3],
                    workingDirectory,
                    out var receivedPath);
                return result == DwaineNetworkResult.Success
                    ? DwaineShellHostResult.Success($"sent {receivedPath}\n")
                    : NetworkFailure(result);
            }
            if (arguments.Count == 1 && arguments[0] == "inbox")
            {
                var output = new StringBuilder();
                for (var count = 0; count < 64; count++)
                {
                    var result = system._communications.TryReceive(mainframe, Identity.Principal, out var message);
                    if (result == DwaineNetworkResult.NotFound)
                        break;
                    if (result != DwaineNetworkResult.Success)
                        return NetworkFailure(result);
                    output.Append(message.SourceAddress)
                        .Append(' ')
                        .Append(message.Sender)
                        .Append(": ")
                        .AppendLine(message.Message);
                }
                return DwaineShellHostResult.Success(output.ToString());
            }
            if (arguments.Count == 1 && arguments[0] == "metrics")
            {
                if (!identities.HasPermission(Identity.Principal, DwaineIdentityPermission.InspectSessions))
                    return DwaineShellHostResult.Failure("net: permission denied\n");
                var metrics = system._network.GetMetrics(mainframe);
                return DwaineShellHostResult.Success(
                    $"sent={metrics.Sent} delivered={metrics.Delivered} dropped={metrics.Dropped} " +
                    $"discoveries={metrics.Discoveries} requests={metrics.Requests} replies={metrics.Replies} " +
                    $"pending={metrics.PendingRequests} capture={metrics.CapturedEntries}\n");
            }
            if (arguments.Count == 1 && arguments[0] == "capture")
            {
                if (!identities.HasPermission(Identity.Principal, DwaineIdentityPermission.InspectSessions))
                    return DwaineShellHostResult.Failure("net: permission denied\n");
                var entries = system._network.GetCapture(mainframe);
                var lines = entries.Select(entry =>
                    $"{entry.Source}->{entry.Destination} {entry.Protocol} bytes={entry.PayloadCharacters} {entry.Result.ToString().ToLowerInvariant()}");
                return DwaineShellHostResult.Success(string.Join('\n', lines) + (entries.Length > 0 ? "\n" : string.Empty));
            }
            return DwaineShellHostResult.Failure(
                "usage: net address|status|discover [TAG]|ping ADDRESS|send ADDRESS USER MESSAGE...|sendfile ADDRESS USER FILE|inbox|metrics|capture\n");
        }

        public DwaineShellHostResult Scan(DwaineProcessId process)
        {
            var discovery = system._network.Discover(mainframe, null, out var nodes);
            if (discovery != DwaineNetworkResult.Success)
                return NetworkFailure(discovery);
            var scan = system._devices.TryScan(mainframe, process, Identity.Principal, out _);
            if (scan != Content.Server._Whiskey.Dwaine.Devices.DwaineDeviceResult.Success)
                return DwaineShellHostResult.Failure($"scnt: {scan.ToString().ToLowerInvariant()}\n");
            var devices = system._devices.ListDevices(mainframe, process, Identity.Principal);
            var output = new StringBuilder();
            foreach (var node in nodes)
                output.Append("network ").Append(node.Address.Value).Append(' ').AppendLine(string.Join(',', node.Tags));
            foreach (var device in devices)
                output.Append("device ").Append(device.Address).Append(' ').Append(device.DriverId).Append(' ')
                    .AppendLine(device.Status.ToString().ToLowerInvariant());
            return DwaineShellHostResult.Success(output.ToString());
        }

        public DwaineShellHostResult Service(
            DwaineProcessId process,
            DwaineVfsNodeHandle workingDirectory,
            IReadOnlyList<string> arguments)
        {
            var response = arguments.Count == 1 && arguments[0] == "list"
                ? system._services.ListServices(mainframe, process, Identity.Principal)
                : arguments.Count >= 2
                    ? system._services.Call(
                        mainframe,
                        process,
                        Identity.Principal,
                        arguments[0],
                        arguments[1],
                        arguments.Skip(2).ToArray(),
                        workingDirectory)
                    : DwaineServiceResponse.Failure(
                        DwaineServiceStatus.InvalidArguments,
                        "usage: service list|SERVICE OPERATION [argument...]\n");
            return response.Succeeded
                ? DwaineShellHostResult.Success(response.Output)
                : DwaineShellHostResult.Failure(response.Output);
        }

        private DwaineStorageMediaSnapshot? FindMedia(string label)
        {
            foreach (var snapshot in system._storage.GetInsertedMedia(mainframe))
            {
                if (string.Equals(snapshot.Label, label, StringComparison.OrdinalIgnoreCase))
                    return snapshot;
            }

            return null;
        }

        private static DwaineShellHostResult StorageResult(
            string command,
            DwaineStorageOperationResult result)
        {
            return result.Succeeded
                ? DwaineShellHostResult.Success()
                : DwaineShellHostResult.Failure(
                    $"{command}: {result.Result.ToString().ToLowerInvariant()}" +
                    (result.FileSystemResult == DwaineVfsResult.Success
                        ? "\n"
                        : $" ({result.FileSystemResult.ToString().ToLowerInvariant()})\n"));
        }

        private static DwaineShellHostResult NetworkFailure(DwaineNetworkResult result)
            => DwaineShellHostResult.Failure($"net: {result.ToString().ToLowerInvariant()}\n");
    }
}
