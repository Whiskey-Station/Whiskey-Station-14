// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Server.Power.Components;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;

namespace Content.Server._Whiskey.Dwaine.Hardware;

/// <summary>
/// Owns physical DWAINE power, presentation leases, and validated line input.
/// It deliberately contains no boot, login, shell, or mainframe behavior.
/// </summary>
public sealed partial class DwaineHardwareSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineComputerHardwareComponent, MapInitEvent>(OnHardwareMapInit);
        SubscribeLocalEvent<DwaineComputerHardwareComponent, ComponentShutdown>(OnHardwareShutdown);
        SubscribeLocalEvent<DwaineComputerHardwareComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<DwaineTerminalComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<DwaineTerminalComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalTogglePowerMessage>(OnTogglePower);
        SubscribeLocalEvent<DwaineTerminalComponent, DwaineTerminalInputMessage>(OnTerminalInput);
    }

    private void OnHardwareMapInit(Entity<DwaineComputerHardwareComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<DwaineHardwareRuntimeComponent>(ent, out var runtime))
            return;

        var terminal = CompOrNull<DwaineTerminalComponent>(ent);

        runtime.Output ??= new DwaineBoundedTextBuffer(
            Math.Clamp(terminal?.OutputLineLimit ?? 16, 1, DwaineTerminalComponent.HardMaxOutputLines),
            Math.Clamp(terminal?.OutputCharacterLimit ?? 2048, 1, DwaineTerminalComponent.HardMaxOutputCharacters));

        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
        {
            runtime.PowerEnabled = !receiver.PowerDisabled;
            runtime.HasPowerSupply = receiver.Powered;
        }
        else
        {
            runtime.HasPowerSupply = !ent.Comp.RequiresExternalPower;
        }

        RefreshStatus((ent.Owner, ent.Comp, runtime));
    }

    private void OnHardwareShutdown(Entity<DwaineComputerHardwareComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<DwaineHardwareRuntimeComponent>(ent, out var runtime))
            return;

        runtime.ActiveViewers.Clear();
        runtime.Output?.Clear();
    }

    private void OnPowerChanged(Entity<DwaineComputerHardwareComponent> ent, ref PowerChangedEvent args)
    {
        if (!TryComp<DwaineHardwareRuntimeComponent>(ent, out var runtime))
            return;

        runtime.HasPowerSupply = args.Powered;

        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
            runtime.PowerEnabled = !receiver.PowerDisabled;

        RefreshStatus((ent.Owner, ent.Comp, runtime));
    }

    private void OnUiOpened(Entity<DwaineTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, DwaineTerminalUiKey.Key))
            return;

        RegisterViewer(ent.Owner, args.Actor);
        UpdateUi(ent.Owner);
    }

    private void OnUiClosed(Entity<DwaineTerminalComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, DwaineTerminalUiKey.Key))
            return;

        UnregisterViewer(ent.Owner, args.Actor);
    }

    private void OnTogglePower(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalTogglePowerMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args.Actor))
            return;

        TryTogglePower(ent.Owner, args.Actor);
    }

    private void OnTerminalInput(Entity<DwaineTerminalComponent> ent, ref DwaineTerminalInputMessage args)
    {
        if (!IsAuthorizedUiActor(ent.Owner, args.Actor)
            || !TryComp<DwaineHardwareRuntimeComponent>(ent, out var runtime)
            || runtime.Status != DwaineHardwareStatus.HardwareReady
            || !TryComp<DwaineKeyboardInputComponent>(ent, out var keyboard)
            || !keyboard.Enabled
            || !TryValidateInput(args.Text, ent.Comp.MaxInputLength, out var input))
        {
            return;
        }

        var ev = new DwaineTerminalInputReceivedEvent(args.Actor, input);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    public bool RegisterViewer(EntityUid terminal, EntityUid actor)
    {
        if (TerminatingOrDeleted(terminal)
            || TerminatingOrDeleted(actor)
            || !TryComp<DwaineTerminalComponent>(terminal, out _)
            || !TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime))
        {
            return false;
        }

        return runtime.ActiveViewers.Add(actor);
    }

    public bool UnregisterViewer(EntityUid terminal, EntityUid actor)
    {
        return TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime)
               && runtime.ActiveViewers.Remove(actor);
    }

    public int GetActiveViewerCount(EntityUid terminal)
    {
        return TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime)
            ? runtime.ActiveViewers.Count
            : 0;
    }

    public bool TryTogglePower(EntityUid uid, EntityUid? actor = null)
    {
        if (!TryComp<DwaineComputerHardwareComponent>(uid, out _)
            || !TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime))
        {
            return false;
        }

        if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            runtime.PowerEnabled = _power.TogglePower(uid, receiver: receiver, user: actor);
        else
            runtime.PowerEnabled = !runtime.PowerEnabled;

        RefreshStatus(uid);
        return true;
    }

    /// <summary>
    /// Adapter point for authoritative power providers and focused integration tests.
    /// </summary>
    public bool SetPowerSupply(EntityUid uid, bool supplied)
    {
        if (!TryComp<DwaineComputerHardwareComponent>(uid, out _)
            || !TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime))
        {
            return false;
        }

        runtime.HasPowerSupply = supplied;
        RefreshStatus(uid);
        return true;
    }

    public bool SetPowerEnabled(EntityUid uid, bool enabled)
    {
        if (!TryComp<DwaineComputerHardwareComponent>(uid, out _)
            || !TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime))
        {
            return false;
        }

        if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            _power.SetPowerDisabled(uid, !enabled, receiver);

        runtime.PowerEnabled = enabled;
        RefreshStatus(uid);
        return true;
    }

    public bool WriteServerText(EntityUid terminal, string text)
    {
        if (TerminatingOrDeleted(terminal)
            || !TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime)
            || runtime.Output is null)
        {
            return false;
        }

        runtime.Output.Add(text);
        UpdateUi(terminal);
        return true;
    }

    public string[] GetOutputSnapshot(EntityUid terminal)
    {
        return TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime)
            ? runtime.Output?.Snapshot() ?? []
            : [];
    }

    public DwaineHardwareStatus? GetStatus(EntityUid uid)
    {
        return TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime)
            ? runtime.Status
            : null;
    }

    public static bool TryValidateInput(string? text, int maxLength, out string input)
    {
        input = string.Empty;
        var effectiveLimit = Math.Min(maxLength, DwaineTerminalComponent.HardMaxInputLength);
        if (string.IsNullOrEmpty(text) || effectiveLimit <= 0 || text.Length > effectiveLimit)
            return false;

        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\0'
                || char.IsControl(character) && character != '\t')
            {
                return false;
            }
        }

        input = text;
        return true;
    }

    public bool IsAuthorizedUiActor(EntityUid terminal, EntityUid actor)
    {
        return !TerminatingOrDeleted(actor)
               && _ui.IsUiOpen(terminal, DwaineTerminalUiKey.Key, actor)
               && TryComp<DwaineHardwareRuntimeComponent>(terminal, out var runtime)
               && runtime.ActiveViewers.Contains(actor);
    }

    private void RefreshStatus(EntityUid uid)
    {
        if (!TryComp<DwaineComputerHardwareComponent>(uid, out var hardware)
            || !TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime))
        {
            return;
        }

        RefreshStatus((uid, hardware, runtime));
    }

    private void RefreshStatus(Entity<DwaineComputerHardwareComponent, DwaineHardwareRuntimeComponent> ent)
    {
        var oldPowered = ent.Comp2.Status == DwaineHardwareStatus.HardwareReady;
        ent.Comp2.Status = !ent.Comp2.PowerEnabled
            ? DwaineHardwareStatus.PoweredOff
            : !ent.Comp2.HasPowerSupply
                ? DwaineHardwareStatus.PowerUnavailable
                : DwaineHardwareStatus.HardwareReady;

        var powered = ent.Comp2.Status == DwaineHardwareStatus.HardwareReady;
        if (oldPowered != powered)
        {
            var ev = new DwaineHardwarePowerChangedEvent(powered);
            RaiseLocalEvent(ent.Owner, ref ev);
        }

        UpdateUi(ent.Owner);
    }

    public void UpdateUi(EntityUid uid)
    {
        if (!TryComp<DwaineTerminalComponent>(uid, out var terminal)
            || !TryComp<DwaineHardwareRuntimeComponent>(uid, out var runtime))
        {
            return;
        }

        var display = CompOrNull<DwaineDisplayComponent>(uid);
        var storage = CompOrNull<DwaineStorageConnectorComponent>(uid);
        var network = CompOrNull<DwaineNetworkConnectorComponent>(uid);
        var bus = CompOrNull<DwaineDeviceBusEndpointComponent>(uid);
        var presentation = new DwaineTerminalPresentationEvent();
        RaiseLocalEvent(uid, ref presentation);

        var state = new DwaineTerminalBoundUserInterfaceState(
            runtime.Status,
            runtime.PowerEnabled,
            runtime.HasPowerSupply,
            Math.Clamp(terminal.MaxInputLength, 1, DwaineTerminalComponent.HardMaxInputLength),
            display?.Columns ?? 0,
            display?.Rows ?? 0,
            storage is { Enabled: true } ? storage.SlotCount : 0,
            network is { Enabled: true } ? network.NetworkId : string.Empty,
            bus is { Enabled: true } ? bus.BusId : string.Empty,
            presentation.OutputOverride ?? runtime.Output?.Snapshot() ?? [],
            presentation.Status,
            presentation.ConnectedMainframe,
            presentation.AvailableMainframes);

        _ui.SetUiState(uid, DwaineTerminalUiKey.Key, state);
    }
}
