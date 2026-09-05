// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Storage;
using Content.Server._Whiskey.Dwaine.Syscalls;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Server._Whiskey.VodkaCode.Runtime;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Identity;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineSyscallTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineSyscallTestMainframe
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
            postDurationSeconds: 0.01
            bootloaderDurationSeconds: 0.01
            kernelInitializationDurationSeconds: 0.01
            shutdownDurationSeconds: 0.01
          - type: DwaineKernelRuntime
          - type: DwaineFileSystem
            maxNodes: 512
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 32
            maxProcessesPerOwner: 24
            maxDispatchesPerUpdate: 32
            instructionsPerSlice: 128
            instructionsPerProcess: 100000
            mailboxMessageLimit: 16
            mailboxCharacterLimit: 16384
          - type: DwaineProcessRuntime
          - type: DwaineIdentity
            maxAccounts: 16
            maxGroups: 8
            maxSessions: 8
            sessionLifetimeSeconds: 300
          - type: DwaineIdentityRuntime
          - type: VodkaRuntime
            maxInstructionsPerInvocation: 10000
            maxVariables: 128
            maxDataBytes: 16384
            maxOutputBytes: 8192
            logicalTimeoutSeconds: 30
          - type: VodkaRuntimeState
          - type: DwaineStorageConnector
            slotCount: 2
          - type: DwaineStorageDrive
          - type: DwaineStorageRuntime
          - type: DwaineNetworkConnector
            networkId: syscall-test
            linkRange: 10
          - type: DwaineDeviceBusEndpoint
            busId: syscall-test
          - type: DwaineDeviceAbi
            maxAttachedDevices: 16
            maxHandles: 32
            maxHandlesPerProcess: 8
            scanCooldownSeconds: 0.1
          - type: DwaineDeviceAbiRuntime
          - type: DwaineSyscall
          - type: DwaineSyscallRuntime

        - type: entity
          id: WhiskeyDwaineSyscallTestTerminal
          components:
          - type: Transform
          - type: DwaineComputerHardware
            kind: Terminal
            requiresExternalPower: false
          - type: DwaineHardwareRuntime
          - type: DwaineTerminalLink
          - type: DwaineTerminal
          - type: DwaineKeyboardInput
          - type: DwaineNetworkConnector
            networkId: syscall-test
            linkRange: 10
          - type: UserInterface
            interfaces:
              enum.DwaineTerminalUiKey.Key:
                type: DwaineTerminalBoundUserInterface
                requireInputValidation: false

        - type: entity
          id: WhiskeyDwaineSyscallTestDevice
          components:
          - type: Transform
          - type: DwaineDeviceBusEndpoint
            busId: syscall-test
          - type: DwaineDevice
            driverId: test-sensor
            address: sensor-a
            tag: sensor
            displayName: test sensor
            capabilities: Inspect, Message
            access: Public

        - type: entity
          id: WhiskeyDwaineSyscallTestActor
          components:
          - type: Transform
        """;

    [Test]
    public async Task SyscallsCapabilitiesVodkaForkAndTypedMessagesAreAuthoritative()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        EntityUid terminal = EntityUid.Invalid;
        EntityUid operatorTerminal = EntityUid.Invalid;
        EntityUid actor = EntityUid.Invalid;
        EntityUid operatorActor = EntityUid.Invalid;
        EntityUid device = EntityUid.Invalid;
        EntityUid media = EntityUid.Invalid;
        DwaineSessionId transportSession = default;
        DwaineSessionId operatorTransportSession = default;
        DwainePrincipalId principal = default;
        DwainePrincipalId operatorPrincipal = default;
        DwaineProcessId parent = default;
        DwaineProcessId peer = default;
        DwaineProcessId killChild = default;
        DwaineProcessId breakChild = default;
        DwaineProcessId operatorProcess = default;
        DwaineWorkingDirectoryHandle homeDirectory = default;
        DwaineDeviceHandle sensorHandle = default;
        DwaineProcessId script = default;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var origin = new MapCoordinates(Vector2.Zero, mapId);
            mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestMainframe", origin);
            terminal = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestTerminal", origin);
            operatorTerminal = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestTerminal", origin);
            actor = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestActor", origin);
            operatorActor = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestActor", origin);
            device = Server.EntMan.SpawnEntity("WhiskeyDwaineSyscallTestDevice", origin);
            media = Server.EntMan.SpawnEntity("WhiskeyDwaineRemovableDisk", origin);
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var transport = Server.System<DwaineTerminalTransportSystem>();
            var identities = Server.System<DwaineIdentitySystem>();
            var processes = Server.System<DwaineProcessSystem>();
            var devices = Server.System<DwaineDeviceSystem>();
            var storage = Server.System<DwaineStorageSystem>();
            var ui = Server.System<SharedUserInterfaceSystem>();

            Assert.That(ui.TryOpenUi(terminal, DwaineTerminalUiKey.Key, actor), Is.True);
            Assert.That(ui.TryOpenUi(operatorTerminal, DwaineTerminalUiKey.Key, operatorActor), Is.True);
            Assert.That(transport.TryConnect(terminal, mainframe, actor, out transportSession),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(transport.TryConnect(operatorTerminal, mainframe, operatorActor, out operatorTransportSession),
                Is.EqualTo(DwaineConnectResult.Connected));
            Assert.That(identities.TryGetSession(mainframe, transportSession, out var identity),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identity.Temporary, Is.True);
            Assert.That(identities.TryGetStore(mainframe, out var identityStore), Is.True);
            Assert.That(identityStore.TryCreateAccount("alex", "safe-password", false, out _),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(identityStore.TryCreateAccount("operator", "operator-password", true, out var operatorAccount),
                Is.EqualTo(DwaineIdentityResult.Success));
            operatorPrincipal = operatorAccount.Principal;
            Assert.That(identities.TryLogin(mainframe, transportSession, "alex", "safe-password", out var login),
                Is.EqualTo(DwaineIdentityResult.Success));
            principal = login.Principal;
            Assert.That(identities.TryLogin(mainframe, operatorTransportSession, "operator", "operator-password", out var operatorLogin),
                Is.EqualTo(DwaineIdentityResult.Success));
            Assert.That(operatorLogin.Principal, Is.EqualTo(operatorPrincipal));
            Assert.That(devices.TryAttachLocal(mainframe, device), Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(storage.TryInsert(mainframe, media, 0).Succeeded, Is.True);

            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var files), Is.True);
            const string homePath = "/home/alex";
            Assert.That(files.TryCreateDirectory(homePath, files.Root, TimeSpan.Zero, out var home),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(files.TrySetMetadata(home, principal.Value, DwaineGroupId.Users.Value,
                DwaineVfsMode.OwnerAll, TimeSpan.Zero), Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(files.TryCreateDirectory("/mnt/disk", files.Root, TimeSpan.Zero, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(files.TryCreate("/conf/syscall.conf", files.Root, new DwaineVfsCreateRequest
            {
                Kind = DwaineVfsNodeKind.Text,
                Owner = DwainePrincipalId.System.Value,
                Group = DwaineGroupId.System.Value,
                Mode = DwaineVfsMode.ReadOnlyFile,
                Flags = DwaineVfsNodeFlags.ReadOnly | DwaineVfsNodeFlags.System,
                Text = "authoritative=true",
            }, TimeSpan.Zero, out _), Is.EqualTo(DwaineVfsResult.Success));
            homeDirectory = DwaineFileSystemSystem.ToWorkingDirectory(home);

            const string childSource = "sys.terminal.write(\"spawned child\");";
            const string mainSource = """
                console.writeln(sys.device.scan());
                console.writeln(sys.device.list());
                let sensor = sys.device.get("sensor-a");
                console.writeln(sys.device.message(sensor, "status", ""));
                sys.terminal.write("vodka terminal output");
                sys.file.write("result.txt", "written", false, false);
                console.writeln(sys.file.read("result.txt"));
                console.writeln(sys.process.spawn("child.vodka"));
                let forked = sys.process.fork();
                if (forked == 0) { sys.process.exit(0); }
                console.writeln(forked);
                """;
            CreateProgram(files, home, "child.vodka", "child", childSource, principal);
            CreateProgram(files, home, "main.vodka", "main", mainSource, principal);

            var request = new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(principal.Value),
                Program = new DwaineProgramDescriptor("test.parent", "test parent"),
                Implementation = new HoldProgram(),
                WorkingDirectory = homeDirectory,
                TerminalSession = new DwaineProcessTerminalSession(transportSession.Value),
            };
            Assert.That(processes.TrySpawn(mainframe, request, out parent), Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, request.WithImplementation(new HoldProgram()), out peer),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(principal.Value),
                ParentId = parent,
                Program = new DwaineProgramDescriptor("test.kill-child", "test kill child"),
                Implementation = new HoldProgram(),
                WorkingDirectory = homeDirectory,
                TerminalSession = new DwaineProcessTerminalSession(transportSession.Value),
            }, out killChild), Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(principal.Value),
                ParentId = parent,
                Program = new DwaineProgramDescriptor("test.break-child", "test break child"),
                Implementation = new HoldProgram(),
                WorkingDirectory = homeDirectory,
                TerminalSession = new DwaineProcessTerminalSession(transportSession.Value),
            }, out breakChild), Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(operatorPrincipal.Value),
                Program = new DwaineProgramDescriptor("test.operator", "test operator"),
                Implementation = new HoldProgram(),
                WorkingDirectory = DwaineWorkingDirectoryHandle.Root,
                TerminalSession = new DwaineProcessTerminalSession(operatorTransportSession.Value),
            }, out operatorProcess), Is.EqualTo(DwaineProcessSpawnResult.Success));
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var syscalls = Server.System<DwaineSyscallSystem>();
            var processes = Server.System<DwaineProcessSystem>();

            var userList = syscalls.Execute(mainframe, parent, DwaineSyscallId.UserList, []);
            Assert.Multiple(() =>
            {
                Assert.That(userList.Status, Is.EqualTo(DwaineSyscallStatus.Success));
                Assert.That(userList.Value.Text, Does.Contain("alex"));
            });
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.UserLogin,
            [
                DwaineSyscallValue.FromString("alex"),
                DwaineSyscallValue.FromString("wrong-password"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.UserLogin,
            [
                DwaineSyscallValue.FromString("alex"),
                DwaineSyscallValue.FromString("safe-password"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.RateLimited));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.UserInput,
            [
                DwaineSyscallValue.FromString("forged input"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.TryDeliverTrustedInput(mainframe, terminal, "pwd"),
                Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.UserMessage,
            [
                DwaineSyscallValue.FromString("operator"),
                DwaineSyscallValue.FromString("hello from syscall"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            var terminalRuntime = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe);
            Assert.That(terminalRuntime.Sessions[operatorTransportSession].Output.Snapshot(),
                Has.Some.Contains("message from alex: hello from syscall"));

            var taskList = syscalls.Execute(mainframe, parent, DwaineSyscallId.TaskList, []);
            Assert.Multiple(() =>
            {
                Assert.That(taskList.Status, Is.EqualTo(DwaineSyscallStatus.Success));
                Assert.That(taskList.Value.Text, Does.Contain($"{killChild.Value}\t"));
                Assert.That(taskList.Value.Text, Does.Contain($"{breakChild.Value}\t"));
            });
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.TaskKill,
            [
                DwaineSyscallValue.FromInteger((long) killChild.Value),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.TaskKill,
            [
                DwaineSyscallValue.FromInteger((long) operatorProcess.Value),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.TryBreak(mainframe, parent, breakChild), Is.EqualTo(DwaineSyscallStatus.Success));

            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileWrite,
            [
                DwaineSyscallValue.FromString("syscall.txt"),
                DwaineSyscallValue.FromString("one"),
                DwaineSyscallValue.FromBoolean(false),
                DwaineSyscallValue.FromBoolean(false),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileWrite,
            [
                DwaineSyscallValue.FromString("syscall.txt"),
                DwaineSyscallValue.FromString("-two"),
                DwaineSyscallValue.FromBoolean(true),
                DwaineSyscallValue.FromBoolean(false),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            var file = syscalls.Execute(mainframe, parent, DwaineSyscallId.FileGet,
            [
                DwaineSyscallValue.FromString("syscall.txt"),
            ]);
            Assert.Multiple(() =>
            {
                Assert.That(file.Status, Is.EqualTo(DwaineSyscallStatus.Success));
                Assert.That(file.Value.Text, Is.EqualTo("one-two"));
            });
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileMode,
            [
                DwaineSyscallValue.FromString("syscall.txt"),
                DwaineSyscallValue.FromInteger((long) DwaineVfsMode.OwnerAll),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileOwner,
            [
                DwaineSyscallValue.FromString("syscall.txt"),
                DwaineSyscallValue.FromString("operator"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.Execute(mainframe, operatorProcess, DwaineSyscallId.FileOwner,
            [
                DwaineSyscallValue.FromString("/home/alex/syscall.txt"),
                DwaineSyscallValue.FromString("operator"),
                DwaineSyscallValue.FromString("operators"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            var configuration = syscalls.Execute(mainframe, parent, DwaineSyscallId.ConfigurationGet,
            [
                DwaineSyscallValue.FromString("syscall.conf"),
            ]);
            Assert.Multiple(() =>
            {
                Assert.That(configuration.Status, Is.EqualTo(DwaineSyscallStatus.Success));
                Assert.That(configuration.Value.Text, Is.EqualTo("authoritative=true"));
            });
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileWrite,
            [
                DwaineSyscallValue.FromString("delete-me.txt"),
                DwaineSyscallValue.FromString("temporary"),
                DwaineSyscallValue.FromBoolean(false),
                DwaineSyscallValue.FromBoolean(false),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.TrySendFileNotification(mainframe, parent, peer, "delete-me.txt"),
                Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(processes.TryReceiveMessage(mainframe, peer, out var receivedFile), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(receivedFile.Type, Is.EqualTo("kernel.receive-file"));
                Assert.That(receivedFile.Payload, Is.EqualTo("delete-me.txt\ntemporary"));
            });
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.FileKill,
            [
                DwaineSyscallValue.FromString("delete-me.txt"),
                DwaineSyscallValue.FromBoolean(false),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));

            var scan = syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceScan, []);
            Assert.That(scan.Status, Is.EqualTo(DwaineSyscallStatus.Success));
            var list = syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceList, []);
            Assert.Multiple(() =>
            {
                Assert.That(list.Status, Is.EqualTo(DwaineSyscallStatus.Success));
                Assert.That(list.Value.Text, Does.Contain("sensor-a"));
                Assert.That(list.Value.Text, Does.Contain("user-terminal"));
                Assert.That(list.Value.Text, Does.Contain("storage-media"));
            });

            var acquired = syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceGet,
            [
                DwaineSyscallValue.FromString("sensor-a"),
                DwaineSyscallValue.FromInteger((long) (DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message)),
            ]);
            Assert.That(acquired.Status, Is.EqualTo(DwaineSyscallStatus.Success));
            sensorHandle = acquired.Value.DeviceHandle;
            var status = syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceMessage,
            [
                DwaineSyscallValue.FromDeviceHandle(sensorHandle),
                DwaineSyscallValue.FromString("status"),
                DwaineSyscallValue.FromString(""),
            ]);
            Assert.That(status.Value.Text, Is.EqualTo("ready"));
            Assert.That(syscalls.Execute(mainframe, peer, DwaineSyscallId.DeviceMessage,
            [
                DwaineSyscallValue.FromDeviceHandle(sensorHandle),
                DwaineSyscallValue.FromString("status"),
                DwaineSyscallValue.FromString(""),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceMessage,
            [
                DwaineSyscallValue.FromDeviceHandle(new DwaineDeviceHandle(ulong.MaxValue)),
                DwaineSyscallValue.FromString("status"),
                DwaineSyscallValue.FromString(""),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.StaleHandle));

            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.TaskExitMessage, []).Status,
                Is.EqualTo(DwaineSyscallStatus.UnknownCall));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.UserGroup,
            [
                DwaineSyscallValue.FromString("alex"),
                DwaineSyscallValue.FromString("operators"),
                DwaineSyscallValue.FromBoolean(true),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));

            var mediaAddress = list.Value.Text.Split('\n')
                .Select(line => line.Split('\t'))
                .Single(parts => parts.Length >= 3 && parts[2] == "storage-media")[0];
            var storageHandle = syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceGet,
            [
                DwaineSyscallValue.FromString(mediaAddress),
                DwaineSyscallValue.FromInteger((long) (DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Mount)),
            ]);
            Assert.That(storageHandle.Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.Mount,
            [
                DwaineSyscallValue.FromDeviceHandle(storageHandle.Value.DeviceHandle),
                DwaineSyscallValue.FromString("/mnt/disk"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            var operatorStorageHandle = syscalls.Execute(mainframe, operatorProcess, DwaineSyscallId.DeviceGet,
            [
                DwaineSyscallValue.FromString(mediaAddress),
                DwaineSyscallValue.FromInteger((long) (DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Mount)),
            ]);
            Assert.That(operatorStorageHandle.Status, Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.Execute(mainframe, operatorProcess, DwaineSyscallId.Mount,
            [
                DwaineSyscallValue.FromDeviceHandle(operatorStorageHandle.Value.DeviceHandle),
                DwaineSyscallValue.FromString("/mnt/disk"),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.Success));

            var started = Server.System<VodkaRuntimeSystem>().TryStart(
                mainframe,
                principal,
                parent,
                homeDirectory,
                "main.vodka",
                [],
                true);
            Assert.That(started.Succeeded, Is.True, started.Error);
            script = started.ProcessId;
        });

        await Server.WaitRunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var vodka = Server.System<VodkaRuntimeSystem>();
            Assert.That(vodka.TryTakeCapturedOutput(mainframe, script, out var output), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(output.ExitCode, Is.Zero, output.StandardError);
                Assert.That(output.StandardError, Is.Empty);
                Assert.That(output.StandardOutput, Does.Contain("sensor-a"));
                Assert.That(output.StandardOutput, Does.Contain("ready"));
                Assert.That(output.StandardOutput, Does.Contain("written"));
            });
            var transportRuntime = Server.EntMan.GetComponent<DwaineMainframeRuntimeComponent>(mainframe);
            Assert.That(transportRuntime.Sessions[transportSession].Output.Snapshot(),
                Has.Some.Contains("vodka terminal output"));
            Assert.That(transportRuntime.Sessions[transportSession].Output.Snapshot(),
                Has.Some.Contains("spawned child"));

            var fileSystems = Server.System<DwaineFileSystemSystem>();
            Assert.That(fileSystems.TryGetFileSystem(mainframe, out var files), Is.True);
            Assert.That(files.TryReadText("result.txt", DwaineFileSystemSystem.ToVfsHandle(homeDirectory), out var text),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(text, Is.EqualTo("written"));

            var processes = Server.System<DwaineProcessSystem>();
            var childProgram = new ExitProgram();
            Assert.That(processes.TrySpawn(mainframe, new DwaineProcessSpawnRequest
            {
                Owner = new DwaineProcessOwner(principal.Value),
                ParentId = parent,
                Program = new DwaineProgramDescriptor("test.exit", "test exit"),
                Implementation = childProgram,
                WorkingDirectory = homeDirectory,
            }, out _), Is.EqualTo(DwaineProcessSpawnResult.Success));
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var exitNotifications = 0;
            while (processes.TryReceiveMessage(mainframe, parent, out var exited))
            {
                Assert.That(exited.Type, Is.EqualTo("kernel.task-exit"));
                exitNotifications++;
            }
            Assert.That(exitNotifications, Is.GreaterThanOrEqualTo(1));

            var syscalls = Server.System<DwaineSyscallSystem>();
            Assert.That(syscalls.TryOpenReply(mainframe, parent, peer, out var correlation),
                Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(syscalls.TryReply(mainframe, parent, correlation, "forged"),
                Is.EqualTo(DwaineSyscallStatus.AccessDenied));
            Assert.That(syscalls.TryReply(mainframe, peer, correlation, "accepted"),
                Is.EqualTo(DwaineSyscallStatus.Success));
            Assert.That(processes.TryReceiveMessage(mainframe, parent, out var reply), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reply.Type, Is.EqualTo("kernel.reply"));
                Assert.That(reply.Payload, Does.EndWith(":accepted"));
            });

            Server.EntMan.DeleteEntity(device);
            Assert.That(syscalls.Execute(mainframe, parent, DwaineSyscallId.DeviceMessage,
            [
                DwaineSyscallValue.FromDeviceHandle(sensorHandle),
                DwaineSyscallValue.FromString("status"),
                DwaineSyscallValue.FromString(""),
            ]).Status, Is.EqualTo(DwaineSyscallStatus.StaleHandle));
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProductionMainframeComposesSyscallAndDeviceAbiRuntimes()
    {
        await Server.WaitAssertion(() =>
        {
            var mainframe = Server.EntMan.SpawnEntity("WhiskeyDwaineMainframe", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(Server.EntMan.HasComponent<DwaineDeviceAbiComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineDeviceAbiRuntimeComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<Content.Shared._Whiskey.Dwaine.Syscalls.DwaineSyscallComponent>(mainframe), Is.True);
                Assert.That(Server.EntMan.HasComponent<DwaineSyscallRuntimeComponent>(mainframe), Is.True);
            });
            Server.EntMan.DeleteEntity(mainframe);
        });
    }

    private static void CreateProgram(
        DwaineVirtualFileSystem files,
        DwaineVfsNodeHandle directory,
        string name,
        string id,
        string source,
        DwainePrincipalId principal)
    {
        Assert.That(files.TryCreate(name, directory, new DwaineVfsCreateRequest
        {
            Kind = DwaineVfsNodeKind.Program,
            Owner = principal.Value,
            Group = DwaineGroupId.Users.Value,
            Mode = DwaineVfsMode.OwnerAll,
            Program = new DwaineVfsProgramData(id, source, true, false),
        }, TimeSpan.Zero, out _), Is.EqualTo(DwaineVfsResult.Success));
    }

    private sealed class HoldProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
            => DwaineProcessStepResult.Yield();
    }

    private sealed class ExitProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
            => DwaineProcessStepResult.Exit(7);
    }
}

file static class DwaineSyscallTestRequestExtensions
{
    public static DwaineProcessSpawnRequest WithImplementation(
        this DwaineProcessSpawnRequest request,
        IDwaineProcessProgram implementation)
        => new()
        {
            Owner = request.Owner,
            Program = request.Program,
            Implementation = implementation,
            ParentId = request.ParentId,
            WorkingDirectory = request.WorkingDirectory,
            TerminalSession = request.TerminalSession,
            Environment = request.Environment,
            InheritParentEnvironment = request.InheritParentEnvironment,
        };
}
