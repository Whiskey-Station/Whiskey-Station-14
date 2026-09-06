// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.VodkaCode;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

internal enum VodkaExecutionState : byte
{
    Ready,
    Yielded,
    Returned,
    Exited,
    Faulted,
    Cancelled,
}

internal enum VodkaSpawnResult : byte
{
    Success,
    RuntimeUnavailable,
    InvalidPath,
    InvalidExtension,
    AccessDenied,
    FileUnavailable,
    InvalidArguments,
    SyntaxError,
    ProcessRejected,
}

internal readonly record struct VodkaRuntimeLimits(
    int MaxInstructions,
    int MaxVariables,
    int MaxStringBytes,
    int MaxDataBytes,
    int MaxOutputBytes,
    int MaxOperandStack,
    int MaxCompatibilityStack,
    int MaxArguments,
    int MaxArgumentBytes,
    int MaxCallDepth,
    TimeSpan LogicalTimeout)
{
    public static VodkaRuntimeLimits FromComponent(VodkaRuntimeComponent component)
    {
        var timeout = float.IsFinite(component.LogicalTimeoutSeconds)
            ? component.LogicalTimeoutSeconds
            : VodkaRuntimeComponent.HardMaxLogicalTimeoutSeconds;

        return new VodkaRuntimeLimits(
            Math.Clamp(component.MaxInstructionsPerInvocation, 1, VodkaRuntimeComponent.HardMaxInstructionsPerInvocation),
            Math.Clamp(component.MaxVariables, 1, VodkaRuntimeComponent.HardMaxVariables),
            Math.Clamp(component.MaxStringBytes, 1, VodkaRuntimeComponent.HardMaxStringBytes),
            Math.Clamp(component.MaxDataBytes, 1, VodkaRuntimeComponent.HardMaxDataBytes),
            Math.Clamp(component.MaxOutputBytes, 1, VodkaRuntimeComponent.HardMaxOutputBytes),
            Math.Clamp(component.MaxOperandStack, 1, VodkaRuntimeComponent.HardMaxOperandStack),
            Math.Clamp(component.MaxCompatibilityStack, 1, VodkaRuntimeComponent.HardMaxCompatibilityStack),
            Math.Clamp(component.MaxArguments, 0, VodkaRuntimeComponent.HardMaxArguments),
            Math.Clamp(component.MaxArgumentBytes, 0, VodkaRuntimeComponent.HardMaxArgumentBytes),
            Math.Clamp(component.MaxCallDepth, 1, VodkaRuntimeComponent.HardMaxCallDepth),
            TimeSpan.FromSeconds(Math.Clamp(timeout, 0.001f, VodkaRuntimeComponent.HardMaxLogicalTimeoutSeconds)));
    }

    public static VodkaRuntimeLimits Default => FromComponent(new VodkaRuntimeComponent());
}

internal readonly record struct VodkaSliceResult(
    VodkaExecutionState State,
    int InstructionsConsumed,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    VodkaValue ReturnValue,
    string ErrorCode);

internal readonly record struct VodkaCompletedOutput(
    string StandardOutput,
    string StandardError,
    int ExitCode,
    string ErrorCode);

internal readonly record struct VodkaActiveScript(
    bool CaptureOutput,
    DwaineProcessId? ParentId);

internal readonly record struct VodkaCapturedOutput(
    DwaineProcessId? ParentId,
    VodkaCompletedOutput Output);

internal readonly record struct VodkaStartResult(
    VodkaSpawnResult Result,
    DwaineProcessId ProcessId,
    string Error)
{
    public bool Succeeded => Result == VodkaSpawnResult.Success;
}

internal interface IVodkaRuntimeHost
{
    TimeSpan Now { get; }

    VodkaHostCallResult Invoke(string name, IReadOnlyList<VodkaValue> arguments);
}

internal enum VodkaHostCallStatus : byte
{
    Success,
    UnknownFunction,
    InvalidArguments,
    AccessDenied,
    Unavailable,
}

internal readonly record struct VodkaHostCallResult(
    VodkaHostCallStatus Status,
    VodkaValue Value,
    string Error)
{
    public static VodkaHostCallResult Success(VodkaValue value)
    {
        return new VodkaHostCallResult(VodkaHostCallStatus.Success, value, string.Empty);
    }

    public static VodkaHostCallResult Failure(
        VodkaHostCallStatus status,
        string error)
    {
        return new VodkaHostCallResult(status, VodkaValue.Null, error);
    }
}
