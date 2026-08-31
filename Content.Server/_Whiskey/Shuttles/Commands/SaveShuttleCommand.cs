using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Whiskey.Shuttles.Commands;

/// <summary>
/// Saves a shuttle grid to a predictable YAML-only directory in user data.
/// This avoids the generic savegrid file completion exposing database files.
/// </summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class SaveShuttleCommand : LocalizedEntityCommands
{
    private static readonly ResPath ShuttleDirectory = new("/Maps/Shuttles");

    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;

    public override string Command => "saveshuttle";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !EntityManager.TryGetEntity(netEntity, out var entity) ||
            !EntityManager.EntityExists(entity))
        {
            shell.WriteError(Loc.GetString("cmd-saveshuttle-invalid-entity", ("uid", args[0])));
            return;
        }

        if (!EntityManager.HasComponent<MapGridComponent>(entity) || EntityManager.HasComponent<MapComponent>(entity))
        {
            shell.WriteError(Loc.GetString("cmd-saveshuttle-not-grid", ("uid", args[0])));
            return;
        }

        if (!TryGetTargetPath(args[1], out var target, out var error))
        {
            shell.WriteError(Loc.GetString(error));
            return;
        }

        try
        {
            if (!_mapLoader.TrySaveGrid(entity.Value, target))
            {
                shell.WriteError(Loc.GetString("cmd-saveshuttle-failure"));
                return;
            }
        }
        catch (Exception exception)
        {
            _logManager.GetSawmill(Command).Error($"Failed to save grid {entity}: {exception}");
            shell.WriteError(Loc.GetString("cmd-saveshuttle-failure"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-saveshuttle-success", ("path", target)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<MapGridComponent>(args[0], EntityManager),
                Loc.GetString("cmd-saveshuttle-hint-grid")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-saveshuttle-hint-filename")),
            _ => CompletionResult.Empty,
        };
    }

    internal static bool TryGetTargetPath(string argument, out ResPath target, out string error)
    {
        target = default;
        error = string.Empty;

        var fileName = argument.Trim();
        if (!ResPath.IsValidFilename(fileName))
        {
            error = "cmd-saveshuttle-invalid-filename";
            return false;
        }

        var requested = new ResPath(fileName);
        if (requested.Extension.Length != 0 &&
            !requested.Extension.Equals("yml", StringComparison.OrdinalIgnoreCase))
        {
            error = "cmd-saveshuttle-invalid-extension";
            return false;
        }

        target = ShuttleDirectory / $"{requested.FilenameWithoutExtension}.yml";
        return true;
    }
}
