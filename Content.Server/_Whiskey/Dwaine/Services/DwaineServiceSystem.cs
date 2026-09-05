// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server.Cargo.Systems;
using Content.Server.CrewManifest;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Services;
using Content.Shared.Cargo.Components;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Components;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Timing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.Dwaine.Services;

/// <summary>
/// Authoritative façade for persistent DWAINE services and explicitly supported station APIs.
/// Every call is scoped to a live process owner; service arguments never contain an EntityUid,
/// session identifier, station reference or arbitrary dependency-container handle.
/// </summary>
public sealed partial class DwaineServiceSystem : EntitySystem
{
    private static readonly string[] BaseServices =
    [
        "diagnostics",
        "documents",
        "email",
        "logs",
    ];

    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineProcessSystem _processes = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private StationJobsSystem _stationJobs = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineServiceSuiteComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineServiceRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
    }

    public DwaineServiceResponse ListServices(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal)
    {
        if (!TryGetCaller(mainframe, process, principal, out _, out _, out _))
            return DwaineServiceResponse.Failure(DwaineServiceStatus.AccessDenied, "service: access denied\n");
        var services = BaseServices.ToList();
        if (TryComp<DwaineStationServiceBridgeComponent>(mainframe, out var bridge))
        {
            if (bridge.Bank)
                services.Add("bank");
            if (bridge.Manifest)
                services.Add("manifest");
            if (bridge.Records)
                services.Add("records");
            if (bridge.Jobs)
                services.Add("jobs");
        }
        services.Sort(StringComparer.Ordinal);
        return DwaineServiceResponse.Success(string.Join('\n', services) + "\n");
    }

    public DwaineServiceResponse Call(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        string service,
        string operation,
        IReadOnlyList<string> arguments,
        DwaineVfsNodeHandle workingDirectory)
    {
        if (!TryGetCaller(mainframe, process, principal, out var runtime, out var identities, out var account))
            return DwaineServiceResponse.Failure(DwaineServiceStatus.AccessDenied, "service: access denied\n");
        var normalizedService = NormalizeName(service);
        var normalizedOperation = NormalizeName(operation);
        DwaineServiceResponse response;
        if (normalizedService.Length == 0 || normalizedOperation.Length == 0 || arguments.Any(argument => !ValidArgument(argument)))
        {
            response = DwaineServiceResponse.Failure(DwaineServiceStatus.InvalidArguments, "service: invalid arguments\n");
        }
        else
        {
            response = normalizedService switch
            {
                "email" => Email(runtime.Store!, identities, account, normalizedOperation, arguments),
                "documents" => Documents(mainframe, identities, principal, normalizedOperation, arguments, workingDirectory),
                "logs" => Logs(runtime.Store!, identities, account, normalizedOperation, arguments),
                "manifest" => Manifest(mainframe, normalizedOperation, arguments),
                "bank" => Bank(mainframe, identities, account, normalizedOperation, arguments),
                "records" => Records(mainframe, identities, account, normalizedOperation, arguments),
                "jobs" => Jobs(mainframe, identities, account, normalizedOperation, arguments),
                "diagnostics" => Diagnostics(mainframe, runtime.Store!, identities, account, normalizedOperation, arguments),
                _ => DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "service: service not found\n"),
            };
        }

        var limit = Limits(Comp<DwaineServiceSuiteComponent>(mainframe)).MaxServiceOutputCharacters;
        if (response.Output.Length > limit)
            response = DwaineServiceResponse.Failure(DwaineServiceStatus.CapacityReached, "service: output limit exceeded\n");
        runtime.Store!.Record(_timing.CurTime, account.Name, normalizedService, normalizedOperation, response.Status);
        return response;
    }

    public DwaineServiceMetrics GetMetrics(EntityUid mainframe)
        => TryComp<DwaineServiceRuntimeComponent>(mainframe, out var runtime) && runtime.Store is { } store
            ? store.GetMetrics()
            : default;

    private DwaineServiceResponse Email(
        DwaineServiceStore store,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (actor.Temporary)
            return Denied("email");
        switch (operation)
        {
            case "send" when arguments.Count >= 3:
            {
                var recipients = ResolveRecipients(identities, actor, arguments[0]);
                if (recipients.Status != DwaineServiceStatus.Success)
                    return DwaineServiceResponse.Failure(recipients.Status, "email: recipient not found or denied\n");
                var status = store.TrySendMail(
                    actor.Name,
                    recipients.Recipients,
                    arguments[1],
                    string.Join(' ', arguments.Skip(2)),
                    _timing.CurTime);
                return status == DwaineServiceStatus.Success
                    ? DwaineServiceResponse.Success($"sent {recipients.Recipients.Length}\n")
                    : DwaineServiceResponse.Failure(status, $"email: {StatusName(status)}\n");
            }
            case "list" when arguments.Count == 0:
            {
                var output = store.ListMail(actor.Principal)
                    .Select(mail => $"{mail.Id}\t{mail.Sender}\t{mail.Subject}");
                var text = string.Join('\n', output);
                return DwaineServiceResponse.Success(text.Length == 0 ? string.Empty : text + "\n");
            }
            case "read" when arguments.Count == 1 && ulong.TryParse(arguments[0], out var readId):
            {
                var status = store.TryReadMail(actor.Principal, readId, out var mail);
                return status == DwaineServiceStatus.Success
                    ? DwaineServiceResponse.Success($"from: {mail.Sender}\nsubject: {mail.Subject}\n\n{mail.Body}\n")
                    : DwaineServiceResponse.Failure(status, "email: message not found\n");
            }
            case "delete" when arguments.Count == 1 && ulong.TryParse(arguments[0], out var deleteId):
            {
                var status = store.TryDeleteMail(actor.Principal, deleteId);
                return status == DwaineServiceStatus.Success
                    ? DwaineServiceResponse.Success()
                    : DwaineServiceResponse.Failure(status, "email: message not found\n");
            }
            default:
                return DwaineServiceResponse.Failure(
                    DwaineServiceStatus.InvalidArguments,
                    "usage: service email send USER|group:GROUP SUBJECT BODY...|list|read ID|delete ID\n");
        }
    }

    private DwaineServiceResponse Documents(
        EntityUid mainframe,
        DwaineIdentityStore identities,
        DwainePrincipalId principal,
        string operation,
        IReadOnlyList<string> arguments,
        DwaineVfsNodeHandle workingDirectory)
    {
        if (!_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Unavailable, "documents: filesystem unavailable\n");
        var files = new DwaineAuthorizedFileSystem(fileSystem, identities);
        switch (operation)
        {
            case "list" when arguments.Count <= 1:
            {
                var status = files.TryList(principal, arguments.Count == 0 ? "." : arguments[0], workingDirectory, out var entries);
                return VfsResponse("documents", status, status == DwaineVfsResult.Success
                    ? string.Join('\n', entries.Select(entry => $"{entry.Name}\t{entry.Kind.ToString().ToLowerInvariant()}"))
                    : string.Empty);
            }
            case "read" when arguments.Count == 1:
            {
                var status = files.TryReadText(principal, arguments[0], workingDirectory, out var text);
                return VfsResponse("documents", status, text);
            }
            case "write" or "append" when arguments.Count >= 2:
            {
                var text = string.Join(' ', arguments.Skip(1));
                var append = operation == "append";
                var status = files.TryWriteText(principal, arguments[0], workingDirectory, text, append, _timing.CurTime);
                if (status == DwaineVfsResult.NotFound && !append)
                    status = files.TryCreateText(principal, arguments[0], workingDirectory, text, null, _timing.CurTime);
                return VfsResponse("documents", status, string.Empty);
            }
            case "delete" when arguments.Count == 1:
                return VfsResponse(
                    "documents",
                    files.TryDelete(principal, arguments[0], workingDirectory, false, _timing.CurTime),
                    string.Empty);
            default:
                return DwaineServiceResponse.Failure(
                    DwaineServiceStatus.InvalidArguments,
                    "usage: service documents list [PATH]|read PATH|write PATH TEXT...|append PATH TEXT...|delete PATH\n");
        }
    }

    private DwaineServiceResponse Logs(
        DwaineServiceStore store,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (operation == "append" && arguments.Count >= 2)
        {
            store.Record(
                _timing.CurTime,
                actor.Name,
                "user",
                arguments[0],
                DwaineServiceStatus.Success,
                string.Join(' ', arguments.Skip(1)));
            return DwaineServiceResponse.Success();
        }
        if (operation == "list" && arguments.Count <= 1)
        {
            if (!IsOperator(identities, actor.Principal))
                return Denied("logs");
            var count = 50;
            if (arguments.Count == 1 && (!int.TryParse(arguments[0], out count) || count <= 0))
                return Invalid("logs");
            var lines = store.GetLogs(count).Select(entry =>
                $"{entry.Sequence}\tT+{entry.Time.TotalSeconds:F3}\t{entry.Actor}\t{entry.Service}.{entry.Operation}\t{StatusName(entry.Status)}" +
                (entry.Detail.Length == 0 ? string.Empty : $"\t{entry.Detail}"));
            return DwaineServiceResponse.Success(string.Join('\n', lines) + "\n");
        }
        return DwaineServiceResponse.Failure(
            DwaineServiceStatus.InvalidArguments,
            "usage: service logs append CATEGORY MESSAGE...|list [COUNT]\n");
    }

    private DwaineServiceResponse Manifest(
        EntityUid mainframe,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (!TryComp<DwaineStationServiceBridgeComponent>(mainframe, out var bridge)
            || !bridge.Manifest
            || operation != "list"
            || arguments.Count != 0)
        {
            return Invalid("manifest");
        }
        if (_stations.GetOwningStation(mainframe) is not { } station)
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Unavailable, "manifest: station unavailable\n");
        var (stationName, entries) = _crewManifest.GetCrewManifest(station);
        if (entries is null)
            return DwaineServiceResponse.Success($"station: {SafeColumn(stationName)}\n");
        var lines = entries.Entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{SafeColumn(entry.Name)}\t{SafeColumn(entry.JobTitle)}");
        return DwaineServiceResponse.Success($"station: {SafeColumn(stationName)}\n{string.Join('\n', lines)}\n");
    }

    private DwaineServiceResponse Bank(
        EntityUid mainframe,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (!TryComp<DwaineStationServiceBridgeComponent>(mainframe, out var bridge) || !bridge.Bank)
            return DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "bank: service not available\n");
        if (!IsOperator(identities, actor.Principal))
            return Denied("bank");
        if (_stations.GetOwningStation(mainframe) is not { } station
            || !TryComp<StationBankAccountComponent>(station, out var bank))
        {
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Unavailable, "bank: station account unavailable\n");
        }

        if (operation == "balance" && arguments.Count <= 1)
        {
            var accounts = bank.Accounts
                .Where(pair => arguments.Count == 0 || string.Equals(pair.Key.Id, arguments[0], StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key.Id}\t{pair.Value}")
                .ToArray();
            return accounts.Length == 0
                ? DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "bank: account not found\n")
                : DwaineServiceResponse.Success(string.Join('\n', accounts) + "\n");
        }
        if (operation == "transfer"
            && arguments.Count == 3
            && int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            && amount > 0)
        {
            var source = bank.Accounts.Keys.FirstOrDefault(key => string.Equals(key.Id, arguments[0], StringComparison.OrdinalIgnoreCase));
            var destination = bank.Accounts.Keys.FirstOrDefault(key => string.Equals(key.Id, arguments[1], StringComparison.OrdinalIgnoreCase));
            if (source.Id is null || destination.Id is null || source == destination)
                return Invalid("bank");
            if (bank.Accounts[source] < amount)
                return DwaineServiceResponse.Failure(DwaineServiceStatus.Conflict, "bank: insufficient funds\n");
            if (!_cargo.TryAdjustBankAccount((station, bank), source, -amount))
                return DwaineServiceResponse.Failure(DwaineServiceStatus.Conflict, "bank: transfer failed\n");
            if (_cargo.TryAdjustBankAccount((station, bank), destination, amount))
                return DwaineServiceResponse.Success("transferred\n");
            _cargo.TryAdjustBankAccount((station, bank), source, amount);
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Conflict, "bank: transfer rolled back\n");
        }
        return DwaineServiceResponse.Failure(
            DwaineServiceStatus.InvalidArguments,
            "usage: service bank balance [ACCOUNT]|transfer FROM TO AMOUNT\n");
    }

    private DwaineServiceResponse Diagnostics(
        EntityUid mainframe,
        DwaineServiceStore store,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (operation != "snapshot" || arguments.Count != 0)
            return Invalid("diagnostics");
        if (!IsOperator(identities, actor.Principal))
            return Denied("diagnostics");
        var processes = _processes.GetProcessTable(mainframe);
        var instructions = processes.Aggregate(0L, (total, process) =>
            long.MaxValue - total < process.InstructionsConsumed ? long.MaxValue : total + process.InstructionsConsumed);
        var nodes = _fileSystems.TryGetFileSystem(mainframe, out var fileSystem) ? fileSystem.NodeCount : 0;
        var metrics = store.GetMetrics();
        return DwaineServiceResponse.Success(
            $"processes={processes.Length} instructions={instructions} vfs_nodes={nodes} " +
            $"mail={metrics.MailMessages} mailboxes={metrics.Mailboxes} logs={metrics.LogEntries} " +
            $"calls={metrics.Calls} failures={metrics.Failures}\n");
    }

    private DwaineServiceResponse Records(
        EntityUid mainframe,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        if (!TryComp<DwaineStationServiceBridgeComponent>(mainframe, out var bridge) || !bridge.Records)
            return DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "records: service not available\n");
        if (!IsOperator(identities, actor.Principal))
            return Denied("records");
        if (arguments.Count != 0)
            return Invalid("records");
        if (_stations.GetOwningStation(mainframe) is not { } station
            || !TryComp<StationRecordsComponent>(station, out var records))
        {
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Unavailable, "records: station records unavailable\n");
        }

        switch (operation)
        {
            case "medical":
            {
                var lines = _stationRecords.GetRecordsOfType<GeneralStationRecord>((station, records))
                    .OrderBy(entry => entry.Item1)
                    .Select(entry =>
                        $"{entry.Item1}\t{SafeColumn(entry.Item2.Name)}\t{entry.Item2.Age}\t" +
                        $"{SafeColumn(entry.Item2.Species)}\t{entry.Item2.Gender.ToString().ToLowerInvariant()}");
                return DwaineServiceResponse.Success(string.Join('\n', lines) + "\n");
            }
            case "security":
            {
                var lines = new List<string>();
                foreach (var (id, record) in _stationRecords.GetRecordsOfType<CriminalRecord>((station, records))
                             .OrderBy(entry => entry.Item1))
                {
                    _stationRecords.TryGetRecord(
                        new StationRecordKey(id, station),
                        out GeneralStationRecord? general,
                        records);
                    lines.Add(
                        $"{id}\t{SafeColumn(general?.Name ?? "unknown")}\t" +
                        $"{record.Status.ToString().ToLowerInvariant()}\t{SafeColumn(record.Reason ?? string.Empty)}");
                }
                return DwaineServiceResponse.Success(string.Join('\n', lines) + "\n");
            }
            default:
                return DwaineServiceResponse.Failure(
                    DwaineServiceStatus.InvalidArguments,
                    "usage: service records medical|security\n");
        }
    }

    private DwaineServiceResponse Jobs(
        EntityUid mainframe,
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string operation,
        IReadOnlyList<string> arguments)
    {
        const int maximumSlots = 256;
        if (!TryComp<DwaineStationServiceBridgeComponent>(mainframe, out var bridge) || !bridge.Jobs)
            return DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "jobs: service not available\n");
        if (!IsOperator(identities, actor.Principal))
            return Denied("jobs");
        if (_stations.GetOwningStation(mainframe) is not { } station
            || !TryComp<StationJobsComponent>(station, out var jobs))
        {
            return DwaineServiceResponse.Failure(DwaineServiceStatus.Unavailable, "jobs: station jobs unavailable\n");
        }

        if (operation == "list" && arguments.Count == 0)
        {
            var lines = _stationJobs.GetJobs(station, jobs)
                .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .Select(pair => $"{SafeColumn(pair.Key.Id)}\t{(pair.Value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited")}");
            return DwaineServiceResponse.Success(string.Join('\n', lines) + "\n");
        }
        if (operation == "set"
            && arguments.Count == 2
            && int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var slots)
            && slots >= 0
            && slots <= maximumSlots)
        {
            return _stationJobs.TrySetJobSlot(station, arguments[0], slots, false, jobs)
                ? DwaineServiceResponse.Success("updated\n")
                : DwaineServiceResponse.Failure(DwaineServiceStatus.NotFound, "jobs: job not found\n");
        }
        return DwaineServiceResponse.Failure(
            DwaineServiceStatus.InvalidArguments,
            $"usage: service jobs list|set JOB SLOTS (0-{maximumSlots})\n");
    }

    private static string SafeColumn(string value)
    {
        const int maximum = 128;
        var sanitized = value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= maximum ? sanitized : sanitized[..maximum];
    }

    private (DwaineServiceStatus Status, DwainePrincipalId[] Recipients) ResolveRecipients(
        DwaineIdentityStore identities,
        DwaineAccountSnapshot actor,
        string target)
    {
        if (target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsOperator(identities, actor.Principal)
                || !identities.TryGetGroup(target[6..], out var group))
            {
                return (DwaineServiceStatus.AccessDenied, []);
            }
            var recipients = identities.GetAccounts()
                .Where(account => account.Enabled && !account.Temporary && account.Groups.Contains(group))
                .Select(account => account.Principal)
                .ToArray();
            return recipients.Length == 0
                ? (DwaineServiceStatus.NotFound, [])
                : (DwaineServiceStatus.Success, recipients);
        }
        return identities.TryGetAccount(target, out var recipient) && recipient.Enabled && !recipient.Temporary
            ? (DwaineServiceStatus.Success, [recipient.Principal])
            : (DwaineServiceStatus.NotFound, []);
    }

    private bool TryGetCaller(
        EntityUid mainframe,
        DwaineProcessId process,
        DwainePrincipalId principal,
        out DwaineServiceRuntimeComponent runtime,
        out DwaineIdentityStore identities,
        out DwaineAccountSnapshot account)
    {
        runtime = null!;
        identities = null!;
        account = default;
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineServiceRuntimeComponent>(mainframe, out var foundRuntime)
            || !foundRuntime.Online
            || foundRuntime.Store is null
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady
            || !_processes.TryGetProcess(mainframe, process, out var caller)
            || caller.State is DwaineProcessState.Exited or DwaineProcessState.Faulted
            || caller.Owner.Value != principal.Value
            || !_identities.TryGetStore(mainframe, out var foundIdentities)
            || !foundIdentities.TryGetAccount(principal, out account)
            || !account.Enabled)
        {
            return false;
        }

        runtime = foundRuntime;
        identities = foundIdentities;
        return true;
    }

    private void OnKernelReady(Entity<DwaineServiceSuiteComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineServiceRuntimeComponent>(ent, out var runtime))
            return;
        runtime.Store ??= new DwaineServiceStore(Limits(ent.Comp));
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (_kernel.TryRegisterService(ent.Owner, "service-suite", new ServiceKernelLease(this, ent.Owner, args.BootGeneration)))
            return;
        runtime.Online = false;
        runtime.BootGeneration = 0;
        _kernel.Panic(ent.Owner, "service-suite-registration");
    }

    private void OnRuntimeShutdown(Entity<DwaineServiceRuntimeComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Online = false;
        ent.Comp.BootGeneration = 0;
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (TryComp<DwaineServiceRuntimeComponent>(mainframe, out var runtime) && runtime.BootGeneration == generation)
        {
            runtime.Online = false;
            runtime.BootGeneration = 0;
        }
    }

    private static DwaineServiceLimits Limits(DwaineServiceSuiteComponent component)
    {
        var maxMail = Math.Clamp(component.MaxMailMessages, 1, DwaineServiceSuiteComponent.HardMaxMailMessages);
        return new(
            maxMail,
            Math.Clamp(component.MaxMailPerUser, 1, Math.Min(maxMail, DwaineServiceSuiteComponent.HardMaxMailPerUser)),
            Math.Clamp(component.MaxMailSubjectCharacters, 1, DwaineServiceSuiteComponent.HardMaxMailSubjectCharacters),
            Math.Clamp(component.MaxMailBodyCharacters, 1, DwaineServiceSuiteComponent.HardMaxMailBodyCharacters),
            Math.Clamp(component.MaxLogEntries, 1, DwaineServiceSuiteComponent.HardMaxLogEntries),
            Math.Clamp(component.MaxServiceOutputCharacters, 1, DwaineServiceSuiteComponent.HardMaxServiceOutputCharacters));
    }

    private static DwaineServiceResponse VfsResponse(string service, DwaineVfsResult result, string output)
    {
        if (result == DwaineVfsResult.Success)
            return DwaineServiceResponse.Success(output.Length == 0 || output.EndsWith('\n') ? output : output + "\n");
        var status = result switch
        {
            DwaineVfsResult.AccessDenied => DwaineServiceStatus.AccessDenied,
            DwaineVfsResult.NotFound or DwaineVfsResult.BrokenLink => DwaineServiceStatus.NotFound,
            DwaineVfsResult.AlreadyExists => DwaineServiceStatus.Conflict,
            DwaineVfsResult.NodeLimit or DwaineVfsResult.ChildLimit or DwaineVfsResult.DataLimit =>
                DwaineServiceStatus.CapacityReached,
            DwaineVfsResult.InvalidPath or DwaineVfsResult.InvalidName or DwaineVfsResult.RootEscape =>
                DwaineServiceStatus.InvalidArguments,
            _ => DwaineServiceStatus.FileSystemFailure,
        };
        return DwaineServiceResponse.Failure(status, $"{service}: {result.ToString().ToLowerInvariant()}\n");
    }

    private static DwaineServiceResponse Denied(string service)
        => DwaineServiceResponse.Failure(DwaineServiceStatus.AccessDenied, $"{service}: permission denied\n");

    private static DwaineServiceResponse Invalid(string service)
        => DwaineServiceResponse.Failure(DwaineServiceStatus.InvalidArguments, $"{service}: invalid arguments\n");

    private static bool IsOperator(DwaineIdentityStore identities, DwainePrincipalId principal)
        => identities.IsInGroup(principal, DwaineGroupId.Operators);

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return string.Empty;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? value.ToLowerInvariant()
            : string.Empty;
    }

    private static bool ValidArgument(string? value)
        => value is not null
           && value.Length <= DwaineServiceSuiteComponent.HardMaxMailBodyCharacters
           && value.IndexOf('\0') < 0;

    private static string StatusName(DwaineServiceStatus status)
        => status.ToString().ToLowerInvariant();

    private sealed class ServiceKernelLease(DwaineServiceSystem system, EntityUid mainframe, ulong generation)
        : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }
}
