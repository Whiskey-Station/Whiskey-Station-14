// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Kernel;

namespace Content.Server._Whiskey.Dwaine.Kernel;

[RegisterComponent]
public sealed partial class DwaineKernelRuntimeComponent : Component
{
    public DwaineSystemState State = DwaineSystemState.PoweredOff;
    public DwaineBootFailure Failure;
    public ulong BootGeneration;
    public TimeSpan StateEnteredAt;
    public TimeSpan NextTransitionAt;
    public bool RestartAfterShutdown;
    public readonly DwaineSystemClock Clock = new();
    public readonly DwaineKernelServiceRegistry Services = new();
    public readonly DwaineBootDiagnosticBuffer Diagnostics = new();
}

[ByRefEvent]
public readonly record struct DwaineSystemStateChangedEvent(
    DwaineSystemState Previous,
    DwaineSystemState Current,
    DwaineBootFailure Failure,
    ulong BootGeneration);

/// <summary>
/// Handoff point for later authentication and shell layers.
/// </summary>
[ByRefEvent]
public readonly record struct DwaineKernelReadyEvent(ulong BootGeneration);
