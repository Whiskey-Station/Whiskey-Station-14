// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Services;
using Content.Server._Whiskey.VodkaCode.Runtime;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Services;
using Content.Shared._Whiskey.Dwaine.Syscalls;
using Content.Shared._Whiskey.VodkaCode;
using Content.Shared._Whiskey.Dwaine.Network;
using Content.Shared.Paper;
using Content.Shared.Cargo.Components;
using Content.Shared.CriminalRecords;
using Content.Shared.Security;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Components;
using Content.Shared.StationRecords.Systems;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineServiceTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineServiceTestMainframe
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Mainframe
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineMainframe
          - type: DwaineMainframeRuntime
          - type: DwaineKernel
            autoBoot: false
            requireStorageConnector: false
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineFileSystem
            maxNodes: 128
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 16
            maxProcessesPerOwner: 8
            maxDispatchesPerUpdate: 16
          - type: DwaineProcessRuntime
          - type: DwaineIdentity
            maxAccounts: 16
            maxGroups: 8
            maxSessions: 8
          - type: DwaineIdentityRuntime
          - type: DwaineServiceSuite
            maxMailMessages: 4
            maxMailPerUser: 2
            maxMailSubjectCharacters: 32
            maxMailBodyCharacters: 64
            maxLogEntries: 8
            maxServiceOutputCharacters: 512
          - type: DwaineServiceRuntime
          - type: DwaineStationServiceBridge
          - type: StationTracker
          - type: DwaineNetworkConnector
            networkId: service-test
            address: service-mainframe
            adapter: Radio
            tags: [mainframe]
            linkRange: 10
          - type: DwaineNetworkEndpoint
          - type: DwaineDeviceAbi
            maxAttachedDevices: 8
            maxHandles: 16
            maxHandlesPerProcess: 4
            scanCooldownSeconds: 0.1
          - type: DwaineDeviceAbiRuntime
          - type: DwaineSyscall
          - type: DwaineSyscallRuntime
          - type: VodkaRuntime
            maxInstructionsPerInvocation: 10000
            maxOutputBytes: 4096
          - type: VodkaRuntimeState

        - type: entity
          id: WhiskeyDwaineServiceTestPrinter
          components:
          - type: Transform
          - type: DwainePrinter
            maxDocumentCharacters: 64
          - type: DwaineNetworkConnector
            networkId: service-test
            address: service-printer
            adapter: Radio
            tags: [device, printer]
            linkRange: 10
          - type: DwaineNetworkEndpoint
          - type: DwaineDevice
            driverId: printer
            address: printer-test
            tag: printer
            displayName: test printer
            capabilities: Inspect, Message
            access: Authenticated

        - type: entity
          id: WhiskeyDwaineServiceTestStation
          name: service test station
          components:
          - type: Transform
          - type: StationData
          - type: StationBankAccount
          - type: StationRecords
          - type: StationJobs
            availableJobs: {}

        - type: entity
          parent: APCBasic
          id: WhiskeyDwaineServiceTestApc
          components:
          - type: DwaineApcInterface
          - type: DwaineNetworkConnector
            networkId: service-test
            address: service-apc
            adapter: Radio
            tags: [device, apc]
            linkRange: 10
          - type: DwaineNetworkEndpoint
          - type: DwaineDevice
            driverId: apc
            address: apc-test
            tag: apc
            displayName: test APC
            capabilities: Inspect, Message
            access: Operator
        """;

    [Test]
    public async Task ServicesRevalidateCallersEnforceBoundsAndPersistAcrossReboot()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineAccountSnapshot alice = default;
        DwaineAccountSnapshot bob = default;
        DwaineAccountSnapshot root = default;
        DwaineProcessId aliceProcess = default;
        DwaineProcessId bobProcess = default;
        DwaineProcessId rootProcess = default;
        EntityUid printer = EntityUid.Invalid;
        EntityUid station = EntityUid.Invalid;
        EntityUid apc = EntityUid.Invalid;
        DwaineProcessId script = default;

        await Server.WaitAssertion(() =>
        {
            map = Server.System<SharedMapSystem>().CreateMap(out var mapId);
            station = Server.EntMan.SpawnEntity("WhiskeyDwaineServiceTestStation", MapCoordinates.Nullspace);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineServiceTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            Server.System<StationSystem>().SetStation(mainframe, station);
            Server.EntMan.GetComponent<StationJobsComponent>(station).JobList[
                new ProtoId<JobPrototype>("Passenger")] = 2;
            printer = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineServiceTestPrinter",
                new MapCoordinates(new Vector2(1, 0), mapId));
            apc = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineServiceTestApc",
                new MapCoordinates(new Vector2(2, 0), mapId));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });
        await Server.WaitRunTicks(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe), Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(Server.System<DwaineIdentitySystem>().TryGetStore(mainframe, out var identities), Is.True);
            Assert.That(identities.TryCreateAccount("alice", "alice-password", false, out alice), Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identities.TryCreateAccount("bob", "bob-password", false, out bob), Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identities.TryCreateAccount("root", "root-password", true, out root), Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(Server.System<DwaineFileSystemSystem>().TryGetFileSystem(mainframe, out var fileSystem), Is.True);
            Assert.That(fileSystem.TryCreate("/home/alice", fileSystem.Root, new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Directory,
                Owner = alice.Principal.Value,
                Group = DwaineGroupId.Users.Value,
                Mode = DwaineVfsMode.DefaultDirectory,
            }, Server.Timing.CurTime, out _), Is.EqualTo(DwaineVfsResult.Success));
            aliceProcess = Spawn(mainframe, alice, "alice-test");
            bobProcess = Spawn(mainframe, bob, "bob-test");
            rootProcess = Spawn(mainframe, root, "root-test");

            var stationRecords = Server.System<StationRecordsSystem>();
            var recordKey = stationRecords.AddRecordEntry<GeneralStationRecord>((station, null), new GeneralStationRecord
            {
                Name = "Audit Subject",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
                JobTitle = "Engineer",
            });
            Assert.That(recordKey.IsValid(), Is.True);
            stationRecords.AddRecordEntry(recordKey, new CriminalRecord
            {
                Status = SecurityStatus.Wanted,
                Reason = "bounded test reason",
            });

            var services = Server.System<DwaineServiceSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(services.ListServices(mainframe, aliceProcess, alice.Principal).Output,
                    Does.Contain("email"));
                Assert.That(services.ListServices(mainframe, aliceProcess, alice.Principal).Output,
                    Does.Contain("records"));
                Assert.That(services.ListServices(mainframe, aliceProcess, alice.Principal).Output,
                    Does.Contain("jobs"));
                Assert.That(services.Call(mainframe, aliceProcess, bob.Principal, "email", "list", [],
                    DwaineVfsNodeHandle.Root).Status, Is.EqualTo(DwaineServiceStatus.AccessDenied));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "email", "send",
                    ["bob", "hello", "bounded body"], DwaineVfsNodeHandle.Root).Status,
                    Is.EqualTo(DwaineServiceStatus.Success));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "email", "send",
                    ["group:users", "denied", "broadcast"], DwaineVfsNodeHandle.Root).Status,
                    Is.EqualTo(DwaineServiceStatus.AccessDenied));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "documents", "write",
                    ["/home/alice/note", "persistent", "document"], DwaineVfsNodeHandle.Root).Status,
                    Is.EqualTo(DwaineServiceStatus.Success));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "email", "send",
                    ["bob", "too-long", new string('x', 65)], DwaineVfsNodeHandle.Root).Status,
                    Is.EqualTo(DwaineServiceStatus.InvalidArguments));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "records", "security", [],
                    DwaineVfsNodeHandle.Root).Status, Is.EqualTo(DwaineServiceStatus.AccessDenied));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "bank", "balance", [],
                    DwaineVfsNodeHandle.Root).Status, Is.EqualTo(DwaineServiceStatus.AccessDenied));
                Assert.That(services.Call(mainframe, aliceProcess, alice.Principal, "jobs", "list", [],
                    DwaineVfsNodeHandle.Root).Status, Is.EqualTo(DwaineServiceStatus.AccessDenied));
            });

            var medical = services.Call(mainframe, rootProcess, root.Principal, "records", "medical", [],
                DwaineVfsNodeHandle.Root);
            var security = services.Call(mainframe, rootProcess, root.Principal, "records", "security", [],
                DwaineVfsNodeHandle.Root);
            var balance = services.Call(mainframe, rootProcess, root.Principal, "bank", "balance", ["Cargo"],
                DwaineVfsNodeHandle.Root);
            var transfer = services.Call(mainframe, rootProcess, root.Principal, "bank", "transfer",
                ["Cargo", "Medical", "10"], DwaineVfsNodeHandle.Root);
            var manifest = services.Call(mainframe, rootProcess, root.Principal, "manifest", "list", [],
                DwaineVfsNodeHandle.Root);
            var jobs = services.Call(mainframe, rootProcess, root.Principal, "jobs", "list", [],
                DwaineVfsNodeHandle.Root);
            var updateJobs = services.Call(mainframe, rootProcess, root.Principal, "jobs", "set", ["Passenger", "3"],
                DwaineVfsNodeHandle.Root);
            Assert.Multiple(() =>
            {
                Assert.That(medical.Output, Does.Contain("Audit Subject"));
                Assert.That(security.Output, Does.Contain("wanted"));
                Assert.That(security.Output, Does.Contain("bounded test reason"));
                Assert.That(balance.Output, Does.Contain("Cargo"));
                Assert.That(transfer.Status, Is.EqualTo(DwaineServiceStatus.Success));
                Assert.That(manifest.Status, Is.EqualTo(DwaineServiceStatus.Success));
                Assert.That(jobs.Output, Does.Contain("Passenger\t2"));
                Assert.That(updateJobs.Status, Is.EqualTo(DwaineServiceStatus.Success));
                Assert.That(Server.EntMan.GetComponent<StationJobsComponent>(station).JobList[
                    new ProtoId<JobPrototype>("Passenger")], Is.EqualTo(3));
                Assert.That(Server.EntMan.GetComponent<StationBankAccountComponent>(station).Accounts["Cargo"],
                    Is.EqualTo(9990));
            });

            var inbox = services.Call(mainframe, bobProcess, bob.Principal, "email", "list", [], DwaineVfsNodeHandle.Root);
            Assert.That(inbox.Output, Does.Contain("hello"));
            var diagnostics = services.Call(mainframe, aliceProcess, alice.Principal, "diagnostics", "snapshot", [],
                DwaineVfsNodeHandle.Root);
            Assert.That(diagnostics.Status, Is.EqualTo(DwaineServiceStatus.AccessDenied));

            Assert.That(fileSystem.TryCreate("/home/alice/service.vodka", fileSystem.Root, new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Program,
                Owner = alice.Principal.Value,
                Group = DwaineGroupId.Users.Value,
                Mode = DwaineVfsMode.OwnerAll,
                Program = new DwaineVfsProgramData(
                    "service-test",
                    "console.write(sys.service.call(\"documents\", \"read\", \"/home/alice/note\"));",
                    true,
                    false),
            }, Server.Timing.CurTime, out _), Is.EqualTo(DwaineVfsResult.Success));
            var started = Server.System<VodkaRuntimeSystem>().TryStart(
                mainframe,
                alice.Principal,
                aliceProcess,
                DwaineWorkingDirectoryHandle.Root,
                "/home/alice/service.vodka",
                [],
                true);
            Assert.That(started.Succeeded, Is.True, started.Error);
            script = started.ProcessId;

            var devices = Server.System<DwaineDeviceSystem>();
            Assert.That(devices.TryScan(mainframe, aliceProcess, alice.Principal, out var count),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(count, Is.GreaterThanOrEqualTo(1));
            Assert.That(devices.TryAcquire(
                    mainframe,
                    aliceProcess,
                    alice.Principal,
                    "printer-test",
                    DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message,
                    out var handle),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(devices.TryMessage(mainframe, aliceProcess, alice.Principal, handle, "print", "audit page").Result,
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(Server.EntMan.EntityQuery<PaperComponent>().Any(paper => paper.Content == "audit page"), Is.True);
            Assert.That(devices.TryScan(mainframe, rootProcess, root.Principal, out _),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(devices.TryAcquire(
                    mainframe,
                    rootProcess,
                    root.Principal,
                    "apc-test",
                    DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message,
                    out var apcHandle),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(devices.TryMessage(mainframe, rootProcess, root.Principal, apcHandle, "inspect", string.Empty).Result,
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(devices.TryMessage(mainframe, rootProcess, root.Principal, apcHandle, "breaker", "off").Payload,
                Is.EqualTo("off"));
            Assert.That(devices.TryMessage(mainframe, rootProcess, root.Principal, apcHandle, "breaker", "on").Payload,
                Is.EqualTo("on"));
            Server.EntMan.DeleteEntity(printer);
            Assert.That(devices.TryMessage(mainframe, aliceProcess, alice.Principal, handle, "print", "stale").Result,
                Is.EqualTo(DwaineDeviceResult.StaleHandle));
        });

        await Server.WaitRunTicks(15);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<VodkaRuntimeSystem>().TryTakeCapturedOutput(mainframe, script, out var output), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(output.ExitCode, Is.Zero, output.StandardError);
                Assert.That(output.StandardError, Is.Empty);
                Assert.That(output.StandardOutput, Is.EqualTo("persistent document\n"));
            });
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().TryShutdown(mainframe), Is.True);
        });
        await Server.WaitRunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineServiceSystem>().ListServices(mainframe, bobProcess, bob.Principal).Status,
                Is.EqualTo(DwaineServiceStatus.AccessDenied));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });
        await Server.WaitRunTicks(8);

        await Server.WaitAssertion(() =>
        {
            bobProcess = Spawn(mainframe, bob, "bob-after-reboot");
            var services = Server.System<DwaineServiceSystem>();
            var inbox = services.Call(mainframe, bobProcess, bob.Principal, "email", "list", [], DwaineVfsNodeHandle.Root);
            var document = services.Call(mainframe, bobProcess, bob.Principal, "documents", "read", ["/home/alice/note"],
                DwaineVfsNodeHandle.Root);
            Assert.Multiple(() =>
            {
                Assert.That(inbox.Output, Does.Contain("hello"), "mail store must survive a controlled kernel reboot");
                Assert.That(document.Output, Is.EqualTo("persistent document\n"));
                Assert.That(Server.System<DwaineProcessSystem>().TryGetProcess(mainframe, aliceProcess, out _), Is.False,
                    "reboot must not preserve stale caller processes");
                Assert.That(services.GetMetrics(mainframe).LogEntries, Is.LessThanOrEqualTo(8));
            });
            Server.EntMan.DeleteEntity(map);
            Server.EntMan.DeleteEntity(station);
            Server.EntMan.DeleteEntity(apc);
        });
    }

    private DwaineProcessId Spawn(EntityUid mainframe, DwaineAccountSnapshot account, string id)
    {
        var result = Server.System<DwaineProcessSystem>().TrySpawn(
            mainframe,
            new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(account.Principal.Value),
                Program = new DwaineProgramDescriptor(id, id),
                Implementation = new WaitingProgram(),
                WorkingDirectory = DwaineWorkingDirectoryHandle.Root,
            },
            out var process);
        Assert.That(result, Is.EqualTo(DwaineProcessSpawnResult.Success));
        return process;
    }

    private sealed class WaitingProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
            => DwaineProcessStepResult.WaitForInput();
    }
}
