// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Network;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Services;
using Content.Server._Whiskey.Dwaine.Syscalls;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.VodkaCode;
using Content.Server._Whiskey.VodkaCode.Frontend;
using Robust.Shared.Timing;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

/// <summary>
/// Owns Vodka Code process creation and the narrow VFS host boundary. Script source, principals,
/// working directories, seeds and process relationships are always selected or revalidated here.
/// </summary>
internal sealed partial class VodkaRuntimeSystem : EntitySystem
{
    private const int MaximumDiagnosticCharacters = 4096;

    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineCommunicationSystem _communications = default!;
    [Dependency] private DwaineNetworkSystem _network = default!;
    [Dependency] private DwaineProcessSystem _processes = default!;
    [Dependency] private DwaineServiceSystem _services = default!;
    [Dependency] private DwaineSyscallSystem _syscalls = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VodkaRuntimeComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<VodkaRuntimeStateComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<VodkaRuntimeStateComponent, DwaineProcessStateChangedEvent>(OnProcessStateChanged);
        SubscribeLocalEvent<VodkaRuntimeStateComponent, DwaineProcessRemovedEvent>(OnProcessRemoved);
    }

    public VodkaStartResult TryStart(
        EntityUid mainframe,
        DwainePrincipalId principal,
        DwaineProcessId? parent,
        DwaineWorkingDirectoryHandle workingDirectory,
        string path,
        IReadOnlyList<string> arguments,
        bool captureOutput)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<VodkaRuntimeComponent>(mainframe, out var config)
            || !TryComp<VodkaRuntimeStateComponent>(mainframe, out var runtime)
            || !runtime.Online
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem)
            || !_identities.TryGetStore(mainframe, out var identities))
        {
            return Failure(VodkaSpawnResult.RuntimeUnavailable, "vodka: runtime unavailable\n");
        }

        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
            return Failure(VodkaSpawnResult.InvalidPath, "vodka: invalid script path\n");
        if (!path.EndsWith(VodkaCodeSpecification.FileExtension, StringComparison.Ordinal))
            return Failure(VodkaSpawnResult.InvalidExtension, $"vodka: script must use {VodkaCodeSpecification.FileExtension}\n");

        var limits = VodkaRuntimeLimits.FromComponent(config);
        if (!ValidateArguments(arguments, limits))
            return Failure(VodkaSpawnResult.InvalidArguments, "vodka: argument limit exceeded\n");

        var authorized = new DwaineAuthorizedFileSystem(fileSystem, identities);
        var cwd = DwaineFileSystemSystem.ToVfsHandle(workingDirectory);
        var read = authorized.TryReadText(principal, path, cwd, out var source);
        if (read != DwaineVfsResult.Success)
        {
            return read == DwaineVfsResult.AccessDenied
                ? Failure(VodkaSpawnResult.AccessDenied, $"vodka: permission denied: {path}\n")
                : Failure(VodkaSpawnResult.FileUnavailable, $"vodka: cannot read {path}: {FileError(read)}\n");
        }

        var compilation = VodkaCompiler.Compile(source, limits.MaxCallDepth);
        if (!compilation.Succeeded)
            return Failure(VodkaSpawnResult.SyntaxError, FormatDiagnostics(compilation.Diagnostics));

        var seed = runtime.NextSeed++;
        if (runtime.NextSeed == 0)
            runtime.NextSeed = 1;
        var displayPath = path.Length <= 72 ? path : path[^72..];
        var host = new DwaineVodkaHost(
            this,
            authorized,
            principal,
            cwd,
            workingDirectory,
            mainframe,
            runtime,
            displayPath);
        var machine = new VodkaVirtualMachine(compilation.Program!, limits, host, arguments, seed);
        var spawn = _processes.TrySpawn(
            mainframe,
            new DwaineProcessSpawnRequest
            {
                Owner = identities.ToProcessOwner(principal),
                ParentId = parent,
                Program = new DwaineProgramDescriptor("vodka-script", $"Vodka Code: {displayPath}"),
                Implementation = new VodkaProcessProgram(machine),
                WorkingDirectory = workingDirectory,
                Environment = BuildEnvironment(arguments),
            },
            out var processId);
        if (spawn != DwaineProcessSpawnResult.Success)
            return Failure(VodkaSpawnResult.ProcessRejected, $"vodka: process rejected: {spawn.ToString().ToLowerInvariant()}\n");

        host.Bind(processId, machine);
        runtime.ActiveScripts.Add(processId, new VodkaActiveScript(captureOutput, parent));
        return new VodkaStartResult(VodkaSpawnResult.Success, processId, string.Empty);
    }

    public bool TryTakeCapturedOutput(EntityUid mainframe, DwaineProcessId processId, out VodkaCompletedOutput output)
    {
        output = default;
        if (!TryComp<VodkaRuntimeStateComponent>(mainframe, out var runtime)
            || !runtime.CapturedOutput.Remove(processId, out var captured))
        {
            return false;
        }

        output = captured.Output;
        return true;
    }

    private void OnKernelReady(Entity<VodkaRuntimeComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<VodkaRuntimeStateComponent>(ent, out var runtime))
            return;

        Cleanup(runtime);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        runtime.NextSeed = 1;
        if (_kernel.TryRegisterService(
                ent.Owner,
                "vodka-runtime",
                new VodkaKernelService(this, ent.Owner, args.BootGeneration)))
        {
            return;
        }

        Cleanup(runtime);
        _kernel.Panic(ent.Owner, "vodka-service-registration");
    }

    private void OnRuntimeShutdown(Entity<VodkaRuntimeStateComponent> ent, ref ComponentShutdown args)
    {
        Cleanup(ent.Comp);
    }

    private void OnProcessStateChanged(
        Entity<VodkaRuntimeStateComponent> ent,
        ref DwaineProcessStateChangedEvent args)
    {
        if (!ent.Comp.Online
            || ent.Comp.BootGeneration != args.BootGeneration
            || args.Current is not (DwaineProcessState.Exited or DwaineProcessState.Faulted))
        {
            return;
        }

        if (ent.Comp.ActiveScripts.Remove(args.ProcessId, out var active)
            && active.CaptureOutput)
        {
            var stdout = DrainOutput(ent.Owner, args.ProcessId, false);
            var stderr = DrainOutput(ent.Owner, args.ProcessId, true);
            var exitCode = 1;
            var errorCode = string.Empty;
            if (_processes.TryGetProcess(ent.Owner, args.ProcessId, out var snapshot))
            {
                exitCode = snapshot.ExitCode ?? 1;
                errorCode = snapshot.ErrorCode;
                if (stderr.Length == 0 && args.Current == DwaineProcessState.Faulted)
                    stderr = FriendlyProcessFault(snapshot);
            }
            ent.Comp.CapturedOutput[args.ProcessId] = new VodkaCapturedOutput(
                active.ParentId,
                new VodkaCompletedOutput(stdout, stderr, exitCode, errorCode));
        }

        RemoveCapturedChildren(ent.Comp, args.ProcessId);
    }

    private void OnProcessRemoved(Entity<VodkaRuntimeStateComponent> ent, ref DwaineProcessRemovedEvent args)
    {
        ent.Comp.ActiveScripts.Remove(args.ProcessId);
        RemoveCapturedChildren(ent.Comp, args.ProcessId);
    }

    private string DrainOutput(EntityUid mainframe, DwaineProcessId processId, bool error)
    {
        var output = new StringBuilder();
        while ((error
                   ? _processes.TryReadError(mainframe, processId, out var chunk)
                   : _processes.TryReadOutput(mainframe, processId, out chunk))
               && output.Length <= VodkaRuntimeComponent.HardMaxOutputBytes)
        {
            output.Append(chunk);
        }
        return output.ToString();
    }

    private static string FriendlyProcessFault(DwaineProcessSnapshot snapshot)
    {
        return snapshot.ExitReason == DwaineProcessExitReason.InstructionLimit
            ? "vodka: process terminated: instruction budget exceeded\n"
            : $"vodka: process terminated: {snapshot.ErrorCode.Replace('-', ' ')}\n";
    }

    private static bool ValidateArguments(IReadOnlyList<string> arguments, VodkaRuntimeLimits limits)
    {
        if (arguments.Count > limits.MaxArguments)
            return false;
        var bytes = 0;
        foreach (var argument in arguments)
        {
            if (argument is null
                || argument.IndexOf('\0') >= 0
                || !HasValidUtf16(argument))
                return false;
            var argumentBytes = Encoding.UTF8.GetByteCount(argument);
            if (argumentBytes > limits.MaxStringBytes
                || argumentBytes > limits.MaxArgumentBytes - bytes)
                return false;
            bytes += argumentBytes;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironment(IReadOnlyList<string> arguments)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VODKA_ARGC"] = arguments.Count.ToString(),
        };
    }

    private static bool HasValidUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
                continue;
            if (!char.IsHighSurrogate(value[index])
                || index + 1 >= value.Length
                || !char.IsLowSurrogate(value[index + 1]))
            {
                return false;
            }
            index++;
        }
        return true;
    }

    private static void RemoveCapturedChildren(VodkaRuntimeStateComponent runtime, DwaineProcessId parent)
    {
        foreach (var (processId, captured) in runtime.CapturedOutput.ToArray())
        {
            if (captured.ParentId == parent)
                runtime.CapturedOutput.Remove(processId);
        }
    }

    private static VodkaStartResult Failure(VodkaSpawnResult result, string error)
    {
        return new VodkaStartResult(result, default, error);
    }

    private static string FormatDiagnostics(IReadOnlyList<VodkaDiagnostic> diagnostics)
    {
        var output = new StringBuilder();
        foreach (var diagnostic in diagnostics.Take(16))
        {
            var line = diagnostic.ToTerminalMessage() + "\n";
            if (line.Length > MaximumDiagnosticCharacters - output.Length)
                break;
            output.Append(line);
        }
        return output.Length > 0 ? output.ToString() : "vodka: syntax error\n";
    }

    private static string FileError(DwaineVfsResult result)
    {
        return result.ToString().ToLowerInvariant();
    }

    private static void Cleanup(VodkaRuntimeStateComponent runtime)
    {
        runtime.ActiveScripts.Clear();
        runtime.CapturedOutput.Clear();
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (!TryComp<VodkaRuntimeStateComponent>(mainframe, out var runtime)
            || runtime.BootGeneration != generation)
        {
            return;
        }
        Cleanup(runtime);
    }

    private sealed class VodkaKernelService(
        VodkaRuntimeSystem system,
        EntityUid mainframe,
        ulong generation) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }

    private DwaineSyscallResult TryFork(
        DwaineVodkaHost parentHost,
        VodkaVirtualMachine parentMachine,
        EntityUid mainframe,
        DwainePrincipalId principal,
        DwaineWorkingDirectoryHandle workingDirectory,
        DwaineVfsNodeHandle vfsWorkingDirectory,
        VodkaRuntimeStateComponent runtime,
        DwaineProcessId parent,
        string displayPath,
        IReadOnlyList<string> arguments)
    {
        const int maximumForkDepth = 32;
        var cursor = parent;
        for (var depth = 0; depth < maximumForkDepth; depth++)
        {
            if (!_processes.TryGetProcess(mainframe, cursor, out var ancestor))
                return DwaineSyscallResult.Failure(DwaineSyscallStatus.InvalidCaller, "fork parent unavailable");
            if (ancestor.ParentId is not { } next)
                break;
            cursor = next;
            if (depth == maximumForkDepth - 1)
                return DwaineSyscallResult.Failure(DwaineSyscallStatus.LimitExceeded, "fork depth limit exceeded");
        }

        var childHost = new DwaineVodkaHost(
            this,
            parentHost.Files,
            principal,
            vfsWorkingDirectory,
            workingDirectory,
            mainframe,
            runtime,
            displayPath);
        var childMachine = parentMachine.Fork(childHost, arguments);
        if (childMachine.State == VodkaExecutionState.Faulted || !childMachine.TryAcceptForkResult(0))
            return DwaineSyscallResult.Failure(DwaineSyscallStatus.LimitExceeded, "fork state exceeds runtime limits");

        var spawn = _processes.TrySpawn(
            mainframe,
            new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(principal.Value),
                ParentId = parent,
                Program = new DwaineProgramDescriptor("vodka-script", $"Vodka Code fork: {displayPath}"),
                Implementation = new VodkaProcessProgram(childMachine),
                WorkingDirectory = workingDirectory,
            },
            out var child);
        if (spawn != DwaineProcessSpawnResult.Success)
            return DwaineSyscallResult.Failure(DwaineSyscallStatus.ProcessFailure, $"fork rejected: {spawn.ToString().ToLowerInvariant()}");

        childHost.Bind(child, childMachine);
        runtime.ActiveScripts.Add(child, new VodkaActiveScript(false, parent));
        return DwaineSyscallResult.Success(DwaineSyscallValue.FromInteger((long) child.Value));
    }

    private sealed class DwaineVodkaHost : IVodkaRuntimeHost, IDwaineSyscallProgramBridge
    {
        private readonly VodkaRuntimeSystem _system;
        private readonly DwainePrincipalId _principal;
        private readonly DwaineVfsNodeHandle _vfsWorkingDirectory;
        private readonly DwaineWorkingDirectoryHandle _workingDirectory;
        private readonly EntityUid _mainframe;
        private readonly VodkaRuntimeStateComponent _runtime;
        private readonly string _displayPath;
        private DwaineProcessId _processId;
        private VodkaVirtualMachine? _machine;

        public DwaineAuthorizedFileSystem Files { get; }
        public TimeSpan Now => _system._timing.CurTime;

        public DwaineVodkaHost(
            VodkaRuntimeSystem system,
            DwaineAuthorizedFileSystem files,
            DwainePrincipalId principal,
            DwaineVfsNodeHandle vfsWorkingDirectory,
            DwaineWorkingDirectoryHandle workingDirectory,
            EntityUid mainframe,
            VodkaRuntimeStateComponent runtime,
            string displayPath)
        {
            _system = system;
            Files = files;
            _principal = principal;
            _vfsWorkingDirectory = vfsWorkingDirectory;
            _workingDirectory = workingDirectory;
            _mainframe = mainframe;
            _runtime = runtime;
            _displayPath = displayPath;
        }

        public void Bind(DwaineProcessId processId, VodkaVirtualMachine machine)
        {
            if (_processId.IsValid || _machine is not null)
                throw new InvalidOperationException("Vodka host is already bound");
            _processId = processId;
            _machine = machine;
        }

        public VodkaHostCallResult Invoke(string name, IReadOnlyList<VodkaValue> arguments)
        {
            if (name is "fs.exists" or "fs.is_directory" or "fs.is_file" or "fs.is_executable")
            {
                if (arguments.Count != 1 || arguments[0].Kind != VodkaValueKind.String)
                    return VodkaHostCallResult.Failure(VodkaHostCallStatus.InvalidArguments, $"{name} expects one path string");
                var path = arguments[0].Text;
                return name switch
                {
                    "fs.exists" => Stat(path, _ => true),
                    "fs.is_directory" => Stat(path, snapshot => snapshot.Kind == DwaineVfsNodeKind.Directory),
                    "fs.is_file" => Stat(path, snapshot => snapshot.Kind != DwaineVfsNodeKind.Directory),
                    "fs.is_executable" => IsExecutable(path),
                    _ => throw new InvalidOperationException("validated Vodka host function was not dispatched"),
                };
            }

            if (name.StartsWith("sys.network.", StringComparison.Ordinal))
                return Network(name, arguments);
            if (name.StartsWith("sys.service.", StringComparison.Ordinal))
                return Service(name, arguments);

            if (!_processId.IsValid || _machine is null || !TryMapCall(name, out var syscall))
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.UnknownFunction, $"unknown function: {name}");
            if (!TryConvertArguments(name, syscall, arguments, out var converted, out var conversionError))
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.InvalidArguments, conversionError);

            var result = _system._syscalls.Execute(_mainframe, _processId, syscall, converted, this);
            if (result.Effect == DwaineSyscallEffect.ExitProcess)
                return VodkaHostCallResult.Exit(result.ExitCode);
            if (!result.Succeeded)
                return VodkaHostCallResult.Failure(MapStatus(result.Status), result.Error);
            if (syscall is DwaineSyscallId.TaskSpawn or DwaineSyscallId.TaskFork)
                _machine.RequestYield();
            return VodkaHostCallResult.Success(result.Value.Kind switch
            {
                DwaineSyscallValueKind.Null => VodkaValue.Null,
                DwaineSyscallValueKind.Integer => VodkaValue.FromInteger(result.Value.Integer),
                DwaineSyscallValueKind.Boolean => VodkaValue.FromBoolean(result.Value.Boolean),
                DwaineSyscallValueKind.String => VodkaValue.FromString(result.Value.Text),
                DwaineSyscallValueKind.DeviceHandle => VodkaValue.FromHandle(result.Value.DeviceHandle.Value),
                _ => VodkaValue.Null,
            });
        }

        public DwaineSyscallResult Spawn(string path, IReadOnlyList<string> arguments)
        {
            var result = _system.TryStart(
                _mainframe,
                _principal,
                _processId,
                _workingDirectory,
                path,
                arguments,
                false);
            return result.Succeeded
                ? DwaineSyscallResult.Success(DwaineSyscallValue.FromInteger((long) result.ProcessId.Value))
                : DwaineSyscallResult.Failure(MapSpawnStatus(result.Result), result.Error.Trim());
        }

        public DwaineSyscallResult Fork(IReadOnlyList<string> arguments)
        {
            return _machine is null
                ? DwaineSyscallResult.Failure(DwaineSyscallStatus.InvalidCaller, "fork runtime unavailable")
                : _system.TryFork(
                    this,
                    _machine,
                    _mainframe,
                    _principal,
                    _workingDirectory,
                    _vfsWorkingDirectory,
                    _runtime,
                    _processId,
                    _displayPath,
                    arguments);
        }

        private VodkaHostCallResult Stat(string path, Func<DwaineVfsNodeSnapshot, bool> predicate)
        {
            var result = Files.TryStat(_principal, path, _vfsWorkingDirectory, out var snapshot);
            return result switch
            {
                DwaineVfsResult.Success => VodkaHostCallResult.Success(VodkaValue.FromBoolean(predicate(snapshot))),
                DwaineVfsResult.NotFound or DwaineVfsResult.AccessDenied or DwaineVfsResult.BrokenLink =>
                    VodkaHostCallResult.Success(VodkaValue.FromBoolean(false)),
                DwaineVfsResult.InvalidPath or DwaineVfsResult.InvalidName or DwaineVfsResult.RootEscape =>
                    VodkaHostCallResult.Failure(VodkaHostCallStatus.InvalidArguments, "invalid filesystem path"),
                _ => VodkaHostCallResult.Failure(VodkaHostCallStatus.Unavailable, "filesystem unavailable"),
            };
        }

        private VodkaHostCallResult IsExecutable(string path)
        {
            var stat = Files.TryStat(_principal, path, _vfsWorkingDirectory, out var snapshot);
            if (stat is DwaineVfsResult.NotFound or DwaineVfsResult.AccessDenied or DwaineVfsResult.BrokenLink)
                return VodkaHostCallResult.Success(VodkaValue.FromBoolean(false));
            if (stat != DwaineVfsResult.Success)
                return Stat(path, _ => false);
            if (snapshot.Kind != DwaineVfsNodeKind.Program)
                return VodkaHostCallResult.Success(VodkaValue.FromBoolean(false));
            return VodkaHostCallResult.Success(VodkaValue.FromBoolean(
                Files.CheckExecute(_principal, path, _vfsWorkingDirectory) == DwaineVfsResult.Success));
        }

        private VodkaHostCallResult Network(string name, IReadOnlyList<VodkaValue> arguments)
        {
            if (!_processId.IsValid || _machine is null)
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.AccessDenied, "network caller unavailable");
            if (name == "sys.network.address" && arguments.Count == 0)
            {
                var result = _system._network.GetNode(_mainframe, out var node);
                return NetworkValue(result, node.Address.Value);
            }
            if (name == "sys.network.discover"
                && arguments.Count <= 1
                && (arguments.Count == 0 || arguments[0].Kind == VodkaValueKind.String))
            {
                var result = _system._network.Discover(
                    _mainframe,
                    arguments.Count == 1 ? arguments[0].Text : null,
                    out var nodes);
                return NetworkValue(result, string.Join('\n', nodes.Select(node => node.Address.Value)));
            }
            if (name == "sys.network.ping"
                && arguments.Count == 1
                && arguments[0].Kind == VodkaValueKind.String)
            {
                var result = _system._network.TryRequest(
                    _mainframe,
                    arguments[0].Text,
                    "dwaine.ping",
                    string.Empty,
                    out var correlation);
                if (result is not (DwaineNetworkResult.Success or DwaineNetworkResult.Pending))
                    return NetworkFailure(result);
                result = _system._network.TryTakeReply(_mainframe, correlation, out var reply);
                return NetworkValue(result, reply);
            }
            if (name == "sys.network.send"
                && arguments.Count == 3
                && arguments.All(argument => argument.Kind == VodkaValueKind.String))
            {
                var result = _system._communications.TrySend(
                    _mainframe,
                    _principal,
                    arguments[0].Text,
                    arguments[1].Text,
                    arguments[2].Text);
                return result == DwaineNetworkResult.Success
                    ? VodkaHostCallResult.Success(VodkaValue.FromBoolean(true))
                    : NetworkFailure(result);
            }
            if (name == "sys.network.sendfile"
                && arguments.Count == 3
                && arguments.All(argument => argument.Kind == VodkaValueKind.String))
            {
                var result = _system._communications.TrySendFile(
                    _mainframe,
                    _principal,
                    arguments[0].Text,
                    arguments[1].Text,
                    arguments[2].Text,
                    _vfsWorkingDirectory,
                    out var receivedPath);
                return NetworkValue(result, receivedPath);
            }
            if (name == "sys.network.receive" && arguments.Count == 0)
            {
                var result = _system._communications.TryReceive(_mainframe, _principal, out var message);
                if (result == DwaineNetworkResult.NotFound)
                    return VodkaHostCallResult.Success(VodkaValue.FromString(string.Empty));
                return NetworkValue(result, $"{message.SourceAddress}\t{message.Sender}\t{message.Message}");
            }
            return VodkaHostCallResult.Failure(
                VodkaHostCallStatus.InvalidArguments,
                $"invalid network call: {name}");
        }

        private VodkaHostCallResult Service(string name, IReadOnlyList<VodkaValue> arguments)
        {
            if (!_processId.IsValid || _machine is null)
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.AccessDenied, "service caller unavailable");

            DwaineServiceResponse response;
            if (name == "sys.service.list" && arguments.Count == 0)
            {
                response = _system._services.ListServices(_mainframe, _processId, _principal);
            }
            else if (name == "sys.service.call"
                     && arguments.Count >= 2
                     && arguments.All(argument => argument.Kind == VodkaValueKind.String))
            {
                response = _system._services.Call(
                    _mainframe,
                    _processId,
                    _principal,
                    arguments[0].Text,
                    arguments[1].Text,
                    arguments.Skip(2).Select(argument => argument.Text).ToArray(),
                    _vfsWorkingDirectory);
            }
            else
            {
                return VodkaHostCallResult.Failure(
                    VodkaHostCallStatus.InvalidArguments,
                    $"invalid service call: {name}");
            }

            return response.Succeeded
                ? VodkaHostCallResult.Success(VodkaValue.FromString(response.Output))
                : VodkaHostCallResult.Failure(response.Status switch
                {
                    DwaineServiceStatus.InvalidArguments => VodkaHostCallStatus.InvalidArguments,
                    DwaineServiceStatus.AccessDenied => VodkaHostCallStatus.AccessDenied,
                    DwaineServiceStatus.NotFound => VodkaHostCallStatus.NotFound,
                    DwaineServiceStatus.Conflict => VodkaHostCallStatus.Conflict,
                    DwaineServiceStatus.CapacityReached => VodkaHostCallStatus.LimitExceeded,
                    _ => VodkaHostCallStatus.Unavailable,
                }, response.Output.Trim());
        }

        private static VodkaHostCallResult NetworkValue(DwaineNetworkResult result, string value)
            => result == DwaineNetworkResult.Success
                ? VodkaHostCallResult.Success(VodkaValue.FromString(value))
                : NetworkFailure(result);

        private static VodkaHostCallResult NetworkFailure(DwaineNetworkResult result)
            => VodkaHostCallResult.Failure(result switch
            {
                DwaineNetworkResult.InvalidNode or DwaineNetworkResult.InvalidAddress
                    or DwaineNetworkResult.InvalidPayload => VodkaHostCallStatus.InvalidArguments,
                DwaineNetworkResult.NotFound => VodkaHostCallStatus.NotFound,
                DwaineNetworkResult.DuplicateAddress => VodkaHostCallStatus.Conflict,
                DwaineNetworkResult.RateLimited => VodkaHostCallStatus.RateLimited,
                DwaineNetworkResult.PayloadTooLarge or DwaineNetworkResult.CapacityReached => VodkaHostCallStatus.LimitExceeded,
                DwaineNetworkResult.CrossNetwork => VodkaHostCallStatus.AccessDenied,
                DwaineNetworkResult.Disabled or DwaineNetworkResult.AdapterMismatch
                    or DwaineNetworkResult.OutOfRange or DwaineNetworkResult.Interfered
                    or DwaineNetworkResult.Timeout or DwaineNetworkResult.Disconnected => VodkaHostCallStatus.Offline,
                _ => VodkaHostCallStatus.Unavailable,
            }, $"network: {result.ToString().ToLowerInvariant()}");

        private static bool TryMapCall(string name, out DwaineSyscallId syscall)
        {
            syscall = name switch
            {
                "sys.terminal.write" => DwaineSyscallId.MessageTerminal,
                "sys.user.login" => DwaineSyscallId.UserLogin,
                "sys.user.group" => DwaineSyscallId.UserGroup,
                "sys.user.list" => DwaineSyscallId.UserList,
                "sys.user.message" => DwaineSyscallId.UserMessage,
                "sys.device.message" => DwaineSyscallId.DeviceMessage,
                "sys.device.list" => DwaineSyscallId.DeviceList,
                "sys.device.get" or "sys.storage.get" => DwaineSyscallId.DeviceGet,
                "sys.device.scan" => DwaineSyscallId.DeviceScan,
                "sys.process.exit" => DwaineSyscallId.Exit,
                "sys.process.spawn" => DwaineSyscallId.TaskSpawn,
                "sys.process.fork" => DwaineSyscallId.TaskFork,
                "sys.process.kill" => DwaineSyscallId.TaskKill,
                "sys.process.list" => DwaineSyscallId.TaskList,
                "sys.file.read" => DwaineSyscallId.FileGet,
                "sys.file.delete" => DwaineSyscallId.FileKill,
                "sys.file.mode" => DwaineSyscallId.FileMode,
                "sys.file.owner" => DwaineSyscallId.FileOwner,
                "sys.file.write" => DwaineSyscallId.FileWrite,
                "sys.config.read" => DwaineSyscallId.ConfigurationGet,
                "sys.storage.mount" => DwaineSyscallId.Mount,
                _ => 0,
            };
            return syscall != 0;
        }

        private static bool TryConvertArguments(
            string name,
            DwaineSyscallId syscall,
            IReadOnlyList<VodkaValue> arguments,
            out DwaineSyscallValue[] converted,
            out string error)
        {
            error = string.Empty;
            var extraCapability = syscall == DwaineSyscallId.DeviceGet && arguments.Count == 1;
            converted = new DwaineSyscallValue[arguments.Count + (extraCapability ? 1 : 0)];
            for (var index = 0; index < arguments.Count; index++)
            {
                var value = arguments[index];
                converted[index] = value.Kind switch
                {
                    VodkaValueKind.Null => DwaineSyscallValue.Null,
                    VodkaValueKind.Integer => DwaineSyscallValue.FromInteger(value.Integer),
                    VodkaValueKind.Boolean => DwaineSyscallValue.FromBoolean(value.Boolean),
                    VodkaValueKind.String => DwaineSyscallValue.FromString(value.Text),
                    VodkaValueKind.Handle when value.Handle != 0 =>
                        DwaineSyscallValue.FromDeviceHandle(new DwaineDeviceHandle(value.Handle)),
                    _ => default,
                };
                if (value.Kind == VodkaValueKind.Handle && value.Handle == 0)
                {
                    error = $"{name} received an invalid handle";
                    return false;
                }
            }
            if (extraCapability)
            {
                var capabilities = name == "sys.storage.get"
                    ? DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Mount
                    : DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message;
                converted[^1] = DwaineSyscallValue.FromInteger((long) capabilities);
            }
            return true;
        }

        private static VodkaHostCallStatus MapStatus(DwaineSyscallStatus status) => status switch
        {
            DwaineSyscallStatus.InvalidArguments => VodkaHostCallStatus.InvalidArguments,
            DwaineSyscallStatus.AccessDenied or DwaineSyscallStatus.InvalidCaller => VodkaHostCallStatus.AccessDenied,
            DwaineSyscallStatus.NotFound => VodkaHostCallStatus.NotFound,
            DwaineSyscallStatus.Conflict => VodkaHostCallStatus.Conflict,
            DwaineSyscallStatus.RateLimited => VodkaHostCallStatus.RateLimited,
            DwaineSyscallStatus.LimitExceeded => VodkaHostCallStatus.LimitExceeded,
            DwaineSyscallStatus.StaleHandle => VodkaHostCallStatus.StaleHandle,
            DwaineSyscallStatus.Offline => VodkaHostCallStatus.Offline,
            DwaineSyscallStatus.UnknownCall => VodkaHostCallStatus.UnknownFunction,
            _ => VodkaHostCallStatus.Unavailable,
        };

        private static DwaineSyscallStatus MapSpawnStatus(VodkaSpawnResult result) => result switch
        {
            VodkaSpawnResult.AccessDenied => DwaineSyscallStatus.AccessDenied,
            VodkaSpawnResult.InvalidArguments or VodkaSpawnResult.InvalidExtension or VodkaSpawnResult.InvalidPath =>
                DwaineSyscallStatus.InvalidArguments,
            VodkaSpawnResult.FileUnavailable => DwaineSyscallStatus.NotFound,
            VodkaSpawnResult.ProcessRejected => DwaineSyscallStatus.ProcessFailure,
            _ => DwaineSyscallStatus.MainframeUnavailable,
        };
    }
}
