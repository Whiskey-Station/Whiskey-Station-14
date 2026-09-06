// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Content.Server._Whiskey.Dwaine.Shell;

/// <summary>
/// Deterministic command engine over server-owned session state and capability-narrow host services.
/// </summary>
public sealed class DwaineShellEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
    private readonly DwaineShellLimits _limits;
    private readonly DwaineShellParser _parser;
    private readonly Dictionary<string, CommandHandler> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _manual = new(StringComparer.OrdinalIgnoreCase);
    private int _executionDepth;
    private int _remainingInstructionBudget;
    private bool _credentialCommandObserved;

    private delegate CommandResult CommandHandler(
        DwaineShellSession session,
        IDwaineShellHost host,
        IReadOnlyList<string> arguments,
        string standardInput,
        int depth);

    private readonly record struct CommandResult(
        int ExitCode,
        string Output = "",
        string Error = "",
        bool ClearScreen = false,
        bool TerminateProcess = false,
        int Instructions = 1,
        DwaineProcessId? WaitFor = null);

    public DwaineShellEngine(DwaineShellLimits limits)
    {
        _limits = limits;
        _parser = new DwaineShellParser(limits);
        RegisterCommands();
    }

    public IReadOnlyList<string> CommandNames => _commands.Keys.Order(StringComparer.Ordinal).ToArray();

    public DwaineShellExecutionResult Execute(
        string source,
        DwaineShellSession session,
        IDwaineShellHost host,
        int depth = 0,
        bool recordHistory = true)
    {
        var rootExecution = _executionDepth == 0;
        if (rootExecution)
        {
            _remainingInstructionBudget = _limits.MaxCommands * 128;
            _credentialCommandObserved = false;
        }
        _executionDepth++;
        try
        {
            return ExecuteCore(source, session, host, depth, recordHistory);
        }
        finally
        {
            _executionDepth--;
            if (rootExecution)
            {
                _remainingInstructionBudget = 0;
                _credentialCommandObserved = false;
            }
        }
    }

    private DwaineShellExecutionResult ExecuteCore(
        string source,
        DwaineShellSession session,
        IDwaineShellHost host,
        int depth,
        bool recordHistory)
    {
        if (depth > _limits.MaxEvaluationDepth)
            return DwaineShellExecutionResult.Error("shell: evaluation depth exceeded");
        if (!TryChargeInstructions(1))
            return BudgetExceeded();
        if (host.Now < session.SleepingUntil)
            return DwaineShellExecutionResult.Error("sleep: process is waiting on the logical clock");

        var parsed = _parser.Parse(source);
        if (!parsed.Succeeded)
            return DwaineShellExecutionResult.Error(parsed.Diagnostic?.ToString() ?? "shell: parse error");
        if (parsed.Line!.Pipelines.Count == 0)
        {
            if (recordHistory)
                session.AddHistory(source);
            return new DwaineShellExecutionResult(0, string.Empty, string.Empty, false, false, 1);
        }

        var standardOutput = new BoundedOutput(_limits.MaxOutputCharacters);
        var standardError = new BoundedOutput(_limits.MaxOutputCharacters);
        var exitCode = session.LastExitCode;
        var clear = false;
        var terminate = false;
        DwaineProcessId? waitFor = null;
        var instructions = 1;

        foreach (var pipeline in parsed.Line.Pipelines)
        {
            if (pipeline.Condition == DwaineShellChainCondition.OnSuccess && exitCode != 0
                || pipeline.Condition == DwaineShellChainCondition.OnFailure && exitCode == 0)
            {
                continue;
            }

            var pipelineInput = string.Empty;
            for (var commandIndex = 0; commandIndex < pipeline.Commands.Count; commandIndex++)
            {
                var command = pipeline.Commands[commandIndex];
                if (!TryExpandWords(command.Words, session, host, depth, out var words, out var expansionError))
                {
                    exitCode = 1;
                    standardError.AppendLine(expansionError);
                    break;
                }
                if (words.Count > 0
                    && (string.Equals(words[0], "su", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(words[0], "eval", StringComparison.OrdinalIgnoreCase)))
                {
                    _credentialCommandObserved = true;
                }

                var inputRedirect = command.Redirections.FirstOrDefault(redirection =>
                    redirection.Kind == DwaineShellRedirectionKind.Input);
                if (inputRedirect.Target is not null)
                {
                    if (!TryExpandWord(inputRedirect.Target, session, host, depth, out var inputPath, out expansionError))
                    {
                        exitCode = 1;
                        standardError.AppendLine(expansionError);
                        break;
                    }
                    var read = host.Files.TryReadText(
                        host.Identity.Principal,
                        inputPath,
                        session.WorkingDirectory,
                        out pipelineInput);
                    if (read != DwaineVfsResult.Success)
                    {
                        exitCode = 1;
                        standardError.AppendLine(FileError("redirect", inputPath, read));
                        break;
                    }
                }

                if (words.Count > 0
                    && string.Equals(words[0], "vodka", StringComparison.OrdinalIgnoreCase)
                    && (depth != 0
                        || parsed.Line.Pipelines.Count != 1
                        || pipeline.Commands.Count != 1
                        || command.Redirections.Count != 0))
                {
                    exitCode = 2;
                    standardError.AppendLine("vodka: process-backed scripts must be a standalone command at the top level");
                    break;
                }

                var result = ExecuteCommand(words, pipelineInput, session, host, depth);
                var consumed = result.Instructions + words.Count;
                instructions += consumed;
                if (!TryChargeInstructions(consumed))
                {
                    exitCode = 1;
                    standardError.AppendLine("shell: command budget exceeded");
                    terminate = true;
                    break;
                }

                exitCode = result.ExitCode;
                session.LastExitCode = exitCode;
                clear |= result.ClearScreen;
                if (result.ClearScreen)
                    host.ClearScreen();
                terminate |= result.TerminateProcess;
                waitFor = result.WaitFor;
                if (!string.IsNullOrEmpty(result.Error))
                    standardError.Append(result.Error);

                var outputRedirect = command.Redirections.FirstOrDefault(redirection =>
                    redirection.Kind != DwaineShellRedirectionKind.Input);
                if (outputRedirect.Target is not null)
                {
                    if (!TryExpandWord(outputRedirect.Target, session, host, depth, out var outputPath, out expansionError))
                    {
                        exitCode = 1;
                        standardError.AppendLine(expansionError);
                        break;
                    }
                    var write = WriteRedirect(
                        host,
                        session,
                        outputPath,
                        result.Output,
                        outputRedirect.Kind == DwaineShellRedirectionKind.Append);
                    if (write != DwaineVfsResult.Success)
                    {
                        exitCode = 1;
                        standardError.AppendLine(FileError("redirect", outputPath, write));
                    }
                    pipelineInput = string.Empty;
                }
                else
                {
                    pipelineInput = result.Output;
                }

                if (terminate || waitFor is not null)
                    break;
            }

            if (!string.IsNullOrEmpty(pipelineInput))
                standardOutput.Append(pipelineInput);
            if (terminate || waitFor is not null)
                break;
        }

        if (standardOutput.Truncated || standardError.Truncated)
        {
            exitCode = 1;
            if (!standardError.Truncated)
                standardError.AppendLine("shell: output limit exceeded");
        }
        session.LastExitCode = exitCode;
        if (recordHistory)
            session.AddHistory(RedactHistory(source, parsed.Line, _credentialCommandObserved));
        return new DwaineShellExecutionResult(
            exitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            clear,
            terminate,
            instructions,
            waitFor);
    }

    private CommandResult ExecuteCommand(
        IReadOnlyList<string> words,
        string standardInput,
        DwaineShellSession session,
        IDwaineShellHost host,
        int depth)
    {
        if (words.Count == 0 || string.IsNullOrWhiteSpace(words[0]))
            return new CommandResult(0);
        if (_commands.TryGetValue(words[0], out var handler))
            return handler(session, host, words.Skip(1).ToArray(), standardInput, depth);

        var resolution = ResolveProgram(words[0], session, host, out var resolved);
        return resolution switch
        {
            DwaineVfsResult.Success => new CommandResult(
                126,
                Error: $"shell: program runtime unavailable for {resolved}\n"),
            DwaineVfsResult.AccessDenied => new CommandResult(
                126,
                Error: $"shell: permission denied: {words[0]}\n"),
            _ => new CommandResult(127, Error: $"shell: command not found: {words[0]}\n"),
        };
    }

    private DwaineVfsResult ResolveProgram(
        string name,
        DwaineShellSession session,
        IDwaineShellHost host,
        out string resolved)
    {
        resolved = name;
        if (name.Contains('/'))
            return host.Files.CheckExecute(host.Identity.Principal, name, session.WorkingDirectory);
        if (!session.TryGetEnvironment("PATH", out var path))
            return DwaineVfsResult.NotFound;

        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = $"{directory.TrimEnd('/')}/{name}";
            var result = host.Files.CheckExecute(host.Identity.Principal, candidate, session.WorkingDirectory);
            if (result == DwaineVfsResult.NotFound)
                continue;
            resolved = candidate;
            return result;
        }

        return DwaineVfsResult.NotFound;
    }

    private bool TryExpandWords(
        IReadOnlyList<DwaineShellWord> words,
        DwaineShellSession session,
        IDwaineShellHost host,
        int depth,
        out IReadOnlyList<string> expanded,
        out string error)
    {
        var result = new List<string>(words.Count);
        foreach (var word in words)
        {
            if (!TryExpandWord(word, session, host, depth, out var value, out error))
            {
                expanded = [];
                return false;
            }
            result.Add(value);
        }

        expanded = result;
        error = string.Empty;
        return true;
    }

    private bool TryExpandWord(
        DwaineShellWord word,
        DwaineShellSession session,
        IDwaineShellHost host,
        int depth,
        out string expanded,
        out string error)
    {
        var output = new StringBuilder();
        foreach (var segment in word.Segments)
        {
            if (!segment.Expand)
            {
                output.Append(segment.Text);
                continue;
            }

            for (var index = 0; index < segment.Text.Length; index++)
            {
                if (segment.Text[index] != '$')
                {
                    output.Append(segment.Text[index]);
                    continue;
                }

                if (index + 1 < segment.Text.Length && segment.Text[index + 1] == '(')
                {
                    if (!TryFindSubstitution(segment.Text, index, out var end))
                    {
                        expanded = string.Empty;
                        error = "shell: malformed command substitution";
                        return false;
                    }
                    var inner = segment.Text[(index + 2)..end];
                    var result = Execute(inner, session, host, depth + 1, false);
                    if (result.ExitCode != 0)
                    {
                        expanded = string.Empty;
                        error = string.IsNullOrEmpty(result.StandardError)
                            ? "shell: command substitution failed"
                            : result.StandardError.TrimEnd();
                        return false;
                    }
                    output.Append(result.StandardOutput.TrimEnd('\r', '\n'));
                    index = end;
                    continue;
                }

                var nameStart = index + 1;
                var nameEnd = nameStart;
                while (nameEnd < segment.Text.Length
                       && (char.IsAsciiLetterOrDigit(segment.Text[nameEnd]) || segment.Text[nameEnd] == '_'))
                {
                    nameEnd++;
                }
                if (nameEnd == nameStart)
                {
                    output.Append('$');
                    continue;
                }

                var name = segment.Text[nameStart..nameEnd];
                if (session.TryGetEnvironment(name, out var value))
                    output.Append(value);
                index = nameEnd - 1;
            }
        }

        if (output.Length > _limits.MaxInputLength)
        {
            expanded = string.Empty;
            error = "shell: expansion limit exceeded";
            return false;
        }

        expanded = output.ToString();
        error = string.Empty;
        return true;
    }

    private static bool TryFindSubstitution(string text, int start, out int end)
    {
        var depth = 1;
        var quote = '\0';
        for (var index = start + 2; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }
            if (quote != '\0')
            {
                if (text[index] == quote)
                    quote = '\0';
                continue;
            }
            if (text[index] is '\'' or '"')
            {
                quote = text[index];
                continue;
            }
            if (text[index] == '(')
                depth++;
            else if (text[index] == ')' && --depth == 0)
            {
                end = index;
                return true;
            }
        }

        end = -1;
        return false;
    }

    private DwaineVfsResult WriteRedirect(
        IDwaineShellHost host,
        DwaineShellSession session,
        string path,
        string output,
        bool append)
    {
        var write = host.Files.TryWriteText(
            host.Identity.Principal,
            path,
            session.WorkingDirectory,
            output,
            append,
            host.Now);
        if (write != DwaineVfsResult.NotFound)
            return write;
        return host.Files.TryCreateText(
            host.Identity.Principal,
            path,
            session.WorkingDirectory,
            output,
            null,
            host.Now);
    }

    private void RegisterCommands()
    {
        Register("echo", "echo [-n] [text...]: write bounded text to stdout", Echo);
        Register("clear", "clear: clear this terminal output buffer", Clear);
        Alias("cls", "clear");
        Register("history", "history [-c]: list or clear bounded session history", History);
        Register("help", "help [command]: list commands or show exact command help", Help);
        Alias("man", "help");
        Register("logout", "logout: replace this login with a temporary session", Logout);
        Alias("logoff", "logout");
        Register("pwd", "pwd: print the canonical working directory", Pwd);
        Register("cd", "cd [path]: change to an executable directory", Cd);
        Register("cat", "cat [file...]: concatenate readable files or stdin", Cat);
        Register("ls", "ls [-l] [path]: list a readable directory", Ls);
        Register("mkdir", "mkdir [-p] path...: create owned directories", Mkdir);
        Register("chmod", "chmod MODE path: set validated octal owner/group/other mode", Chmod);
        Register("chown", "chown USER[:GROUP] path: operator-only ownership change", Chown);
        Register("cp", "cp source destination: permission-aware VFS copy", Copy);
        Register("mv", "mv source destination: atomic same-volume move", Move);
        Register("rm", "rm [-r] [-f] [-i] path... | rm --confirm: remove entries; interactive confirmations expire", Remove);
        Register("ln", "ln target link: create an owned symbolic link", Link);
        Register("date", "date: print deterministic mainframe logical time", Date);
        Register("grep", "grep [-i] [-E] [-r] pattern [file]: bounded text/record or timed-regex search", Grep);
        Register("getopt", "getopt OPTSPEC arguments...: parse bounded short options", Getopt);
        Register("tar", "tar -c archive source | -t archive | -x archive directory", Tar);
        Register("mount", "mount [LABEL PATH|-u LABEL]: list or manage inserted media", Mount);
        Register("su", "su USER PASSWORD: reauthenticate this session; history redacts the password", Su);
        Register("set", "set [NAME=VALUE]: list or update the bounded session environment", Set);
        Register("unset", "unset NAME...: remove non-protected environment variables", Unset);
        Register("mesg", "mesg [y|n]: inspect or set direct-message acceptance", Mesg);
        Register("talk", "talk USER message...: message a consenting user on this mainframe", Talk);
        Register("who", "who: list public active users without privileged session details", Who);
        Register("net", "net address|status|discover [TAG]|ping ADDRESS|send ADDRESS USER MESSAGE...|sendfile ADDRESS USER FILE|inbox|metrics|capture", Net);
        Register("scnt", "scnt: bounded network discovery and local Device ABI rescan", Scan);
        Register("sleep", "sleep SECONDS: wait on bounded logical game time", Sleep);
        Register("eval", "eval shell-text...: evaluate only this bounded shell grammar", Eval);
        Register("if", "if LEFT OP RIGHT: comparison status; OP is =, !=, -eq, -ne, -lt, -le, -gt or -ge", If);
        Register("else", "else: succeed only when the previous command status failed", Else);
        Register("while", "while COUNT command...: repeat a command with a hard iteration cap", While);
        Register("break", "break: stop the nearest bounded shell while", Break);
        Register("whiskeysay", "whiskeysay text...: Whiskey terminal novelty output", WhiskeySay);
        Register("vodka", "vodka FILE.vodka [argument...]: run a Vodka Code script as a bounded child process", Vodka);
    }

    private void Register(string name, string manual, CommandHandler handler)
    {
        _commands.Add(name, handler);
        _manual.Add(name, manual);
    }

    private void Alias(string alias, string target)
    {
        _commands.Add(alias, _commands[target]);
        _manual.Add(alias, $"{alias}: alias of {target}");
    }

    private static CommandResult Echo(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        var newline = args.Count == 0 || args[0] != "-n";
        var start = newline || args.Count == 0 ? 0 : 1;
        var output = string.Join(' ', args.Skip(start));
        return new CommandResult(0, output + (newline ? "\n" : string.Empty));
    }

    private static CommandResult Clear(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        return args.Count == 0
            ? new CommandResult(0, ClearScreen: true)
            : Usage("clear");
    }

    private static CommandResult History(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 1 && args[0] == "-c")
        {
            session.ClearHistory();
            return new CommandResult(0);
        }
        if (args.Count != 0)
            return Usage("history [-c]");
        var history = session.GetHistory();
        return new CommandResult(0, string.Join('\n', history.Select((line, index) => $"{index + 1,4}  {line}")) + (history.Length > 0 ? "\n" : string.Empty));
    }

    private CommandResult Help(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count > 1)
            return Usage("help [command]");
        if (args.Count == 1)
            return _manual.TryGetValue(args[0], out var manual)
                ? new CommandResult(0, manual + "\n")
                : new CommandResult(1, Error: $"help: no manual entry for {args[0]}\n");
        return new CommandResult(0, "commands: " + string.Join(' ', CommandNames) + "\n");
    }

    private static CommandResult Logout(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("logout");
        var result = host.TryLogout(out _);
        return result == DwaineIdentityResult.Success
            ? new CommandResult(0, "logged out\n", TerminateProcess: true)
            : new CommandResult(1, Error: $"logout: {IdentityError(result)}\n");
    }

    private static CommandResult Pwd(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("pwd");
        var result = host.TryGetPath(session.WorkingDirectory, out var path);
        return result == DwaineVfsResult.Success
            ? new CommandResult(0, path + "\n")
            : new CommandResult(1, Error: FileError("pwd", string.Empty, result) + "\n");
    }

    private static CommandResult Cd(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count > 1)
            return Usage("cd [path]");
        var path = args.Count == 1 ? args[0] : session.TryGetEnvironment("HOME", out var home) ? home : "/home";
        var result = host.Files.TryResolveDirectory(host.Identity.Principal, path, session.WorkingDirectory, out var directory);
        if (result != DwaineVfsResult.Success)
            return new CommandResult(1, Error: FileError("cd", path, result) + "\n");
        session.WorkingDirectory = directory;
        return new CommandResult(0);
    }

    private static CommandResult Cat(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 0)
            return new CommandResult(0, stdin);
        var output = new StringBuilder();
        foreach (var path in args)
        {
            var result = host.Files.TryReadText(host.Identity.Principal, path, session.WorkingDirectory, out var text);
            if (result != DwaineVfsResult.Success)
                return new CommandResult(1, output.ToString(), FileError("cat", path, result) + "\n");
            output.Append(text);
        }
        return new CommandResult(0, output.ToString(), Instructions: args.Count);
    }

    private static CommandResult Ls(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        var longFormat = args.Count > 0 && args[0] == "-l";
        var pathIndex = longFormat ? 1 : 0;
        if (args.Count > pathIndex + 1)
            return Usage("ls [-l] [path]");
        var path = args.Count == pathIndex ? "." : args[pathIndex];
        var result = host.Files.TryList(host.Identity.Principal, path, session.WorkingDirectory, out var entries);
        if (result != DwaineVfsResult.Success)
            return new CommandResult(1, Error: FileError("ls", path, result) + "\n");
        var lines = longFormat
            ? entries.Select(entry => $"{FormatMode(entry.Metadata.Mode)} {entry.Metadata.Owner}:{entry.Metadata.Group} {entry.Size,6} {entry.Name}")
            : entries.Select(entry => entry.Name);
        return new CommandResult(0, string.Join('\n', lines) + (entries.Length > 0 ? "\n" : string.Empty), Instructions: entries.Length + 1);
    }

    private static CommandResult Mkdir(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        var parents = args.Count > 0 && args[0] == "-p";
        var paths = args.Skip(parents ? 1 : 0).ToArray();
        if (paths.Length == 0 || paths.Length > 32)
            return Usage("mkdir [-p] path...");
        foreach (var path in paths)
        {
            var result = parents
                ? CreateParents(session, host, path)
                : host.Files.TryCreateDirectory(host.Identity.Principal, path, session.WorkingDirectory, host.Now, out _);
            if (result != DwaineVfsResult.Success && !(parents && result == DwaineVfsResult.AlreadyExists))
                return new CommandResult(1, Error: FileError("mkdir", path, result) + "\n");
        }
        return new CommandResult(0, Instructions: paths.Length);
    }

    private static DwaineVfsResult CreateParents(DwaineShellSession session, IDwaineShellHost host, string path)
    {
        var canonical = host.TryCanonicalize(path, session.WorkingDirectory, out var canonicalPath);
        if (canonical != DwaineVfsResult.Success)
            return canonical;
        var current = string.Empty;
        foreach (var segment in canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += $"/{segment}";
            var resolve = host.Files.TryResolveDirectory(host.Identity.Principal, current, DwaineVfsNodeHandle.Root, out _);
            if (resolve == DwaineVfsResult.Success)
                continue;
            if (resolve != DwaineVfsResult.NotFound)
                return resolve;
            var create = host.Files.TryCreateDirectory(host.Identity.Principal, current, DwaineVfsNodeHandle.Root, host.Now, out _);
            if (create != DwaineVfsResult.Success)
                return create;
        }
        return DwaineVfsResult.Success;
    }

    private static CommandResult Chmod(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2 || !TryParseMode(args[0], out var mode))
            return Usage("chmod MODE path");
        var result = host.Files.TryChangeMode(host.Identity.Principal, args[1], session.WorkingDirectory, mode, host.Now);
        return result == DwaineVfsResult.Success
            ? new CommandResult(0)
            : new CommandResult(1, Error: FileError("chmod", args[1], result) + "\n");
    }

    private static CommandResult Chown(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2)
            return Usage("chown USER[:GROUP] path");
        var spec = args[0].Split(':', 2);
        if (!host.Identities.TryGetAccount(spec[0], out var account))
            return new CommandResult(1, Error: $"chown: unknown user: {spec[0]}\n");
        DwaineGroupId? group = null;
        if (spec.Length == 2)
        {
            if (!host.Identities.TryGetGroup(spec[1], out var selectedGroup))
                return new CommandResult(1, Error: $"chown: unknown group: {spec[1]}\n");
            group = selectedGroup;
        }
        var result = host.Files.TryChangeOwner(host.Identity.Principal, args[1], session.WorkingDirectory, account.Principal, group, host.Now);
        return result == DwaineVfsResult.Success
            ? new CommandResult(0)
            : new CommandResult(1, Error: FileError("chown", args[1], result) + "\n");
    }

    private static CommandResult Copy(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2)
            return Usage("cp source destination");
        var result = host.Files.TryCopy(host.Identity.Principal, args[0], args[1], session.WorkingDirectory, host.Now);
        return result == DwaineVfsResult.Success ? new CommandResult(0) : new CommandResult(1, Error: FileError("cp", args[0], result) + "\n");
    }

    private static CommandResult Move(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2)
            return Usage("mv source destination");
        var result = host.Files.TryMove(host.Identity.Principal, args[0], args[1], session.WorkingDirectory, host.Now);
        return result == DwaineVfsResult.Success ? new CommandResult(0) : new CommandResult(1, Error: FileError("mv", args[0], result) + "\n");
    }

    private static CommandResult Remove(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 1 && args[0] == "--confirm")
        {
            if (session.PendingRemovalPaths.Length == 0 || host.Now > session.PendingRemovalUntil)
            {
                ClearPendingRemoval(session);
                return new CommandResult(1, Error: "rm: no pending removal confirmation\n");
            }

            var pending = session.PendingRemovalPaths;
            var recursivePending = session.PendingRemovalRecursive;
            var forcePending = session.PendingRemovalForce;
            ClearPendingRemoval(session);
            return RemovePaths(session, host, pending, recursivePending, forcePending);
        }

        if (args.Any(argument => argument.StartsWith('-')
                                 && argument is not ("-r" or "-R" or "-f" or "-i")))
        {
            return Usage("rm [-r] [-f] [-i] path... | rm --confirm");
        }
        var recursive = args.Contains("-r") || args.Contains("-R");
        var force = args.Contains("-f");
        var interactive = args.Contains("-i");
        var paths = args.Where(argument => !argument.StartsWith('-')).ToArray();
        if (paths.Length == 0 || paths.Length > 32)
            return Usage("rm [-r] [-f] [-i] path... | rm --confirm");

        if (interactive)
        {
            var canonical = new string[paths.Length];
            for (var index = 0; index < paths.Length; index++)
            {
                var result = host.TryCanonicalize(paths[index], session.WorkingDirectory, out canonical[index]);
                if (result != DwaineVfsResult.Success)
                    return new CommandResult(1, Error: FileError("rm", paths[index], result) + "\n");
            }
            session.PendingRemovalPaths = canonical;
            session.PendingRemovalRecursive = recursive;
            session.PendingRemovalForce = force;
            session.PendingRemovalUntil = host.Now + TimeSpan.FromSeconds(30);
            return new CommandResult(0, $"rm: confirm removal of {paths.Length} entr{(paths.Length == 1 ? "y" : "ies")} with: rm --confirm\n");
        }

        return RemovePaths(session, host, paths, recursive, force);
    }

    private static CommandResult RemovePaths(
        DwaineShellSession session,
        IDwaineShellHost host,
        IReadOnlyList<string> paths,
        bool recursive,
        bool force)
    {
        foreach (var path in paths)
        {
            var result = host.Files.TryDelete(host.Identity.Principal, path, session.WorkingDirectory, recursive, host.Now);
            if (result == DwaineVfsResult.NotFound && force)
                continue;
            if (result != DwaineVfsResult.Success)
                return new CommandResult(1, Error: FileError("rm", path, result) + "\n");
        }
        return new CommandResult(0, Instructions: paths.Count);
    }

    private static void ClearPendingRemoval(DwaineShellSession session)
    {
        session.PendingRemovalPaths = [];
        session.PendingRemovalRecursive = false;
        session.PendingRemovalForce = false;
        session.PendingRemovalUntil = TimeSpan.Zero;
    }

    private static CommandResult Link(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2)
            return Usage("ln target link");
        var result = host.Files.TryCreateLink(host.Identity.Principal, args[1], args[0], session.WorkingDirectory, host.Now);
        return result == DwaineVfsResult.Success ? new CommandResult(0) : new CommandResult(1, Error: FileError("ln", args[1], result) + "\n");
    }

    private static CommandResult Date(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("date");
        var time = host.Now;
        return new CommandResult(0, $"T+{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}\n");
    }

    private static CommandResult Grep(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        var insensitive = false;
        var regex = false;
        var recursive = false;
        var offset = 0;
        while (offset < args.Count && args[offset].StartsWith('-'))
        {
            switch (args[offset])
            {
                case "-i":
                    insensitive = true;
                    break;
                case "-E":
                    regex = true;
                    break;
                case "-r":
                case "-R":
                    recursive = true;
                    break;
                default:
                    return Usage("grep [-i] [-E] [-r] pattern [file]");
            }
            offset++;
        }
        if (args.Count < offset + 1 || args.Count > offset + 2)
            return Usage("grep [-i] [-E] [-r] pattern [file]");
        if (recursive && args.Count != offset + 2)
            return Usage("grep -r pattern directory");
        var pattern = args[offset];
        var inputs = new List<(string? Path, string Text)>();
        if (args.Count == offset + 2)
        {
            var path = args[offset + 1];
            var input = string.Empty;
            var read = recursive
                ? CollectGrepInputs(session, host, path, inputs, 0)
                : ReadGrepInput(session, host, path, out input);
            if (read != DwaineVfsResult.Success)
                return new CommandResult(2, Error: FileError("grep", path, read) + "\n");
            if (!recursive)
                inputs.Add((null, input));
        }
        else
        {
            inputs.Add((null, stdin));
        }
        if (inputs.Sum(input => input.Text.Length) > 65_536 || pattern.Length > 256)
            return new CommandResult(2, Error: "grep: input limit exceeded\n");

        Regex? compiled = null;
        try
        {
            if (regex)
                compiled = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return new CommandResult(2, Error: "grep: invalid regular expression\n");
        }

        var comparison = insensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var matches = new List<string>();
        try
        {
            foreach (var input in inputs)
            {
                foreach (var line in input.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    if (compiled?.IsMatch(line) == true || compiled is null && line.Contains(pattern, comparison))
                        matches.Add(input.Path is null ? line : $"{input.Path}:{line}");
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new CommandResult(2, Error: "grep: regular expression timed out\n");
        }
        return matches.Count == 0
            ? new CommandResult(1)
            : new CommandResult(0, string.Join('\n', matches) + "\n", Instructions: matches.Count + 1);
    }

    private static DwaineVfsResult CollectGrepInputs(
        DwaineShellSession session,
        IDwaineShellHost host,
        string path,
        List<(string? Path, string Text)> inputs,
        int depth)
    {
        if (depth > 32 || inputs.Count >= 1024)
            return DwaineVfsResult.DataLimit;
        var list = host.Files.TryList(host.Identity.Principal, path, session.WorkingDirectory, out var entries);
        if (list != DwaineVfsResult.Success)
            return list;
        foreach (var entry in entries)
        {
            if (host.TryGetPath(entry.Handle, out var childPath) != DwaineVfsResult.Success)
                return DwaineVfsResult.InvalidHandle;
            if (entry.Kind == DwaineVfsNodeKind.Directory)
            {
                var recurse = CollectGrepInputs(session, host, childPath, inputs, depth + 1);
                if (recurse != DwaineVfsResult.Success)
                    return recurse;
                continue;
            }
            if (entry.Kind == DwaineVfsNodeKind.SymbolicLink)
                continue;
            var read = ReadGrepInput(session, host, childPath, out var text);
            if (read == DwaineVfsResult.InvalidType)
                continue;
            if (read != DwaineVfsResult.Success)
                return read;
            inputs.Add((childPath, text));
            if (inputs.Count > 1024 || inputs.Sum(input => input.Text.Length) > 65_536)
                return DwaineVfsResult.DataLimit;
        }
        return DwaineVfsResult.Success;
    }

    private static DwaineVfsResult ReadGrepInput(
        DwaineShellSession session,
        IDwaineShellHost host,
        string path,
        out string input)
    {
        var read = host.Files.TryReadText(host.Identity.Principal, path, session.WorkingDirectory, out input);
        if (read != DwaineVfsResult.InvalidType)
            return read;
        var fields = host.Files.TryGetFields(host.Identity.Principal, path, session.WorkingDirectory, out var record);
        input = fields == DwaineVfsResult.Success
            ? string.Join('\n', record.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))
            : string.Empty;
        return fields;
    }

    private static CommandResult Getopt(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count < 1 || args[0].Length > 64)
            return Usage("getopt OPTSPEC arguments...");
        var spec = args[0];
        var output = new List<string>();
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--")
            {
                output.AddRange(args.Skip(index + 1).Select(value => $"arg {value}"));
                break;
            }
            if (!argument.StartsWith('-') || argument == "-")
            {
                output.Add($"arg {argument}");
                continue;
            }
            foreach (var option in argument[1..])
            {
                var specIndex = spec.IndexOf(option);
                if (specIndex < 0)
                    return new CommandResult(2, Error: $"getopt: unknown option -{option}\n");
                if (specIndex + 1 < spec.Length && spec[specIndex + 1] == ':')
                {
                    if (++index >= args.Count)
                        return new CommandResult(2, Error: $"getopt: option -{option} requires a value\n");
                    output.Add($"option {option}={args[index]}");
                }
                else
                {
                    output.Add($"option {option}");
                }
            }
        }
        return new CommandResult(0, string.Join('\n', output) + (output.Count > 0 ? "\n" : string.Empty));
    }

    private static CommandResult Tar(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 3 && args[0] == "-c")
        {
            var result = host.Files.TryCreateArchive(host.Identity.Principal, args[2], args[1], session.WorkingDirectory, host.Now);
            return result == DwaineVfsResult.Success ? new CommandResult(0) : new CommandResult(1, Error: FileError("tar", args[1], result) + "\n");
        }
        if (args.Count == 2 && args[0] == "-t")
        {
            var result = host.Files.TryListArchive(host.Identity.Principal, args[1], session.WorkingDirectory, out var entries);
            var paths = new List<string>();
            foreach (var entry in entries)
                AppendArchivePaths(entry, string.Empty, paths);
            return result == DwaineVfsResult.Success
                ? new CommandResult(0, string.Join('\n', paths) + (paths.Count > 0 ? "\n" : string.Empty), Instructions: paths.Count + 1)
                : new CommandResult(1, Error: FileError("tar", args[1], result) + "\n");
        }
        if (args.Count == 3 && args[0] == "-x")
        {
            var result = host.Files.TryExtractArchive(host.Identity.Principal, args[1], args[2], session.WorkingDirectory, host.Now);
            return result == DwaineVfsResult.Success ? new CommandResult(0) : new CommandResult(1, Error: FileError("tar", args[1], result) + "\n");
        }
        return Usage("tar -c archive source | -t archive | -x archive directory");
    }

    private static void AppendArchivePaths(DwaineVfsArchiveEntry entry, string prefix, List<string> paths)
    {
        var path = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
        paths.Add(path);
        foreach (var child in entry.Children)
            AppendArchivePaths(child, path, paths);
        foreach (var embedded in entry.EmbeddedArchiveEntries)
            AppendArchivePaths(embedded, $"{path}!", paths);
    }

    private static CommandResult Mount(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        DwaineShellHostResult result;
        if (args.Count == 0)
            result = host.ListMedia();
        else if (args.Count == 2 && args[0] == "-u")
            result = host.Unmount(args[1]);
        else if (args.Count == 2)
            result = host.Mount(args[0], args[1]);
        else
            return Usage("mount [LABEL PATH|-u LABEL]");
        return result.ExitCode == 0
            ? new CommandResult(0, result.Output)
            : new CommandResult(result.ExitCode, Error: result.Output);
    }

    private static CommandResult Su(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 2)
            return Usage("su USER PASSWORD");
        if (host.Now < session.NextAuthenticationAt)
            return new CommandResult(1, Error: "su: authentication temporarily throttled\n");
        var result = host.TryElevate(args[0], args[1], out _);
        if (result == DwaineIdentityResult.Success)
        {
            session.FailedAuthenticationAttempts = 0;
            session.NextAuthenticationAt = TimeSpan.Zero;
            return new CommandResult(0, $"identity changed to {args[0]}\n", TerminateProcess: true);
        }

        session.FailedAuthenticationAttempts = Math.Min(session.FailedAuthenticationAttempts + 1, 5);
        session.NextAuthenticationAt = host.Now + TimeSpan.FromSeconds(
            1 << (session.FailedAuthenticationAttempts - 1));
        return new CommandResult(1, Error: $"su: {IdentityError(result)}\n");
    }

    private static CommandResult Set(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 0)
        {
            var lines = session.Environment
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}");
            return new CommandResult(0, string.Join('\n', lines) + "\n");
        }
        if (args.Count != 1)
            return Usage("set [NAME=VALUE]");
        var separator = args[0].IndexOf('=');
        if (separator <= 0 || !session.TrySetEnvironment(args[0][..separator], args[0][(separator + 1)..]))
            return new CommandResult(2, Error: "set: invalid or over-limit assignment\n");
        return new CommandResult(0);
    }

    private static CommandResult Unset(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 0)
            return Usage("unset NAME...");
        foreach (var name in args)
        {
            if (!session.TryUnsetEnvironment(name))
                return new CommandResult(1, Error: $"unset: cannot remove {name}\n");
        }
        return new CommandResult(0);
    }

    private static CommandResult Mesg(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 0)
            return new CommandResult(0, session.MessagesEnabled ? "is y\n" : "is n\n");
        if (args.Count != 1 || args[0] is not ("y" or "n"))
            return Usage("mesg [y|n]");
        session.MessagesEnabled = args[0] == "y";
        return new CommandResult(0);
    }

    private static CommandResult Talk(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count < 2)
            return Usage("talk USER message...");
        var result = host.Talk(args[0], string.Join(' ', args.Skip(1)));
        return result.ExitCode == 0 ? new CommandResult(0, result.Output) : new CommandResult(result.ExitCode, Error: result.Output);
    }

    private static CommandResult Who(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("who");
        var users = host.GetUsers();
        return new CommandResult(0, string.Join('\n', users.Select(user => user.Temporary ? $"{user.Name} (guest)" : user.Name)) + (users.Count > 0 ? "\n" : string.Empty));
    }

    private static CommandResult Net(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (host is not IDwaineNetworkShellHost network)
            return new CommandResult(1, Error: "net: service unavailable\n");
        var result = network.Network(args, session.WorkingDirectory);
        return result.ExitCode == 0
            ? new CommandResult(0, result.Output)
            : new CommandResult(result.ExitCode, Error: result.Output);
    }

    private static CommandResult Scan(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("scnt");
        if (host is not IDwaineNetworkShellHost network || session.ProcessId is not { } process)
            return new CommandResult(1, Error: "scnt: service unavailable\n");
        var result = network.Scan(process);
        return result.ExitCode == 0
            ? new CommandResult(0, result.Output)
            : new CommandResult(result.ExitCode, Error: result.Output);
    }

    private static CommandResult Sleep(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 1
            || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 0 or > 300)
        {
            return Usage("sleep SECONDS (0..300)");
        }
        session.SleepingUntil = host.Now + TimeSpan.FromSeconds(seconds);
        return new CommandResult(0);
    }

    private CommandResult Eval(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count == 0)
            return Usage("eval shell-text...");
        var result = Execute(string.Join(' ', args), session, host, depth + 1, false);
        return new CommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.ClearScreen, result.TerminateProcess, result.InstructionsConsumed);
    }

    private static CommandResult If(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 3)
            return Usage("if LEFT OP RIGHT");
        var result = args[1] switch
        {
            "=" => string.Equals(args[0], args[2], StringComparison.Ordinal),
            "!=" => !string.Equals(args[0], args[2], StringComparison.Ordinal),
            "-eq" => CompareIntegers(args[0], args[2], comparison => comparison == 0),
            "-ne" => CompareIntegers(args[0], args[2], comparison => comparison != 0),
            "-lt" => CompareIntegers(args[0], args[2], comparison => comparison < 0),
            "-le" => CompareIntegers(args[0], args[2], comparison => comparison <= 0),
            "-gt" => CompareIntegers(args[0], args[2], comparison => comparison > 0),
            "-ge" => CompareIntegers(args[0], args[2], comparison => comparison >= 0),
            _ => null,
        };
        return result is null ? Usage("if LEFT OP RIGHT") : new CommandResult(result.Value ? 0 : 1);
    }

    private static bool? CompareIntegers(string left, string right, Func<int, bool> predicate)
    {
        if (!long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftValue)
            || !long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightValue))
        {
            return null;
        }
        return predicate(leftValue.CompareTo(rightValue));
    }

    private static CommandResult Else(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        return args.Count == 0 ? new CommandResult(session.LastExitCode == 0 ? 1 : 0) : Usage("else");
    }

    private CommandResult While(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count < 2
            || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count < 0
            || count > _limits.MaxLoopIterations)
        {
            return Usage($"while COUNT(0..{_limits.MaxLoopIterations}) command...");
        }

        var command = string.Join(' ', args.Skip(1));
        var output = new BoundedOutput(_limits.MaxOutputCharacters);
        var error = new BoundedOutput(_limits.MaxOutputCharacters);
        var exit = 0;
        var instructions = 1;
        var terminate = false;
        session.LoopDepth++;
        try
        {
            for (var iteration = 0; iteration < count; iteration++)
            {
                var result = Execute(command, session, host, depth + 1, false);
                output.Append(result.StandardOutput);
                error.Append(result.StandardError);
                exit = result.ExitCode;
                instructions += result.InstructionsConsumed;
                terminate |= result.TerminateProcess;
                if (session.BreakRequested || exit != 0 || terminate)
                    break;
            }
        }
        finally
        {
            session.LoopDepth--;
            if (session.LoopDepth == 0)
                session.BreakRequested = false;
        }
        if (output.Truncated || error.Truncated)
        {
            exit = 1;
            if (!error.Truncated)
                error.AppendLine("shell: output limit exceeded");
        }
        return new CommandResult(exit, output.ToString(), error.ToString(), TerminateProcess: terminate, Instructions: instructions);
    }

    private static CommandResult Break(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        if (args.Count != 0)
            return Usage("break");
        if (session.LoopDepth == 0)
            return new CommandResult(1, Error: "break: not inside a loop\n");
        session.BreakRequested = true;
        return new CommandResult(0);
    }

    private static CommandResult WhiskeySay(DwaineShellSession session, IDwaineShellHost host, IReadOnlyList<string> args, string stdin, int depth)
    {
        return new CommandResult(0, $"[WHISKEY] {string.Join(' ', args)}\n");
    }

    private static CommandResult Vodka(
        DwaineShellSession session,
        IDwaineShellHost host,
        IReadOnlyList<string> args,
        string stdin,
        int depth)
    {
        if (args.Count < 1)
            return new CommandResult(2, Error: "usage: vodka FILE.vodka [argument...]\n");
        if (host is not IDwaineVodkaShellHost vodkaHost || session.ProcessId is not { } parent)
            return new CommandResult(126, Error: "vodka: runtime unavailable\n");

        var started = vodkaHost.TryStartVodka(parent, session.WorkingDirectory, args[0], args.Skip(1).ToArray());
        return started.Succeeded
            ? new CommandResult(0, WaitFor: started.ProcessId)
            : new CommandResult(1, Error: started.Error);
    }

    private static bool TryParseMode(string text, out DwaineVfsMode mode)
    {
        mode = DwaineVfsMode.None;
        if (text.Length != 3 || text.Any(character => character is < '0' or > '7'))
            return false;
        var values = text.Select(character => character - '0').ToArray();
        mode = ApplyMode(values[0], DwaineVfsMode.OwnerRead, DwaineVfsMode.OwnerWrite, DwaineVfsMode.OwnerExecute)
               | ApplyMode(values[1], DwaineVfsMode.GroupRead, DwaineVfsMode.GroupWrite, DwaineVfsMode.GroupExecute)
               | ApplyMode(values[2], DwaineVfsMode.OtherRead, DwaineVfsMode.OtherWrite, DwaineVfsMode.OtherExecute);
        return true;
    }

    private static DwaineVfsMode ApplyMode(int value, DwaineVfsMode read, DwaineVfsMode write, DwaineVfsMode execute)
    {
        var mode = DwaineVfsMode.None;
        if ((value & 4) != 0)
            mode |= read;
        if ((value & 2) != 0)
            mode |= write;
        if ((value & 1) != 0)
            mode |= execute;
        return mode;
    }

    private static string FormatMode(DwaineVfsMode mode)
    {
        return string.Create(9, mode, static (span, value) =>
        {
            span[0] = value.HasFlag(DwaineVfsMode.OwnerRead) ? 'r' : '-';
            span[1] = value.HasFlag(DwaineVfsMode.OwnerWrite) ? 'w' : '-';
            span[2] = value.HasFlag(DwaineVfsMode.OwnerExecute) ? 'x' : '-';
            span[3] = value.HasFlag(DwaineVfsMode.GroupRead) ? 'r' : '-';
            span[4] = value.HasFlag(DwaineVfsMode.GroupWrite) ? 'w' : '-';
            span[5] = value.HasFlag(DwaineVfsMode.GroupExecute) ? 'x' : '-';
            span[6] = value.HasFlag(DwaineVfsMode.OtherRead) ? 'r' : '-';
            span[7] = value.HasFlag(DwaineVfsMode.OtherWrite) ? 'w' : '-';
            span[8] = value.HasFlag(DwaineVfsMode.OtherExecute) ? 'x' : '-';
        });
    }

    private static string FileError(string command, string path, DwaineVfsResult result)
    {
        var message = result switch
        {
            DwaineVfsResult.AccessDenied => "permission denied",
            DwaineVfsResult.NotFound => "not found",
            DwaineVfsResult.AlreadyExists => "already exists",
            DwaineVfsResult.NotDirectory => "not a directory",
            DwaineVfsResult.IsDirectory => "is a directory",
            DwaineVfsResult.ReadOnly => "read-only filesystem",
            DwaineVfsResult.RootProtected or DwaineVfsResult.RootEscape => "root is protected",
            DwaineVfsResult.DirectoryNotEmpty => "directory not empty",
            DwaineVfsResult.CrossVolumeMoveDenied => "cross-volume move denied",
            DwaineVfsResult.DataLimit or DwaineVfsResult.NodeLimit or DwaineVfsResult.ChildLimit => "resource limit exceeded",
            _ => result.ToString().ToLowerInvariant(),
        };
        return string.IsNullOrEmpty(path) ? $"{command}: {message}" : $"{command}: {message}: {path}";
    }

    private static string IdentityError(DwaineIdentityResult result)
    {
        return result switch
        {
            DwaineIdentityResult.InvalidCredential => "authentication failed",
            DwaineIdentityResult.Disabled => "account disabled",
            DwaineIdentityResult.SessionExpired => "session expired",
            DwaineIdentityResult.Throttled => "authentication temporarily throttled",
            DwaineIdentityResult.AccessDenied => "permission denied",
            _ => result.ToString().ToLowerInvariant(),
        };
    }

    private static CommandResult Usage(string usage)
    {
        return new CommandResult(2, Error: $"usage: {usage}\n");
    }

    private static string RedactHistory(
        string source,
        DwaineShellLineNode line,
        bool credentialCommandObserved)
    {
        return credentialCommandObserved || line.Pipelines
            .SelectMany(pipeline => pipeline.Commands)
            .Any(command => command.Words.Count > 0
                            && string.Equals(command.Words[0].Text, "su", StringComparison.OrdinalIgnoreCase))
                ? "<redacted credential command>"
                : source;
    }

    private bool TryChargeInstructions(int instructions)
    {
        if (instructions <= 0 || instructions > _remainingInstructionBudget)
            return false;
        _remainingInstructionBudget -= instructions;
        return true;
    }

    private static DwaineShellExecutionResult BudgetExceeded()
    {
        return new DwaineShellExecutionResult(
            1,
            string.Empty,
            "shell: command budget exceeded\n",
            false,
            true,
            1);
    }

    private sealed class BoundedOutput(int limit)
    {
        private readonly StringBuilder _builder = new();
        public bool Truncated { get; private set; }

        public void Append(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (_builder.Length >= limit)
            {
                Truncated = true;
                return;
            }
            var remaining = limit - _builder.Length;
            _builder.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
            Truncated |= value.Length > remaining;
        }

        public void AppendLine(string value)
        {
            Append(value);
            Append("\n");
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
