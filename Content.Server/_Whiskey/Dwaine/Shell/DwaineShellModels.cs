// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Shell;

public readonly record struct DwaineShellLimits(
    int MaxInputLength,
    int MaxTokens,
    int MaxPipelineStages,
    int MaxCommands,
    int MaxHistoryEntries,
    int MaxEnvironmentEntries,
    int MaxEnvironmentCharacters,
    int MaxOutputCharacters,
    int MaxEvaluationDepth,
    int MaxLoopIterations)
{
    public static DwaineShellLimits FromComponent(DwaineShellComponent component)
    {
        return new DwaineShellLimits(
            Math.Clamp(component.MaxInputLength, 1, DwaineShellComponent.HardMaxInputLength),
            Math.Clamp(component.MaxTokens, 1, DwaineShellComponent.HardMaxTokens),
            Math.Clamp(component.MaxPipelineStages, 1, DwaineShellComponent.HardMaxPipelineStages),
            Math.Clamp(component.MaxCommands, 1, DwaineShellComponent.HardMaxCommands),
            Math.Clamp(component.MaxHistoryEntries, 1, DwaineShellComponent.HardMaxHistoryEntries),
            Math.Clamp(
                component.MaxEnvironmentEntries,
                DwaineShellComponent.MinimumEnvironmentEntries,
                DwaineShellComponent.HardMaxEnvironmentEntries),
            Math.Clamp(
                component.MaxEnvironmentCharacters,
                DwaineShellComponent.MinimumEnvironmentCharacters,
                DwaineShellComponent.HardMaxEnvironmentCharacters),
            Math.Clamp(component.MaxOutputCharacters, 1, DwaineShellComponent.HardMaxOutputCharacters),
            Math.Clamp(component.MaxEvaluationDepth, 1, DwaineShellComponent.HardMaxEvaluationDepth),
            Math.Clamp(component.MaxLoopIterations, 1, DwaineShellComponent.HardMaxLoopIterations));
    }
}

public sealed class DwaineShellSession(DwaineShellLimits limits)
{
    private static readonly HashSet<string> ProtectedVariables = new(StringComparer.Ordinal)
    {
        "HOME",
        "PATH",
        "USER",
    };

    private readonly Queue<string> _history = new();
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public DwaineVfsNodeHandle WorkingDirectory = DwaineVfsNodeHandle.Root;
    public DwaineProcessId? ProcessId;
    public DwaineProcessOwner ProcessOwner;
    public int LastExitCode;
    public bool MessagesEnabled = true;
    public TimeSpan SleepingUntil;
    public int LoopDepth;
    public bool BreakRequested;
    public bool PromptPending;
    public string[] PendingRemovalPaths = [];
    public bool PendingRemovalRecursive;
    public bool PendingRemovalForce;
    public TimeSpan PendingRemovalUntil;
    public int FailedAuthenticationAttempts;
    public TimeSpan NextAuthenticationAt;

    public IReadOnlyDictionary<string, string> Environment => _environment;

    public void InitializeEnvironment(string user, string home)
    {
        _environment["USER"] = user;
        _environment["HOME"] = home;
        _environment["PATH"] = "/bin:/usr/bin:.";
        foreach (var name in _environment.Keys
                     .Where(name => !ProtectedVariables.Contains(name))
                     .OrderByDescending(name => name, StringComparer.Ordinal)
                     .ToArray())
        {
            if (EnvironmentCharacterCount() <= limits.MaxEnvironmentCharacters)
                break;
            _environment.Remove(name);
        }
    }

    public bool TrySetEnvironment(string name, string value)
    {
        if (!IsValidVariableName(name) || value.Length > limits.MaxInputLength)
            return false;
        if (!_environment.ContainsKey(name) && _environment.Count >= limits.MaxEnvironmentEntries)
            return false;
        var previousCharacters = _environment.TryGetValue(name, out var previous)
            ? name.Length + previous.Length
            : 0;
        var nextCharacters = EnvironmentCharacterCount()
                             - previousCharacters
                             + name.Length
                             + value.Length;
        if (nextCharacters > limits.MaxEnvironmentCharacters)
            return false;
        _environment[name] = value;
        return true;
    }

    public bool TryUnsetEnvironment(string name)
    {
        return !ProtectedVariables.Contains(name) && _environment.Remove(name);
    }

    public bool TryGetEnvironment(string name, out string value)
    {
        if (name == "STATUS")
        {
            value = LastExitCode.ToString();
            return true;
        }

        return _environment.TryGetValue(name, out value!);
    }

    private int EnvironmentCharacterCount()
    {
        return _environment.Sum(pair => pair.Key.Length + pair.Value.Length);
    }

    public void AddHistory(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        _history.Enqueue(line);
        while (_history.Count > limits.MaxHistoryEntries)
            _history.Dequeue();
    }

    public string[] GetHistory()
    {
        return _history.ToArray();
    }

    public void ClearHistory()
    {
        _history.Clear();
    }

    private static bool IsValidVariableName(string name)
    {
        return !string.IsNullOrEmpty(name)
               && (char.IsAsciiLetter(name[0]) || name[0] == '_')
               && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}

public readonly record struct DwaineShellUserEntry(string Name, bool Temporary, bool MessagesEnabled);

public readonly record struct DwaineShellHostResult(int ExitCode, string Output)
{
    public static DwaineShellHostResult Success(string output = "") => new(0, output);
    public static DwaineShellHostResult Failure(string output) => new(1, output);
}

public interface IDwaineShellHost
{
    TimeSpan Now { get; }
    DwaineIdentitySessionSnapshot Identity { get; }
    DwaineIdentityStore Identities { get; }
    DwaineAuthorizedFileSystem Files { get; }

    DwaineVfsResult TryGetPath(DwaineVfsNodeHandle handle, out string path);
    DwaineVfsResult TryCanonicalize(string path, DwaineVfsNodeHandle workingDirectory, out string canonical);
    DwaineIdentityResult TryElevate(string name, string password, out DwaineIdentitySessionSnapshot session);
    DwaineIdentityResult TryLogout(out DwaineIdentitySessionSnapshot session);
    IReadOnlyList<DwaineShellUserEntry> GetUsers();
    DwaineShellHostResult Talk(string target, string message);
    DwaineShellHostResult Mount(string label, string path);
    DwaineShellHostResult Unmount(string label);
    DwaineShellHostResult ListMedia();
    void ClearScreen();
}

public readonly record struct DwaineShellProgramStartResult(
    bool Succeeded,
    DwaineProcessId ProcessId,
    string Error);

public readonly record struct DwaineShellProgramOutput(
    string StandardOutput,
    string StandardError,
    int ExitCode,
    string ErrorCode);

/// <summary>
/// Optional process-backed language host. Keeping this separate preserves the pure shell host contract
/// while ensuring script execution can only occur through the authoritative process runtime.
/// </summary>
public interface IDwaineVodkaShellHost
{
    DwaineShellProgramStartResult TryStartVodka(
        DwaineProcessId parent,
        DwaineVfsNodeHandle workingDirectory,
        string path,
        IReadOnlyList<string> arguments);

    bool TryTakeVodkaOutput(DwaineProcessId processId, out DwaineShellProgramOutput output);
}

/// <summary>
/// Optional server-owned network host used by the shell without exposing topology entities.
/// </summary>
public interface IDwaineNetworkShellHost
{
    DwaineShellHostResult Network(IReadOnlyList<string> arguments, DwaineVfsNodeHandle workingDirectory);
    DwaineShellHostResult Scan(DwaineProcessId process);
}

/// <summary>
/// Optional server-owned station-service façade. The caller process and authenticated principal
/// are revalidated by the service subsystem for every operation.
/// </summary>
public interface IDwaineServiceShellHost
{
    DwaineShellHostResult Service(
        DwaineProcessId process,
        DwaineVfsNodeHandle workingDirectory,
        IReadOnlyList<string> arguments);
}

public readonly record struct DwaineShellExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool ClearScreen,
    bool TerminateProcess,
    int InstructionsConsumed,
    DwaineProcessId? WaitFor = null)
{
    public static DwaineShellExecutionResult Error(string error, int instructions = 1)
    {
        return new DwaineShellExecutionResult(1, string.Empty, error, false, false, instructions);
    }
}
