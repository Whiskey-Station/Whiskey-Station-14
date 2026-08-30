// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine;

/// <summary>
/// The world-facing hardware roles used by the DWAINE contracts. Gameplay state
/// is added through ECS components rather than stored in this classification.
/// </summary>
public enum DwaineMachineKind : byte
{
    Computer,
    Mainframe,
    Terminal,
}

/// <summary>
/// The externally observable stages of the DWAINE boot contract.
/// </summary>
public enum DwaineBootStage : byte
{
    PoweredOff,
    Post,
    Bootloader,
    Kernel,
    Login,
    Shell,
    Faulted,
}

/// <summary>
/// Hardware-only state exposed by a DWAINE chassis before an operating system exists.
/// This is deliberately separate from <see cref="DwaineBootStage"/>: PR 02 never
/// pretends that POST or a kernel has run.
/// </summary>
public enum DwaineHardwareStatus : byte
{
    PoweredOff,
    PowerUnavailable,
    HardwareReady,
}
