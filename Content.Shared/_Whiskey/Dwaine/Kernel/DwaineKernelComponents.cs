// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Kernel;

/// <summary>
/// Authoritative lifecycle of a DWAINE mainframe. Login and shell handoff are
/// intentionally represented by the ready state and implemented by later layers.
/// </summary>
public enum DwaineSystemState : byte
{
    PoweredOff,
    PowerOnSelfTest,
    Bootloader,
    KernelInitializing,
    SystemReady,
    ShuttingDown,
    BootFailed,
    KernelPanic,
}

public enum DwaineBootFailure : byte
{
    None,
    PowerLost,
    HardwareUnavailable,
    StorageUnavailable,
    KernelInitializationFailed,
    Cancelled,
    KernelPanic,
}

/// <summary>
/// Configuration for the bootloader and kernel lifecycle. Runtime state remains server-only.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineKernelComponent : Component
{
    public const float MinimumStageDurationSeconds = 0.01f;
    public const float MaximumStageDurationSeconds = 30f;

    [DataField]
    public bool AutoBoot = true;

    [DataField]
    public bool RequireStorageConnector = true;

    [DataField]
    public bool RequireBootMedia;

    [DataField]
    public string BootProfile = "whiskey-system-v1";

    [DataField]
    public float PostDurationSeconds = 0.4f;

    [DataField]
    public float BootloaderDurationSeconds = 0.4f;

    [DataField]
    public float KernelInitializationDurationSeconds = 0.4f;

    [DataField]
    public float ShutdownDurationSeconds = 0.2f;
}
