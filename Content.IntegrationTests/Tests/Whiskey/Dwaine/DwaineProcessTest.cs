// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Process;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineProcessTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WhiskeyDwaineProcessTestMainframe
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
            maxNodes: 256
          - type: DwaineFileSystemRuntime
          - type: DwaineProcessScheduler
            maxProcesses: 8
            maxProcessesPerOwner: 4
            maxDispatchesPerUpdate: 8
            instructionsPerSlice: 8
            instructionsPerProcess: 16
            streamChunkLimit: 2
            streamCharacterLimit: 16
            mailboxMessageLimit: 2
            mailboxCharacterLimit: 32
            environmentEntryLimit: 4
            environmentCharacterLimit: 64
            completedProcessLimit: 16
            completedRetentionSeconds: 300
          - type: DwaineProcessRuntime
          - type: DwaineStorageConnector
            slotCount: 1
        """;

    [Test]
    public async Task CreationSchedulingStreamsMetadataAndPidUniqueness()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId echoId = default;
        DwaineProcessId holdId = default;

        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineProcessTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(
                        new DwaineProcessOwner(10),
                        "echo",
                        new EchoProgram(),
                        environment: new Dictionary<string, string> { ["USER"] = "ada" }),
                    out echoId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(10), "hold", new HoldProgram()),
                    out holdId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TryWriteInput(mainframe, echoId, "hello"), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(echoId.IsValid, Is.True);
                Assert.That(holdId.IsValid, Is.True);
                Assert.That(holdId, Is.Not.EqualTo(echoId));
                Assert.That(processes.GetActiveProcessCount(mainframe), Is.EqualTo(2));
                Assert.That(processes.TryGetProcess(mainframe, echoId, out var created), Is.True);
                Assert.That(created.State, Is.EqualTo(DwaineProcessState.Ready));
                Assert.That(created.ParentId, Is.Null);
                Assert.That(created.WorkingDirectory, Is.EqualTo(DwaineWorkingDirectoryHandle.Root));
            });
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(processes.TryGetProcess(mainframe, echoId, out var exited), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(exited.State, Is.EqualTo(DwaineProcessState.Exited));
                Assert.That(exited.ExitCode, Is.EqualTo(7));
                Assert.That(exited.ExitReason, Is.EqualTo(DwaineProcessExitReason.NormalExit));
                Assert.That(exited.InstructionsConsumed, Is.EqualTo(3));
                Assert.That(exited.StartedAt, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
                Assert.That(processes.TryReadOutput(mainframe, echoId, out var output), Is.True);
                Assert.That(output, Is.EqualTo("hello:ada"));
                Assert.That(processes.TryReadError(mainframe, echoId, out var error), Is.True);
                Assert.That(error, Is.EqualTo("diagnostic"));
                Assert.That(processes.GetEnvironmentSnapshot(mainframe, echoId)!["USER"], Is.EqualTo("ada"));
                Assert.That(processes.TryExit(mainframe, holdId), Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(10), echoId), Is.True);
                Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(10), holdId), Is.True);
                Assert.That(processes.GetActiveProcessCount(mainframe), Is.Zero);
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ParentChildWaitKillAndCleanupAreDeterministic()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId parentId = default;
        DwaineProcessId childId = default;
        var waiting = new WaitingProgram();

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var owner = new DwaineProcessOwner(20);
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "parent", waiting), out parentId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "child", new ExitAfterProgram(1, 23), parentId),
                    out childId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            waiting.Child = childId;
        });

        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(processes.TryGetProcess(mainframe, parentId, out var parent), Is.True);
                Assert.That(parent.State, Is.EqualTo(DwaineProcessState.Exited));
                Assert.That(parent.ExitCode, Is.EqualTo(23));
                Assert.That(waiting.ObservedResult?.ProcessId, Is.EqualTo(childId));
                Assert.That(waiting.ObservedResult?.ExitCode, Is.EqualTo(23));
                Assert.That(processes.TryGetProcess(mainframe, childId, out _), Is.False);
            });

            Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(20), parentId), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var owner = new DwaineProcessOwner(21);
            var childProgram = new HoldProgram();
            var grandchildProgram = new HoldProgram();
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "root", new HoldProgram()), out var root),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "child", childProgram, root),
                    out var child),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "grandchild", grandchildProgram, child),
                    out var grandchild),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(22), "unrelated", new HoldProgram()),
                    out var unrelated),
                Is.EqualTo(DwaineProcessSpawnResult.Success));

            Assert.Multiple(() =>
            {
                Assert.That(processes.TryKill(mainframe, unrelated, child),
                    Is.EqualTo(DwaineProcessControlResult.AccessDenied));
                Assert.That(processes.TryKill(mainframe, root, child),
                    Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryGetProcess(mainframe, child, out var killed), Is.True);
                Assert.That(killed.ExitReason, Is.EqualTo(DwaineProcessExitReason.Killed));
                Assert.That(childProgram.CancelCalls, Is.EqualTo(1));
                Assert.That(childProgram.LastCancellation, Is.EqualTo(DwaineProcessExitReason.Killed));
                Assert.That(processes.TryGetProcess(mainframe, grandchild, out var cascaded), Is.True);
                Assert.That(cascaded.ExitReason, Is.EqualTo(DwaineProcessExitReason.ParentExited));
                Assert.That(grandchildProgram.CancelCalls, Is.EqualTo(1));
                Assert.That(grandchildProgram.LastCancellation, Is.EqualTo(DwaineProcessExitReason.ParentExited));
                Assert.That(processes.TryReap(mainframe, owner, child), Is.True);
                Assert.That(processes.TryReap(mainframe, owner, grandchild), Is.True);
                Assert.That(processes.TryExit(mainframe, root), Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryExit(mainframe, unrelated), Is.EqualTo(DwaineProcessControlResult.Success));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task CascadeDoesNotRequeueAWaitingDescendantThatIsAlsoTerminating()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId rootId = default;
        DwaineProcessId middleId = default;
        DwaineProcessId leafId = default;
        var middleProgram = new WaitingProgram();

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var owner = new DwaineProcessOwner(23);
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "root", new HoldProgram()), out rootId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "middle", middleProgram, rootId),
                    out middleId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "leaf", new HoldProgram(), middleId),
                    out leafId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            middleProgram.Child = leafId;
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe);
            Assert.That(processes.TryGetProcess(mainframe, middleId, out var waiting), Is.True);
            Assert.That(waiting.State, Is.EqualTo(DwaineProcessState.Waiting));

            Assert.That(processes.TryKill(mainframe, rootId, middleId),
                Is.EqualTo(DwaineProcessControlResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(processes.TryGetProcess(mainframe, middleId, out var middle), Is.True);
                Assert.That(middle.State, Is.EqualTo(DwaineProcessState.Exited));
                Assert.That(processes.TryGetProcess(mainframe, leafId, out _), Is.False);
                Assert.That(runtime.ReadyQueue, Does.Not.Contain(middleId));
                Assert.That(runtime.Queued, Does.Not.Contain(middleId));
                Assert.That(runtime.CompletedProcessCount, Is.EqualTo(1));
            });

            Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(23), middleId), Is.True);
            Assert.That(runtime.CompletedProcessCount, Is.Zero);
            Assert.That(processes.TryExit(mainframe, rootId), Is.EqualTo(DwaineProcessControlResult.Success));
            Assert.That(runtime.CompletedProcessCount, Is.EqualTo(1));
            Assert.That(processes.TryReap(mainframe, new DwaineProcessOwner(23), rootId), Is.True);
            Assert.That(runtime.CompletedProcessCount, Is.Zero);
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task StoppedWaiterKeepsCompletedChildResultAfterAutomaticReap()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId controllerId = default;
        DwaineProcessId parentId = default;
        DwaineProcessId childId = default;
        var waiting = new WaitingProgram();

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var owner = new DwaineProcessOwner(24);
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "controller", new HoldProgram()), out controllerId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "parent", waiting), out parentId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(owner, "child", new HoldProgram(), parentId),
                    out childId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            waiting.Child = childId;
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var runtime = Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe);
            Assert.That(processes.TryGetProcess(mainframe, parentId, out var beforeStop), Is.True);
            Assert.That(beforeStop.State, Is.EqualTo(DwaineProcessState.Waiting));
            Assert.That(processes.TryStop(mainframe, controllerId, parentId),
                Is.EqualTo(DwaineProcessControlResult.Success));
            Assert.That(processes.TryExit(mainframe, childId, 37), Is.EqualTo(DwaineProcessControlResult.Success));

            Assert.Multiple(() =>
            {
                Assert.That(processes.TryGetProcess(mainframe, childId, out _), Is.False);
                Assert.That(processes.TryReap(mainframe, DwaineProcessOwner.System, childId), Is.False);
                Assert.That(processes.TryGetProcess(mainframe, parentId, out var stopped), Is.True);
                Assert.That(stopped.State, Is.EqualTo(DwaineProcessState.Stopped));
                Assert.That(stopped.WaitingFor, Is.Null);
                Assert.That(runtime.Processes[parentId].LastWaitResult?.ProcessId, Is.EqualTo(childId));
                Assert.That(runtime.Processes[parentId].LastWaitResult?.ExitCode, Is.EqualTo(37));
            });
            Assert.That(processes.TryContinue(mainframe, controllerId, parentId),
                Is.EqualTo(DwaineProcessControlResult.Success));
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(processes.TryGetProcess(mainframe, parentId, out var completed), Is.True);
                Assert.That(completed.State, Is.EqualTo(DwaineProcessState.Exited));
                Assert.That(completed.ExitCode, Is.EqualTo(37));
                Assert.That(waiting.ObservedResult?.ProcessId, Is.EqualTo(childId));
                Assert.That(waiting.ObservedResult?.ExitCode, Is.EqualTo(37));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task FaultStopContinueInstructionBudgetAndIpcAreContained()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId exceptionId = default;
        DwaineProcessId budgetId = default;

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(30), "exception", new ExceptionProgram()),
                    out exceptionId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(30), "budget", new BudgetProgram()),
                    out budgetId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(processes.TryGetProcess(mainframe, exceptionId, out var exception), Is.True);
                Assert.That(exception.State, Is.EqualTo(DwaineProcessState.Faulted));
                Assert.That(exception.ErrorCode, Is.EqualTo("program-exception"));
                Assert.That(exception.InstructionsConsumed, Is.EqualTo(1));
                Assert.That(processes.TryGetProcess(mainframe, budgetId, out var budget), Is.True);
                Assert.That(budget.State, Is.EqualTo(DwaineProcessState.Faulted));
                Assert.That(budget.ExitReason, Is.EqualTo(DwaineProcessExitReason.InstructionLimit));
                Assert.That(budget.ErrorCode, Is.EqualTo("instruction-budget-exceeded"));
            });
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var owner = new DwaineProcessOwner(31);
            var held = new HoldProgram();
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "held", held), out var heldId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(mainframe, Request(owner, "receiver", new HoldProgram()), out var receiver),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(32), "outsider", new HoldProgram()),
                    out var outsider),
                Is.EqualTo(DwaineProcessSpawnResult.Success));

            Assert.Multiple(() =>
            {
                Assert.That(processes.TryStop(mainframe, receiver, heldId),
                    Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryGetProcess(mainframe, heldId, out var stopped), Is.True);
                Assert.That(stopped.State, Is.EqualTo(DwaineProcessState.Stopped));
                Assert.That(processes.TryContinue(mainframe, receiver, heldId),
                    Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryStop(mainframe, outsider, heldId),
                    Is.EqualTo(DwaineProcessControlResult.AccessDenied));
                Assert.That(processes.TrySendMessage(mainframe, heldId, receiver, "data", "one"),
                    Is.EqualTo(DwaineProcessMessageResult.Success));
                Assert.That(processes.TrySendMessage(mainframe, heldId, receiver, "data", "two"),
                    Is.EqualTo(DwaineProcessMessageResult.Success));
                Assert.That(processes.TrySendMessage(mainframe, heldId, receiver, "data", "three"),
                    Is.EqualTo(DwaineProcessMessageResult.MailboxFull));
                Assert.That(processes.TrySendMessage(mainframe, heldId, receiver, "INVALID", "x"),
                    Is.EqualTo(DwaineProcessMessageResult.MalformedMessage));
                Assert.That(processes.TrySendMessage(mainframe, outsider, receiver, "data", "x"),
                    Is.EqualTo(DwaineProcessMessageResult.AccessDenied));
                Assert.That(processes.TryReceiveMessage(mainframe, receiver, out var first), Is.True);
                Assert.That(first.Payload, Is.EqualTo("one"));
                Assert.That(processes.TryReceiveMessage(mainframe, receiver, out var second), Is.True);
                Assert.That(second.Payload, Is.EqualTo("two"));
                Assert.That(processes.TryReceiveMessage(mainframe, receiver, out _), Is.False);
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task ProcessLimitsAndOneHundredTwentyEightProcessChurnStayBounded()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var firstOwner = new DwaineProcessOwner(40);
            var secondOwner = new DwaineProcessOwner(41);
            var active = new List<(DwaineProcessOwner Owner, DwaineProcessId Id)>();

            for (var index = 0; index < 4; index++)
            {
                Assert.That(processes.TrySpawn(
                        mainframe,
                        Request(firstOwner, $"first-{index}", new HoldProgram()),
                        out var processId),
                    Is.EqualTo(DwaineProcessSpawnResult.Success));
                active.Add((firstOwner, processId));
            }

            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(firstOwner, "owner-overflow", new HoldProgram()),
                    out _),
                Is.EqualTo(DwaineProcessSpawnResult.OwnerLimitReached));

            for (var index = 0; index < 4; index++)
            {
                Assert.That(processes.TrySpawn(
                        mainframe,
                        Request(secondOwner, $"second-{index}", new HoldProgram()),
                        out var processId),
                    Is.EqualTo(DwaineProcessSpawnResult.Success));
                active.Add((secondOwner, processId));
            }

            Assert.Multiple(() =>
            {
                Assert.That(processes.GetActiveProcessCount(mainframe), Is.EqualTo(8));
                Assert.That(processes.TrySpawn(
                        mainframe,
                        Request(new DwaineProcessOwner(42), "mainframe-overflow", new HoldProgram()),
                        out _),
                    Is.EqualTo(DwaineProcessSpawnResult.MainframeLimitReached));
            });

            foreach (var (owner, processId) in active)
            {
                Assert.That(processes.TryExit(mainframe, processId), Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryReap(mainframe, owner, processId), Is.True);
            }

            var allocated = new HashSet<DwaineProcessId>();
            for (var index = 0; index < 128; index++)
            {
                Assert.That(processes.TrySpawn(
                        mainframe,
                        Request(firstOwner, "churn", new HoldProgram()),
                        out var processId),
                    Is.EqualTo(DwaineProcessSpawnResult.Success));
                Assert.That(allocated.Add(processId), Is.True);
                Assert.That(processes.TryExit(mainframe, processId), Is.EqualTo(DwaineProcessControlResult.Success));
                Assert.That(processes.TryReap(mainframe, firstOwner, processId), Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Has.Count.EqualTo(128));
                Assert.That(processes.GetActiveProcessCount(mainframe), Is.Zero);
                Assert.That(processes.GetProcessTable(mainframe), Is.Empty);
                Assert.That(Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe)
                    .CompletedProcessCount, Is.Zero);
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    [Test]
    public async Task RebootAndDestructionCancelAllProcessesWithoutPidReuse()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId firstId = default;
        var firstProgram = new HoldProgram();

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(50), "before-reboot", firstProgram),
                    out firstId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.That(Server.System<DwaineKernelSystem>().TryReboot(mainframe), Is.True);
            var runtime = Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe);
            Assert.Multiple(() =>
            {
                Assert.That(firstProgram.CancelCalls, Is.EqualTo(1));
                Assert.That(firstProgram.LastCancellation, Is.EqualTo(DwaineProcessExitReason.KernelShutdown));
                Assert.That(runtime.Online, Is.False);
                Assert.That(processes.GetProcessTable(mainframe), Is.Empty);
            });
        });

        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            var secondProgram = new HoldProgram();
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(processes.TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(50), "after-reboot", secondProgram),
                    out var secondId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(secondId.Value, Is.GreaterThan(firstId.Value));
                Assert.That(Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe).BootGeneration,
                    Is.EqualTo(2));
            });

            Server.EntMan.DeleteEntity(map);
            Assert.Multiple(() =>
            {
                Assert.That(secondProgram.CancelCalls, Is.EqualTo(1));
                Assert.That(secondProgram.LastCancellation, Is.EqualTo(DwaineProcessExitReason.KernelShutdown));
            });
        });
    }

    [Test]
    public async Task SchedulerClampsSliceToDispatchPlusOneProgramInstruction()
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        DwaineProcessId processId = default;

        await SpawnReadyMainframe(uid =>
        {
            mainframe = uid.Mainframe;
            map = uid.Map;
        });

        await Server.WaitAssertion(() =>
        {
            var scheduler = Server.EntMan.GetComponent<DwaineProcessSchedulerComponent>(mainframe);
            scheduler.InstructionsPerSlice = 1;
            Assert.That(Server.System<DwaineProcessSystem>().TrySpawn(
                    mainframe,
                    Request(new DwaineProcessOwner(51), "one-instruction", new OneInstructionProgram()),
                    out processId),
                Is.EqualTo(DwaineProcessSpawnResult.Success));
        });

        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() =>
        {
            var processes = Server.System<DwaineProcessSystem>();
            Assert.That(processes.TryGetProcess(mainframe, processId, out var completed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(completed.State, Is.EqualTo(DwaineProcessState.Exited));
                Assert.That(completed.InstructionsConsumed, Is.EqualTo(2));
            });
            Server.EntMan.DeleteEntity(map);
        });
    }

    private async Task SpawnReadyMainframe(Action<(EntityUid Map, EntityUid Mainframe)> assign)
    {
        EntityUid map = EntityUid.Invalid;
        EntityUid mainframe = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            mainframe = Server.EntMan.SpawnEntity(
                "WhiskeyDwaineProcessTestMainframe",
                new MapCoordinates(Vector2.Zero, mapId));
            Assert.That(Server.System<DwaineKernelSystem>().TryBoot(mainframe), Is.True);
        });
        await Server.WaitRunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DwaineKernelSystem>().GetState(mainframe),
                Is.EqualTo(DwaineSystemState.SystemReady));
            Assert.That(Server.EntMan.GetComponent<DwaineProcessRuntimeComponent>(mainframe).Online, Is.True);
            assign((map, mainframe));
        });
    }

    private static DwaineProcessSpawnRequest Request(
        DwaineProcessOwner owner,
        string id,
        IDwaineProcessProgram implementation,
        DwaineProcessId? parentId = null,
        IReadOnlyDictionary<string, string> environment = null)
    {
        return new DwaineProcessSpawnRequest
        {
            Owner = owner,
            Program = new DwaineProgramDescriptor(id, id),
            Implementation = implementation,
            ParentId = parentId,
            Environment = environment,
        };
    }

    private sealed class EchoProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            Assert.That(context.TryChargeInstructions(2), Is.True);
            Assert.That(context.TryReadStdin(out var input), Is.True);
            Assert.That(context.TryGetEnvironment("USER", out var user), Is.True);
            Assert.That(context.TryWriteStdout($"{input}:{user}"), Is.True);
            Assert.That(context.TryWriteStderr("diagnostic"), Is.True);
            return DwaineProcessStepResult.Exit(7);
        }
    }

    private sealed class HoldProgram : IDwaineProcessProgram, IDwaineCancellableProcessProgram
    {
        public int Steps { get; private set; }
        public int CancelCalls { get; private set; }
        public DwaineProcessExitReason? LastCancellation { get; private set; }

        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            Steps++;
            return DwaineProcessStepResult.Yield();
        }

        public void Cancel(DwaineProcessExitReason reason)
        {
            CancelCalls++;
            LastCancellation = reason;
        }
    }

    private sealed class ExitAfterProgram(int steps, int exitCode) : IDwaineProcessProgram
    {
        private int _steps;

        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            _steps++;
            return _steps >= steps
                ? DwaineProcessStepResult.Exit(exitCode)
                : DwaineProcessStepResult.Yield();
        }
    }

    private sealed class WaitingProgram : IDwaineProcessProgram
    {
        private bool _waiting;
        public DwaineProcessId Child { get; set; }
        public DwaineProcessResult? ObservedResult { get; private set; }

        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            if (!_waiting)
            {
                _waiting = true;
                return DwaineProcessStepResult.Wait(Child);
            }

            if (!context.TryTakeWaitResult(out var result))
                return DwaineProcessStepResult.Fault("missing-wait-result");

            ObservedResult = result;
            return DwaineProcessStepResult.Exit(result.ExitCode);
        }
    }

    private sealed class ExceptionProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            throw new InvalidOperationException("must be contained by the process boundary");
        }
    }

    private sealed class BudgetProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            context.TryChargeInstructions(8);
            return DwaineProcessStepResult.Yield();
        }
    }

    private sealed class OneInstructionProgram : IDwaineProcessProgram
    {
        public DwaineProcessStepResult Step(DwaineProcessExecutionContext context)
        {
            return context.TryChargeInstructions(1)
                ? DwaineProcessStepResult.Exit()
                : DwaineProcessStepResult.Fault("missing-program-budget");
        }
    }
}
