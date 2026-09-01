// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.VodkaCode;

/// <summary>
/// Server-clamped limits for the deterministic Vodka Code virtual machine.
/// No interpreter state is replicated to clients.
/// </summary>
[RegisterComponent]
public sealed partial class VodkaRuntimeComponent : Component
{
    public const int HardMaxInstructionsPerInvocation = 1_000_000;
    public const int HardMaxVariables = 4096;
    public const int HardMaxStringBytes = 65_536;
    public const int HardMaxDataBytes = 262_144;
    public const int HardMaxOutputBytes = 262_144;
    public const int HardMaxOperandStack = 4096;
    public const int HardMaxCompatibilityStack = 4096;
    public const int HardMaxArguments = 128;
    public const int HardMaxArgumentBytes = 65_536;
    public const int HardMaxCallDepth = 64;
    public const float HardMaxLogicalTimeoutSeconds = 300f;

    [DataField]
    public int MaxInstructionsPerInvocation = 100_000;

    [DataField]
    public int MaxVariables = 512;

    [DataField]
    public int MaxStringBytes = 16_384;

    [DataField]
    public int MaxDataBytes = 65_536;

    [DataField]
    public int MaxOutputBytes = 65_536;

    [DataField]
    public int MaxOperandStack = 1024;

    [DataField]
    public int MaxCompatibilityStack = 512;

    [DataField]
    public int MaxArguments = 32;

    [DataField]
    public int MaxArgumentBytes = 16_384;

    [DataField]
    public int MaxCallDepth = 64;

    [DataField]
    public float LogicalTimeoutSeconds = 30f;
}
