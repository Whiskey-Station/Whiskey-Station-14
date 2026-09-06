// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Hardware;

namespace Content.Server._Whiskey.Dwaine.Hardware;

/// <summary>
/// Transient, authoritative hardware state. This component is never networked.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineHardwareRuntimeComponent : Component
{
    public bool PowerEnabled = true;
    public bool HasPowerSupply;
    public DwaineHardwareStatus Status = DwaineHardwareStatus.PowerUnavailable;
    public readonly HashSet<EntityUid> ActiveViewers = new();
    public DwaineBoundedTextBuffer? Output;
}

/// <summary>
/// Raised only after the server has validated terminal input.
/// Later transport layers consume this without trusting client identity fields.
/// </summary>
[ByRefEvent]
public readonly record struct DwaineTerminalInputReceivedEvent(EntityUid Actor, string Text);

/// <summary>
/// Authoritative effective-power transition consumed by later runtime layers.
/// </summary>
[ByRefEvent]
public readonly record struct DwaineHardwarePowerChangedEvent(bool Powered);

/// <summary>
/// Extension point for server systems to add presentation-only terminal state without
/// making the physical hardware layer depend on those systems.
/// </summary>
[ByRefEvent]
public record struct DwaineTerminalPresentationEvent
{
    public DwaineTerminalConnectionStatus Status = DwaineTerminalConnectionStatus.Disconnected;
    public string ConnectedMainframe = string.Empty;
    public DwaineMainframeUiEntry[] AvailableMainframes = [];
    public string[]? OutputOverride;

    public DwaineTerminalPresentationEvent()
    {
    }
}
