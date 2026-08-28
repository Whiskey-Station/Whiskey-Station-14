using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Robust.Packaging;

namespace Content.Packaging;

public static class LauncherEngineGuard
{
    private const string LockPath = "Tools/launcher_engine_lock.json";
    private const string EnginePath = "RobustToolbox";

    public static async Task VerifyAsync(IPackageLogger logger, bool skipBuild)
    {
        var engineLock = ReadLock();
        var engineDirectory = Path.GetFullPath(EnginePath);
        if (!Directory.Exists(engineDirectory))
            Refuse($"launcher engine directory does not exist: {engineDirectory}");

        var head = (await RunGitAsync(engineDirectory, "rev-parse", "HEAD")).ToLowerInvariant();
        if (head != engineLock.Commit)
            Refuse($"launcher engine commit mismatch: expected {engineLock.Commit}, found {head}");

        var exactTag = await RunGitAsync(engineDirectory, "describe", "--tags", "--exact-match", "HEAD");
        if (exactTag != engineLock.Tag)
            Refuse($"launcher engine tag mismatch: expected {engineLock.Tag}, found {exactTag}");

        var versionPath = Path.Combine(engineDirectory, "MSBuild", "Robust.Engine.Version.props");
        string version;
        try
        {
            var document = XDocument.Load(versionPath);
            var versions = document.Descendants().Where(node => node.Name.LocalName == "Version").ToList();
            if (versions.Count != 1)
                Refuse($"expected exactly one Version element in {versionPath}");
            version = versions[0].Value.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Refuse($"cannot read launcher engine version from {versionPath}: {exception.Message}");
            throw;
        }

        if (version != engineLock.Version)
            Refuse($"launcher engine version mismatch: expected {engineLock.Version}, found {version}");

        var status = await RunGitAsync(
            engineDirectory,
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignore-submodules=none");
        if (status.Length != 0)
            Refuse("launcher engine working tree or submodules are not clean");

        var submodules = await RunGitAsync(engineDirectory, "submodule", "status", "--recursive");
        var invalidSubmodules = submodules.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line[0] != ' ')
            .ToList();
        if (invalidSubmodules.Count != 0)
            Refuse("launcher engine has missing or mismatched submodules: " + string.Join("; ", invalidSubmodules));

        if (skipBuild)
        {
            Refuse(
                "client --skip-build is disabled because stale binaries may have been compiled against "
                + "a different engine; every client package requires a fresh guarded /t:Rebuild");
        }

        logger.Info(
            $"Verified launcher engine {engineLock.Tag} ({engineLock.Version}) at {engineLock.Commit}.");
    }

    private static LauncherEngineLock ReadLock()
    {
        LauncherEngineLock? engineLock;
        try
        {
            var json = File.ReadAllText(LockPath);
            engineLock = JsonSerializer.Deserialize<LauncherEngineLock>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Refuse($"cannot read launcher engine lock {LockPath}: {exception.Message}");
            throw;
        }

        if (engineLock == null
            || engineLock.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(engineLock.Repository)
            || string.IsNullOrWhiteSpace(engineLock.Tag)
            || string.IsNullOrWhiteSpace(engineLock.Version)
            || engineLock.Commit == null
            || engineLock.Commit.Length != 40
            || engineLock.Commit.Any(character => !Uri.IsHexDigit(character)))
        {
            Refuse($"invalid launcher engine lock {LockPath}");
        }

        engineLock.Commit = engineLock.Commit.ToLowerInvariant();
        return engineLock;
    }

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Refuse($"cannot start git while verifying {workingDirectory}: {exception.Message}");
            throw;
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // Preserve the leading status character used by `git submodule status`.
        var output = (await standardOutput).TrimEnd('\r', '\n');
        var error = (await standardError).Trim();
        if (process.ExitCode != 0)
        {
            var detail = error.Length != 0 ? error : output;
            Refuse($"git {string.Join(' ', arguments)} failed: {detail}");
        }

        return output;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Refuse(string reason)
    {
        throw new InvalidOperationException(
            "LAUNCHER_ENGINE_GUARD: refusing to package SS14.Client.zip. " + reason + Environment.NewLine
            + "Use `python3 Tools/launcher_engine.py clone RobustToolbox.launcher`, then place that "
            + "verified checkout at RobustToolbox before packaging the client.");
    }

    private sealed class LauncherEngineLock
    {
        public int SchemaVersion { get; set; }
        public string? Repository { get; set; }
        public string? Tag { get; set; }
        public string? Version { get; set; }
        public string? Commit { get; set; }
    }
}
