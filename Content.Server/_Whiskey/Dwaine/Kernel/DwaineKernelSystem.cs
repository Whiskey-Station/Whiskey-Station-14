// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Hardware;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Transport;
using Robust.Shared.Log;
using Robust.Shared.Timing;
using System.Diagnostics;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Kernel;

/// <summary>
/// Owns the deterministic mainframe bootloader and kernel lifecycle. It does not schedule processes.
/// </summary>
public sealed partial class DwaineKernelSystem : EntitySystem
{
    private const int MaximumTransitionsPerUpdate = 8;

    [Dependency] private DwaineHardwareSystem _hardware = default!;
    [Dependency] private DwaineTerminalTransportSystem _transport = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _activeMainframes = new();
    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = _logManager.GetSawmill("whiskey.dwaine.kernel");

        SubscribeLocalEvent<DwaineKernelComponent, MapInitEvent>(OnKernelMapInit);
        SubscribeLocalEvent<DwaineKernelComponent, ComponentShutdown>(OnKernelShutdown);
        SubscribeLocalEvent<DwaineKernelRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineKernelComponent, DwaineHardwarePowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<DwaineKernelComponent, DwaineMainframeSessionConnectedEvent>(OnSessionConnected);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        foreach (var mainframe in _activeMainframes.ToArray())
        {
            if (TerminatingOrDeleted(mainframe)
                || !TryComp<DwaineKernelComponent>(mainframe, out var config)
                || !TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime))
            {
                _activeMainframes.Remove(mainframe);
                continue;
            }

            runtime.Clock.Observe(now);
            if (_hardware.GetStatus(mainframe) != DwaineHardwareStatus.HardwareReady)
            {
                HandlePowerLoss(mainframe, runtime, now);
                continue;
            }

            for (var transitions = 0;
                 transitions < MaximumTransitionsPerUpdate && IsTimedState(runtime.State) && now >= runtime.NextTransitionAt;
                 transitions++)
            {
                var transitionAt = runtime.NextTransitionAt;
                AdvanceState(mainframe, config, runtime, transitionAt);
            }
        }
    }

    private void OnKernelMapInit(Entity<DwaineKernelComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<DwaineKernelRuntimeComponent>(ent, out var runtime))
            return;

        runtime.Clock.Observe(_timing.CurTime);
        if (ent.Comp.AutoBoot && _hardware.GetStatus(ent.Owner) == DwaineHardwareStatus.HardwareReady)
            TryBoot(ent.Owner);
    }

    private void OnKernelShutdown(Entity<DwaineKernelComponent> ent, ref ComponentShutdown args)
    {
        _activeMainframes.Remove(ent.Owner);

        // Entity deletion is owned by the runtime shutdown handler below. This handler exists only
        // for the exceptional case where the kernel configuration is removed from a live entity.
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (TryComp<DwaineKernelRuntimeComponent>(ent, out var runtime))
            StopServices(ent.Owner, runtime, DwaineKernelShutdownReason.EntityRemoved, _timing.CurTime);
    }

    private void OnRuntimeShutdown(Entity<DwaineKernelRuntimeComponent> ent, ref ComponentShutdown args)
    {
        _activeMainframes.Remove(ent.Owner);
        StopServices(ent.Owner, ent.Comp, DwaineKernelShutdownReason.EntityRemoved, _timing.CurTime);
    }

    private void OnPowerChanged(Entity<DwaineKernelComponent> ent, ref DwaineHardwarePowerChangedEvent args)
    {
        if (!TryComp<DwaineKernelRuntimeComponent>(ent, out var runtime))
            return;

        if (!args.Powered)
        {
            HandlePowerLoss(ent.Owner, runtime, _timing.CurTime);
            return;
        }

        if (ent.Comp.AutoBoot && runtime.State == DwaineSystemState.PoweredOff)
            TryBoot(ent.Owner);
    }

    private void OnSessionConnected(Entity<DwaineKernelComponent> ent, ref DwaineMainframeSessionConnectedEvent args)
    {
        if (!TryComp<DwaineKernelRuntimeComponent>(ent, out var runtime))
            return;

        foreach (var diagnostic in runtime.Diagnostics.Snapshot())
            _transport.WriteOutput(ent.Owner, args.Session, FormatDiagnostic(diagnostic));
    }

    public DwaineSystemState GetState(EntityUid mainframe)
    {
        return TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
            ? runtime.State
            : DwaineSystemState.PoweredOff;
    }

    public DwaineBootDiagnostic[] GetDiagnostics(EntityUid mainframe)
    {
        return TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
            ? runtime.Diagnostics.Snapshot()
            : [];
    }

    public bool TryGetClock(EntityUid mainframe, out DwaineSystemClockSnapshot snapshot)
    {
        snapshot = default;
        if (!TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime))
            return false;

        runtime.Clock.Observe(_timing.CurTime);
        snapshot = runtime.Clock.Snapshot();
        return true;
    }

    public bool TryRegisterService(EntityUid mainframe, string name, IDwaineKernelService service)
    {
        return TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
               && runtime.State == DwaineSystemState.SystemReady
               && runtime.Services.TryRegister(name, service);
    }

    public bool TryUnregisterService(EntityUid mainframe, string name)
    {
        return TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
               && runtime.Services.TryUnregister(name);
    }

    public bool TryBoot(EntityUid mainframe)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineMainframeComponent>(mainframe, out _)
            || !TryComp<DwaineKernelComponent>(mainframe, out var config)
            || !TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
            || runtime.State is not (DwaineSystemState.PoweredOff
                or DwaineSystemState.BootFailed
                or DwaineSystemState.KernelPanic))
        {
            return false;
        }

        if (_hardware.GetStatus(mainframe) != DwaineHardwareStatus.HardwareReady)
        {
            runtime.Failure = DwaineBootFailure.HardwareUnavailable;
            return false;
        }

        var now = _timing.CurTime;
        Debug.Assert(
            runtime.Services.Count == 0,
            "A non-ready kernel state must never retain services from an earlier boot generation.");
        if (runtime.Services.Count > 0)
        {
            _log.Error(
                $"DWAINE mainframe {ToPrettyString(mainframe)} retained {runtime.Services.Count} " +
                "kernel service(s) in a non-ready state; forcing a bounded cleanup before boot.");
            StopServices(mainframe, runtime, DwaineKernelShutdownReason.BootFailed, now);
        }

        runtime.BootGeneration++;
        if (runtime.BootGeneration == 0)
            runtime.BootGeneration = 1;

        runtime.Failure = DwaineBootFailure.None;
        runtime.RestartAfterShutdown = false;
        runtime.Diagnostics.Clear();
        runtime.Clock.StartBoot(now, runtime.BootGeneration);
        EnterTimedState(
            mainframe,
            runtime,
            DwaineSystemState.PowerOnSelfTest,
            now,
            config.PostDurationSeconds,
            "post-start",
            Loc.GetString("dwaine-kernel-diagnostic-post"));
        _activeMainframes.Add(mainframe);
        return true;
    }

    public bool TryShutdown(EntityUid mainframe)
    {
        if (!TryComp<DwaineKernelComponent>(mainframe, out var config)
            || !TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
            || runtime.State is DwaineSystemState.PoweredOff or DwaineSystemState.ShuttingDown)
        {
            return false;
        }

        BeginShutdown(mainframe, config, runtime, false, DwaineKernelShutdownReason.Requested);
        return true;
    }

    public bool TryReboot(EntityUid mainframe)
    {
        if (!TryComp<DwaineKernelComponent>(mainframe, out var config)
            || !TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime))
        {
            return false;
        }

        if (runtime.State is DwaineSystemState.PoweredOff
            or DwaineSystemState.BootFailed
            or DwaineSystemState.KernelPanic)
        {
            return TryBoot(mainframe);
        }

        if (runtime.State == DwaineSystemState.ShuttingDown)
        {
            runtime.RestartAfterShutdown = true;
            return true;
        }

        BeginShutdown(mainframe, config, runtime, true, DwaineKernelShutdownReason.Reboot);
        return true;
    }

    public bool Panic(EntityUid mainframe, string code)
    {
        if (!TryComp<DwaineKernelRuntimeComponent>(mainframe, out var runtime)
            || runtime.State is DwaineSystemState.PoweredOff
                or DwaineSystemState.BootFailed
                or DwaineSystemState.KernelPanic)
        {
            return false;
        }

        var now = _timing.CurTime;
        StopServices(mainframe, runtime, DwaineKernelShutdownReason.Panic, now);
        runtime.RestartAfterShutdown = false;
        runtime.Failure = DwaineBootFailure.KernelPanic;
        runtime.Clock.Stop(now);
        _activeMainframes.Remove(mainframe);
        EnterState(
            mainframe,
            runtime,
            DwaineSystemState.KernelPanic,
            now,
            NormalizePanicCode(code),
            Loc.GetString("dwaine-kernel-diagnostic-panic"));
        return true;
    }

    private void AdvanceState(
        EntityUid mainframe,
        DwaineKernelComponent config,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan transitionAt)
    {
        switch (runtime.State)
        {
            case DwaineSystemState.PowerOnSelfTest:
                CompletePost(mainframe, config, runtime, transitionAt);
                break;
            case DwaineSystemState.Bootloader:
                EnterTimedState(
                    mainframe,
                    runtime,
                    DwaineSystemState.KernelInitializing,
                    transitionAt,
                    config.KernelInitializationDurationSeconds,
                    "kernel-initialize",
                    Loc.GetString("dwaine-kernel-diagnostic-initialize"));
                break;
            case DwaineSystemState.KernelInitializing:
                CompleteKernelInitialization(mainframe, runtime, transitionAt);
                break;
            case DwaineSystemState.ShuttingDown:
                CompleteShutdown(mainframe, runtime, transitionAt);
                break;
        }
    }

    private void CompletePost(
        EntityUid mainframe,
        DwaineKernelComponent config,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan transitionAt)
    {
        if (_hardware.GetStatus(mainframe) != DwaineHardwareStatus.HardwareReady
            || !HasComp<DwaineMainframeComponent>(mainframe))
        {
            FailBoot(
                mainframe,
                runtime,
                DwaineBootFailure.HardwareUnavailable,
                transitionAt,
                "post-hardware-failed",
                Loc.GetString("dwaine-kernel-diagnostic-hardware-failed"));
            return;
        }

        if (config.RequireStorageConnector
            && (!TryComp<DwaineStorageConnectorComponent>(mainframe, out var storage)
                || !storage.Enabled
                || storage.SlotCount <= 0))
        {
            var recovery = new DwaineBootRecoveryRequestedEvent();
            RaiseLocalEvent(mainframe, ref recovery);
            if (recovery.Recovered)
            {
                EnterTimedState(
                    mainframe,
                    runtime,
                    DwaineSystemState.Bootloader,
                    transitionAt,
                    config.BootloaderDurationSeconds,
                    "bootloader-start",
                    Loc.GetString("dwaine-kernel-diagnostic-bootloader"));
                return;
            }
            FailBoot(
                mainframe,
                runtime,
                DwaineBootFailure.StorageUnavailable,
                transitionAt,
                "post-storage-failed",
                Loc.GetString("dwaine-kernel-diagnostic-storage-failed"));
            return;
        }

        EnterTimedState(
            mainframe,
            runtime,
            DwaineSystemState.Bootloader,
            transitionAt,
            config.BootloaderDurationSeconds,
            "bootloader-start",
            Loc.GetString("dwaine-kernel-diagnostic-bootloader"));
    }

    private void CompleteKernelInitialization(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan transitionAt)
    {
        if (_hardware.GetStatus(mainframe) != DwaineHardwareStatus.HardwareReady)
        {
            FailBoot(
                mainframe,
                runtime,
                DwaineBootFailure.KernelInitializationFailed,
                transitionAt,
                "kernel-initialize-failed",
                Loc.GetString("dwaine-kernel-diagnostic-initialize-failed"));
            return;
        }

        runtime.Failure = DwaineBootFailure.None;
        // SystemReady is deliberately event-driven instead of remaining in the per-update set.
        // DwaineHardwareStatus currently changes readiness only through power transitions, and
        // every such edge raises DwaineHardwarePowerChangedEvent. Any future hardware-health
        // source must raise an equivalent authoritative event before it can affect this status.
        _activeMainframes.Remove(mainframe);
        EnterState(
            mainframe,
            runtime,
            DwaineSystemState.SystemReady,
            transitionAt,
            "system-ready",
            Loc.GetString("dwaine-kernel-diagnostic-ready"));
        var ready = new DwaineKernelReadyEvent(runtime.BootGeneration);
        RaiseLocalEvent(mainframe, ref ready);
    }

    private void BeginShutdown(
        EntityUid mainframe,
        DwaineKernelComponent config,
        DwaineKernelRuntimeComponent runtime,
        bool restart,
        DwaineKernelShutdownReason reason)
    {
        var now = _timing.CurTime;
        runtime.RestartAfterShutdown = restart;
        StopServices(mainframe, runtime, reason, now);
        EnterTimedState(
            mainframe,
            runtime,
            DwaineSystemState.ShuttingDown,
            now,
            config.ShutdownDurationSeconds,
            restart ? "reboot-start" : "shutdown-start",
            Loc.GetString(restart
                ? "dwaine-kernel-diagnostic-reboot"
                : "dwaine-kernel-diagnostic-shutdown"));
        _activeMainframes.Add(mainframe);
    }

    private void CompleteShutdown(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan transitionAt)
    {
        var restart = runtime.RestartAfterShutdown;
        runtime.RestartAfterShutdown = false;
        runtime.Clock.Stop(transitionAt);
        _activeMainframes.Remove(mainframe);
        EnterState(
            mainframe,
            runtime,
            DwaineSystemState.PoweredOff,
            transitionAt,
            "system-off",
            Loc.GetString("dwaine-kernel-diagnostic-off"));

        if (restart && _hardware.GetStatus(mainframe) == DwaineHardwareStatus.HardwareReady)
            TryBoot(mainframe);
    }

    private void HandlePowerLoss(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan now)
    {
        if (runtime.State == DwaineSystemState.PoweredOff)
            return;

        StopServices(mainframe, runtime, DwaineKernelShutdownReason.PowerLost, now);
        runtime.RestartAfterShutdown = false;
        runtime.Failure = DwaineBootFailure.PowerLost;
        runtime.Clock.Stop(now);
        _activeMainframes.Remove(mainframe);
        EnterState(
            mainframe,
            runtime,
            DwaineSystemState.PoweredOff,
            now,
            "power-lost",
            Loc.GetString("dwaine-kernel-diagnostic-power-lost"));
    }

    private void FailBoot(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        DwaineBootFailure failure,
        TimeSpan now,
        string code,
        string message)
    {
        StopServices(mainframe, runtime, DwaineKernelShutdownReason.BootFailed, now);
        runtime.RestartAfterShutdown = false;
        runtime.Failure = failure;
        runtime.Clock.Stop(now);
        _activeMainframes.Remove(mainframe);
        EnterState(mainframe, runtime, DwaineSystemState.BootFailed, now, code, message);
    }

    private void StopServices(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        DwaineKernelShutdownReason reason,
        TimeSpan now)
    {
        if (runtime.Services.Count == 0)
            return;

        var context = new DwaineKernelShutdownContext(mainframe, runtime.BootGeneration, reason);
        foreach (var failure in runtime.Services.ShutdownAll(context))
        {
            AddDiagnostic(
                mainframe,
                runtime,
                now,
                "service-shutdown-failed",
                Loc.GetString("dwaine-kernel-diagnostic-service-failed", ("service", failure.ServiceName)));
        }
    }

    private void EnterTimedState(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        DwaineSystemState state,
        TimeSpan enteredAt,
        float durationSeconds,
        string code,
        string message)
    {
        EnterState(mainframe, runtime, state, enteredAt, code, message);
        runtime.NextTransitionAt = enteredAt + GetDuration(durationSeconds);
    }

    private void EnterState(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        DwaineSystemState state,
        TimeSpan enteredAt,
        string code,
        string message)
    {
        var previous = runtime.State;
        runtime.State = state;
        runtime.StateEnteredAt = enteredAt;
        runtime.NextTransitionAt = TimeSpan.Zero;
        runtime.Clock.Observe(enteredAt);
        AddDiagnostic(mainframe, runtime, enteredAt, code, message);
        var changed = new DwaineSystemStateChangedEvent(
            previous,
            state,
            runtime.Failure,
            runtime.BootGeneration);
        RaiseLocalEvent(mainframe, ref changed);
    }

    private void AddDiagnostic(
        EntityUid mainframe,
        DwaineKernelRuntimeComponent runtime,
        TimeSpan timestamp,
        string code,
        string message)
    {
        runtime.Diagnostics.Add(timestamp, runtime.State, code, message);
        _transport.WriteOutputToAll(mainframe, FormatDiagnostic(runtime.Diagnostics.Snapshot()[^1]));
    }

    private static string FormatDiagnostic(DwaineBootDiagnostic diagnostic)
    {
        return $"[{diagnostic.Code}] {diagnostic.Message}";
    }

    private static bool IsTimedState(DwaineSystemState state)
    {
        return state is DwaineSystemState.PowerOnSelfTest
            or DwaineSystemState.Bootloader
            or DwaineSystemState.KernelInitializing
            or DwaineSystemState.ShuttingDown;
    }

    private static TimeSpan GetDuration(float seconds)
    {
        if (!float.IsFinite(seconds))
            seconds = DwaineKernelComponent.MaximumStageDurationSeconds;

        return TimeSpan.FromSeconds(Math.Clamp(
            seconds,
            DwaineKernelComponent.MinimumStageDurationSeconds,
            DwaineKernelComponent.MaximumStageDurationSeconds));
    }

    private static string NormalizePanicCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "kernel-fault";

        var normalized = new string(code
            .ToLowerInvariant()
            .Where(character => character is >= 'a' and <= 'z'
                                or >= '0' and <= '9'
                                or '-' or '_')
            .Take(32)
            .ToArray());
        return string.IsNullOrEmpty(normalized) ? "kernel-fault" : normalized;
    }
}
