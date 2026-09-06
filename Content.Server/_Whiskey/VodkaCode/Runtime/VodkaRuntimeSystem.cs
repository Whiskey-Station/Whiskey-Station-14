// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
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
    [Dependency] private DwaineProcessSystem _processes = default!;
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
        var host = new DwaineVodkaHost(authorized, principal, cwd, _timing);
        var machine = new VodkaVirtualMachine(compilation.Program!, limits, host, arguments, seed);
        var displayPath = path.Length <= 72 ? path : path[^72..];
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

    private sealed class DwaineVodkaHost(
        DwaineAuthorizedFileSystem files,
        DwainePrincipalId principal,
        DwaineVfsNodeHandle workingDirectory,
        IGameTiming timing) : IVodkaRuntimeHost
    {
        public TimeSpan Now => timing.CurTime;

        public VodkaHostCallResult Invoke(string name, IReadOnlyList<VodkaValue> arguments)
        {
            if (name is not ("fs.exists" or "fs.is_directory" or "fs.is_file" or "fs.is_executable"))
                return VodkaHostCallResult.Failure(VodkaHostCallStatus.UnknownFunction, $"unknown function: {name}");
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

        private VodkaHostCallResult Stat(string path, Func<DwaineVfsNodeSnapshot, bool> predicate)
        {
            var result = files.TryStat(principal, path, workingDirectory, out var snapshot);
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
            var stat = files.TryStat(principal, path, workingDirectory, out var snapshot);
            if (stat is DwaineVfsResult.NotFound or DwaineVfsResult.AccessDenied or DwaineVfsResult.BrokenLink)
                return VodkaHostCallResult.Success(VodkaValue.FromBoolean(false));
            if (stat != DwaineVfsResult.Success)
                return Stat(path, _ => false);
            if (snapshot.Kind != DwaineVfsNodeKind.Program)
                return VodkaHostCallResult.Success(VodkaValue.FromBoolean(false));
            return VodkaHostCallResult.Success(VodkaValue.FromBoolean(
                files.CheckExecute(principal, path, workingDirectory) == DwaineVfsResult.Success));
        }
    }
}
