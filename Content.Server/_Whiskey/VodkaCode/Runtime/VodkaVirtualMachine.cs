// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.VodkaCode.Frontend;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

/// <summary>
/// Resumable deterministic bytecode interpreter. A caller supplies an explicit logical slice;
/// the VM never observes frame time, starts a Task, or executes host code outside its narrow ABI.
/// </summary>
internal sealed class VodkaVirtualMachine
{
    private const int InstructionCostBytes = 256;
    private const int MaximumInstructionCost = 64;

    private readonly VodkaCompiledProgram _program;
    private readonly VodkaRuntimeLimits _limits;
    private readonly IVodkaRuntimeHost _host;
    private readonly VodkaValue[] _arguments;
    private readonly List<VodkaValue> _operands = [];
    private readonly List<VodkaValue> _compatibilityStack = [];
    private readonly List<Dictionary<string, VodkaValue>> _scopes = [new(StringComparer.Ordinal)];
    private readonly TimeSpan _startedAt;
    private readonly DeterministicGenerator _random;

    private int _instructionPointer;
    private int _instructionsConsumed;
    private int _variableCount;
    private int _dataBytes;
    private int _outputBytes;
    private bool _cancelled;
    private bool _yieldRequested;
    private VodkaExecutionState _state = VodkaExecutionState.Ready;
    private VodkaValue _returnValue = VodkaValue.Null;
    private int _exitCode;
    private string _errorCode = string.Empty;

    public VodkaExecutionState State => _state;
    public int InstructionsConsumed => _instructionsConsumed;
    public VodkaValue ReturnValue => _returnValue;
    public int ExitCode => _exitCode;
    public string ErrorCode => _errorCode;

    public VodkaVirtualMachine(
        VodkaCompiledProgram program,
        VodkaRuntimeLimits limits,
        IVodkaRuntimeHost host,
        IReadOnlyList<string>? arguments = null,
        ulong seed = 1)
    {
        _program = program;
        _limits = limits;
        _host = host;
        _startedAt = host.Now;
        _random = new DeterministicGenerator(seed);

        var supplied = arguments ?? Array.Empty<string>();
        if (supplied.Count > limits.MaxArguments)
        {
            _arguments = [];
            SetTerminalFault("argument-limit-exceeded");
            return;
        }

        _arguments = new VodkaValue[supplied.Count];
        for (var index = 0; index < supplied.Count; index++)
        {
            var value = VodkaValue.FromString(supplied[index]);
            if (!IsValidValue(value)
                || value.DataBytes > limits.MaxArgumentBytes - _dataBytes)
            {
                _arguments = [];
                _dataBytes = 0;
                SetTerminalFault("argument-limit-exceeded");
                return;
            }

            _arguments[index] = value;
            _dataBytes += value.DataBytes;
        }
    }

    private VodkaVirtualMachine(
        VodkaVirtualMachine source,
        IVodkaRuntimeHost host,
        IReadOnlyList<string> arguments)
    {
        _program = source._program;
        _limits = source._limits;
        _host = host;
        _startedAt = source._startedAt;
        _random = new DeterministicGenerator(source._random);
        _instructionPointer = source._instructionPointer;
        _instructionsConsumed = source._instructionsConsumed;
        _variableCount = source._variableCount;
        _dataBytes = source._dataBytes - source._arguments.Sum(value => value.DataBytes);
        _outputBytes = source._outputBytes;
        _state = VodkaExecutionState.Ready;

        _arguments = new VodkaValue[arguments.Count];
        if (arguments.Count > _limits.MaxArguments)
        {
            SetTerminalFault("argument-limit-exceeded");
            return;
        }
        for (var index = 0; index < arguments.Count; index++)
        {
            var value = VodkaValue.FromString(arguments[index]);
            if (!IsValidValue(value) || value.DataBytes > _limits.MaxArgumentBytes - _dataBytes)
            {
                SetTerminalFault("argument-limit-exceeded");
                return;
            }
            _arguments[index] = value;
            _dataBytes += value.DataBytes;
        }

        _operands.AddRange(source._operands);
        _compatibilityStack.AddRange(source._compatibilityStack);
        _scopes.Clear();
        foreach (var scope in source._scopes)
            _scopes.Add(new Dictionary<string, VodkaValue>(scope, StringComparer.Ordinal));
    }

    public VodkaVirtualMachine Fork(IVodkaRuntimeHost host, IReadOnlyList<string> arguments)
    {
        return new VodkaVirtualMachine(this, host, arguments);
    }

    public bool TryAcceptForkResult(long value)
    {
        if (_state == VodkaExecutionState.Faulted)
            return false;
        return TryPush(VodkaValue.FromInteger(value), default, new StringBuilder());
    }

    public void Cancel()
    {
        _cancelled = true;
    }

    public void RequestYield()
    {
        _yieldRequested = true;
    }

    public VodkaSliceResult ExecuteSlice(int instructionBudget)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var sliceInstructions = 0;

        if (_state is VodkaExecutionState.Returned
            or VodkaExecutionState.Exited
            or VodkaExecutionState.Faulted
            or VodkaExecutionState.Cancelled)
        {
            return Result(sliceInstructions, output, error);
        }

        if (_cancelled)
        {
            _state = VodkaExecutionState.Cancelled;
            _exitCode = 130;
            _errorCode = "cancelled";
            AppendTerminalError(error, "vodka: process cancelled\n");
            return Result(sliceInstructions, output, error);
        }

        if (_host.Now - _startedAt >= _limits.LogicalTimeout)
        {
            Fault("logical-timeout", "process terminated: logical timeout exceeded", null, error);
            return Result(sliceInstructions, output, error);
        }

        var budget = Math.Max(0, instructionBudget);
        _state = VodkaExecutionState.Ready;
        while (sliceInstructions < budget)
        {
            if (_instructionsConsumed >= _limits.MaxInstructions)
            {
                Fault("instruction-budget-exceeded", "process terminated: instruction budget exceeded", null, error);
                break;
            }

            if (_instructionPointer < 0 || _instructionPointer >= _program.Instructions.Count)
            {
                Fault("invalid-instruction-pointer", "runtime entered an invalid instruction", null, error);
                break;
            }

            var instruction = _program.Instructions[_instructionPointer];
            var instructionCost = Math.Min(EstimateInstructionCost(instruction), budget);
            if (instructionCost > budget - sliceInstructions)
                break;
            if (instructionCost > _limits.MaxInstructions - _instructionsConsumed)
            {
                Fault("instruction-budget-exceeded", "process terminated: instruction budget exceeded", null, error);
                break;
            }

            _instructionPointer++;
            sliceInstructions += instructionCost;
            _instructionsConsumed += instructionCost;
            if (!ExecuteInstruction(instruction, output, error))
                break;
            if (_yieldRequested)
            {
                _yieldRequested = false;
                break;
            }
        }

        if (_state == VodkaExecutionState.Ready)
            _state = VodkaExecutionState.Yielded;
        return Result(sliceInstructions, output, error);
    }

    private bool ExecuteInstruction(
        VodkaInstruction instruction,
        StringBuilder output,
        StringBuilder error)
    {
        switch (instruction.OpCode)
        {
            case VodkaOpCode.Push:
                return TryPush(instruction.Value, instruction.Span, error);
            case VodkaOpCode.Pop:
                return TryPop(out _, instruction.Span, error);
            case VodkaOpCode.Load:
                if (!TryGetVariable(instruction.Text, out var loaded))
                    return Fault("undefined-variable", $"undefined variable: {instruction.Text}", instruction.Span, error);
                return TryPush(loaded, instruction.Span, error);
            case VodkaOpCode.Declare:
                return Declare(instruction.Text, instruction.Span, error);
            case VodkaOpCode.Store:
                return Store(instruction.Text, instruction.Span, error);
            case VodkaOpCode.EnterScope:
                if (_scopes.Count - 1 >= _limits.MaxCallDepth)
                    return Fault("scope-depth-exceeded", "scope depth limit exceeded", instruction.Span, error);
                _scopes.Add(new Dictionary<string, VodkaValue>(StringComparer.Ordinal));
                return true;
            case VodkaOpCode.ExitScope:
                return ExitScopes(1, instruction.Span, error);
            case VodkaOpCode.LeaveScopes:
                return ExitScopes(instruction.Operand, instruction.Span, error);
            case VodkaOpCode.Negate:
                return UnaryInteger(instruction, error, value => checked(-value));
            case VodkaOpCode.Not:
                if (!TryPop(out var negated, instruction.Span, error))
                    return false;
                return negated.Kind == VodkaValueKind.Boolean
                    ? TryPush(VodkaValue.FromBoolean(!negated.Boolean), instruction.Span, error)
                    : Fault("type-error", "operator 'not' requires a boolean", instruction.Span, error);
            case VodkaOpCode.Add:
            case VodkaOpCode.Subtract:
            case VodkaOpCode.Multiply:
            case VodkaOpCode.Divide:
            case VodkaOpCode.Modulo:
                return Arithmetic(instruction, error);
            case VodkaOpCode.Equal:
            case VodkaOpCode.NotEqual:
                return Equality(instruction, error);
            case VodkaOpCode.Less:
            case VodkaOpCode.LessOrEqual:
            case VodkaOpCode.Greater:
            case VodkaOpCode.GreaterOrEqual:
                return Relation(instruction, error);
            case VodkaOpCode.Xor:
                return BooleanXor(instruction, error);
            case VodkaOpCode.Jump:
                return Jump(instruction.Operand, instruction.Span, error);
            case VodkaOpCode.JumpIfFalse:
            case VodkaOpCode.JumpIfTrue:
                return ConditionalJump(instruction, error);
            case VodkaOpCode.Call:
                return Call(instruction, output, error);
            case VodkaOpCode.Return:
                if (!TryPop(out _returnValue, instruction.Span, error))
                    return false;
                _state = VodkaExecutionState.Returned;
                _exitCode = 0;
                return false;
            case VodkaOpCode.Exit:
                if (instruction.Operand == 0)
                {
                    _exitCode = 0;
                }
                else
                {
                    if (!TryPop(out var code, instruction.Span, error))
                        return false;
                    if (code.Kind != VodkaValueKind.Integer || code.Integer is < int.MinValue or > int.MaxValue)
                        return Fault("invalid-exit-code", "exit code must be a signed 32-bit integer", instruction.Span, error);
                    _exitCode = (int) code.Integer;
                }
                _state = VodkaExecutionState.Exited;
                return false;
            default:
                return Fault("invalid-opcode", "runtime encountered an invalid opcode", instruction.Span, error);
        }
    }

    private bool Declare(string name, VodkaSourceSpan span, StringBuilder error)
    {
        if (!TryPop(out var value, span, error))
            return false;
        var scope = _scopes[^1];
        if (scope.ContainsKey(name))
            return Fault("duplicate-variable", $"variable already declared in this scope: {name}", span, error);
        if (_variableCount >= _limits.MaxVariables || value.DataBytes > _limits.MaxDataBytes - _dataBytes)
            return Fault("data-limit-exceeded", "variable data limit exceeded", span, error);

        scope.Add(name, value);
        _variableCount++;
        _dataBytes += value.DataBytes;
        return true;
    }

    private bool Store(string name, VodkaSourceSpan span, StringBuilder error)
    {
        if (!TryPop(out var value, span, error))
            return false;
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (!_scopes[index].TryGetValue(name, out var previous))
                continue;
            var nextBytes = _dataBytes - previous.DataBytes + value.DataBytes;
            if (nextBytes > _limits.MaxDataBytes)
                return Fault("data-limit-exceeded", "variable data limit exceeded", span, error);
            _scopes[index][name] = value;
            _dataBytes = nextBytes;
            return true;
        }

        return Fault("undefined-variable", $"cannot assign undeclared variable: {name}", span, error);
    }

    private bool ExitScopes(int count, VodkaSourceSpan span, StringBuilder error)
    {
        if (count < 0 || count >= _scopes.Count)
            return Fault("invalid-scope", "runtime scope stack is invalid", span, error);
        for (var removed = 0; removed < count; removed++)
        {
            var scope = _scopes[^1];
            foreach (var value in scope.Values)
                _dataBytes -= value.DataBytes;
            _variableCount -= scope.Count;
            _scopes.RemoveAt(_scopes.Count - 1);
        }
        return true;
    }

    private bool UnaryInteger(VodkaInstruction instruction, StringBuilder error, Func<long, long> operation)
    {
        if (!TryPop(out var operand, instruction.Span, error))
            return false;
        if (operand.Kind != VodkaValueKind.Integer)
            return Fault("type-error", "unary '-' requires an integer", instruction.Span, error);
        try
        {
            return TryPush(VodkaValue.FromInteger(operation(operand.Integer)), instruction.Span, error);
        }
        catch (OverflowException)
        {
            return Fault("integer-overflow", "integer overflow", instruction.Span, error);
        }
    }

    private bool Arithmetic(VodkaInstruction instruction, StringBuilder error)
    {
        if (!TryPopPair(out var left, out var right, instruction.Span, error))
            return false;
        if (instruction.OpCode == VodkaOpCode.Add
            && left.Kind == VodkaValueKind.String
            && right.Kind == VodkaValueKind.String)
        {
            return TryPush(VodkaValue.FromString(left.Text + right.Text), instruction.Span, error);
        }
        if (left.Kind != VodkaValueKind.Integer || right.Kind != VodkaValueKind.Integer)
            return Fault("type-error", "arithmetic requires two integers or two strings for '+'", instruction.Span, error);

        if (instruction.OpCode is VodkaOpCode.Divide or VodkaOpCode.Modulo && right.Integer == 0)
            return Fault("division-by-zero", "division by zero", instruction.Span, error);
        try
        {
            var result = instruction.OpCode switch
            {
                VodkaOpCode.Add => checked(left.Integer + right.Integer),
                VodkaOpCode.Subtract => checked(left.Integer - right.Integer),
                VodkaOpCode.Multiply => checked(left.Integer * right.Integer),
                VodkaOpCode.Divide => checked(left.Integer / right.Integer),
                VodkaOpCode.Modulo => left.Integer % right.Integer,
                _ => throw new InvalidOperationException(),
            };
            return TryPush(VodkaValue.FromInteger(result), instruction.Span, error);
        }
        catch (OverflowException)
        {
            return Fault("integer-overflow", "integer overflow", instruction.Span, error);
        }
    }

    private bool Equality(VodkaInstruction instruction, StringBuilder error)
    {
        if (!TryPopPair(out var left, out var right, instruction.Span, error))
            return false;
        if (left.Kind != right.Kind)
            return Fault("type-error", "equality requires values of the same kind", instruction.Span, error);
        var equal = left.Kind switch
        {
            VodkaValueKind.Null => true,
            VodkaValueKind.Integer => left.Integer == right.Integer,
            VodkaValueKind.Boolean => left.Boolean == right.Boolean,
            VodkaValueKind.String => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            VodkaValueKind.Handle => left.Handle == right.Handle,
            _ => false,
        };
        return TryPush(
            VodkaValue.FromBoolean(instruction.OpCode == VodkaOpCode.Equal ? equal : !equal),
            instruction.Span,
            error);
    }

    private bool Relation(VodkaInstruction instruction, StringBuilder error)
    {
        if (!TryPopPair(out var left, out var right, instruction.Span, error))
            return false;
        int comparison;
        if (left.Kind == VodkaValueKind.Integer && right.Kind == VodkaValueKind.Integer)
            comparison = left.Integer.CompareTo(right.Integer);
        else if (left.Kind == VodkaValueKind.String && right.Kind == VodkaValueKind.String)
            comparison = string.CompareOrdinal(left.Text, right.Text);
        else
            return Fault("type-error", "relational operators require two integers or two strings", instruction.Span, error);

        var result = instruction.OpCode switch
        {
            VodkaOpCode.Less => comparison < 0,
            VodkaOpCode.LessOrEqual => comparison <= 0,
            VodkaOpCode.Greater => comparison > 0,
            VodkaOpCode.GreaterOrEqual => comparison >= 0,
            _ => false,
        };
        return TryPush(VodkaValue.FromBoolean(result), instruction.Span, error);
    }

    private bool BooleanXor(VodkaInstruction instruction, StringBuilder error)
    {
        if (!TryPopPair(out var left, out var right, instruction.Span, error))
            return false;
        return left.Kind == VodkaValueKind.Boolean && right.Kind == VodkaValueKind.Boolean
            ? TryPush(VodkaValue.FromBoolean(left.Boolean ^ right.Boolean), instruction.Span, error)
            : Fault("type-error", "operator 'xor' requires two booleans", instruction.Span, error);
    }

    private bool ConditionalJump(VodkaInstruction instruction, StringBuilder error)
    {
        if (!TryPop(out var condition, instruction.Span, error))
            return false;
        if (condition.Kind != VodkaValueKind.Boolean)
            return Fault("type-error", "condition must be a boolean", instruction.Span, error);
        var shouldJump = instruction.OpCode == VodkaOpCode.JumpIfTrue
            ? condition.Boolean
            : !condition.Boolean;
        return !shouldJump || Jump(instruction.Operand, instruction.Span, error);
    }

    private bool Jump(int target, VodkaSourceSpan span, StringBuilder error)
    {
        if (target < 0 || target >= _program.Instructions.Count)
            return Fault("invalid-jump", "runtime jump target is invalid", span, error);
        _instructionPointer = target;
        return true;
    }

    private bool Call(
        VodkaInstruction instruction,
        StringBuilder output,
        StringBuilder error)
    {
        if (instruction.Operand < 0 || instruction.Operand > _operands.Count)
            return Fault("operand-underflow", "function argument stack underflow", instruction.Span, error);

        var arguments = new VodkaValue[instruction.Operand];
        for (var index = instruction.Operand - 1; index >= 0; index--)
        {
            if (!TryPop(out arguments[index], instruction.Span, error))
                return false;
        }

        if (TryInvokeStandard(instruction.Text, arguments, instruction.Span, output, error, out var standard))
            return _state != VodkaExecutionState.Faulted && TryPush(standard, instruction.Span, error);
        if (_state == VodkaExecutionState.Faulted)
            return false;

        var hostResult = _host.Invoke(instruction.Text, arguments);
        if (hostResult.Status == VodkaHostCallStatus.Exit)
        {
            _state = VodkaExecutionState.Exited;
            _exitCode = hostResult.ExitCode;
            return false;
        }
        return hostResult.Status == VodkaHostCallStatus.Success
            ? TryPush(hostResult.Value, instruction.Span, error)
            : Fault(
                hostResult.Status switch
                {
                    VodkaHostCallStatus.UnknownFunction => "unknown-function",
                    VodkaHostCallStatus.InvalidArguments => "invalid-arguments",
                    VodkaHostCallStatus.AccessDenied => "permission-denied",
                    VodkaHostCallStatus.NotFound => "not-found",
                    VodkaHostCallStatus.Conflict => "conflict",
                    VodkaHostCallStatus.RateLimited => "rate-limited",
                    VodkaHostCallStatus.LimitExceeded => "resource-limit-exceeded",
                    VodkaHostCallStatus.StaleHandle => "stale-handle",
                    VodkaHostCallStatus.Offline => "device-offline",
                    _ => "host-unavailable",
                },
                string.IsNullOrWhiteSpace(hostResult.Error)
                    ? $"host function failed: {instruction.Text}"
                    : hostResult.Error,
                instruction.Span,
                error);
    }

    private bool TryInvokeStandard(
        string name,
        IReadOnlyList<VodkaValue> arguments,
        VodkaSourceSpan span,
        StringBuilder output,
        StringBuilder error,
        out VodkaValue result)
    {
        result = VodkaValue.Null;
        switch (name)
        {
            case "print":
            case "console.writeln":
                if (!RequireCount(name, arguments, 1, span, error))
                    return true;
                AppendOutput(output, arguments[0].ToDisplayString() + "\n", span, error);
                return true;
            case "console.write":
                if (!RequireCount(name, arguments, 1, span, error))
                    return true;
                AppendOutput(output, arguments[0].ToDisplayString(), span, error);
                return true;
            case "rand":
                if (!RequireCount(name, arguments, 1, span, error)
                    || !RequireKind(name, arguments[0], VodkaValueKind.Integer, span, error))
                    return true;
                if (arguments[0].Integer <= 0)
                {
                    Fault("invalid-random-bound", "rand bound must be positive", span, error);
                    return true;
                }
                result = VodkaValue.FromInteger(_random.Next(arguments[0].Integer));
                return true;
            case "args.count":
                if (!RequireCount(name, arguments, 0, span, error))
                    return true;
                result = VodkaValue.FromInteger(_arguments.Length);
                return true;
            case "args.get":
                if (!RequireCount(name, arguments, 1, span, error)
                    || !RequireKind(name, arguments[0], VodkaValueKind.Integer, span, error))
                    return true;
                var argumentIndex = arguments[0].Integer;
                if (argumentIndex < 0 || argumentIndex >= _arguments.Length)
                {
                    Fault("argument-out-of-range", "argument index is out of range", span, error);
                    return true;
                }
                result = _arguments[(int) argumentIndex];
                return true;
            case "string.length":
                if (!OneString(name, arguments, span, error, out var lengthValue))
                    return true;
                result = VodkaValue.FromInteger(arguments[0].RuneCount);
                return true;
            case "string.lower":
                if (!OneString(name, arguments, span, error, out var lowerValue))
                    return true;
                result = VodkaValue.FromString(lowerValue.ToLowerInvariant());
                return true;
            case "string.upper":
                if (!OneString(name, arguments, span, error, out var upperValue))
                    return true;
                result = VodkaValue.FromString(upperValue.ToUpperInvariant());
                return true;
            case "string.contains":
                if (!RequireCount(name, arguments, 2, span, error)
                    || !RequireKind(name, arguments[0], VodkaValueKind.String, span, error)
                    || !RequireKind(name, arguments[1], VodkaValueKind.String, span, error))
                    return true;
                result = VodkaValue.FromBoolean(arguments[0].Text.Contains(arguments[1].Text, StringComparison.Ordinal));
                return true;
            case "string.slice":
                if (!RequireCount(name, arguments, 3, span, error)
                    || !RequireKind(name, arguments[0], VodkaValueKind.String, span, error)
                    || !RequireKind(name, arguments[1], VodkaValueKind.Integer, span, error)
                    || !RequireKind(name, arguments[2], VodkaValueKind.Integer, span, error))
                    return true;
                var runes = SplitRunes(arguments[0].Text);
                var start = arguments[1].Integer;
                var length = arguments[2].Integer;
                if (start < 0 || length < 0 || start > runes.Length || length > runes.Length - start)
                {
                    Fault("string-range-error", "string.slice range is invalid", span, error);
                    return true;
                }
                result = VodkaValue.FromString(string.Concat(runes.Skip((int) start).Take((int) length)));
                return true;
            case "string.repeat":
                if (!RequireCount(name, arguments, 2, span, error)
                    || !RequireKind(name, arguments[0], VodkaValueKind.String, span, error)
                    || !RequireKind(name, arguments[1], VodkaValueKind.Integer, span, error))
                    return true;
                if (arguments[1].Integer < 0 || arguments[1].Integer > _limits.MaxStringBytes)
                {
                    Fault("string-limit-exceeded", "string.repeat count exceeds the string limit", span, error);
                    return true;
                }
                var repetitions = (int) arguments[1].Integer;
                var sourceBytes = arguments[0].DataBytes;
                if ((long) sourceBytes * repetitions > _limits.MaxStringBytes)
                {
                    Fault("string-limit-exceeded", "string.repeat result exceeds the string limit", span, error);
                    return true;
                }
                result = VodkaValue.FromString(string.Concat(Enumerable.Repeat(arguments[0].Text, repetitions)));
                return true;
            case "stack.push":
                if (!RequireCount(name, arguments, 1, span, error))
                    return true;
                if (_compatibilityStack.Count >= _limits.MaxCompatibilityStack
                    || arguments[0].DataBytes > _limits.MaxDataBytes - _dataBytes)
                {
                    Fault("stack-limit-exceeded", "compatibility stack limit exceeded", span, error);
                    return true;
                }
                _compatibilityStack.Add(arguments[0]);
                _dataBytes += arguments[0].DataBytes;
                return true;
            case "stack.drop":
                if (!RequireCount(name, arguments, 0, span, error) || !TryPopCompatibility(span, error, out _))
                    return true;
                return true;
            case "stack.depth":
                if (!RequireCount(name, arguments, 0, span, error))
                    return true;
                result = VodkaValue.FromInteger(_compatibilityStack.Count);
                return true;
            case "stack.dup":
                if (!RequireCount(name, arguments, 0, span, error))
                    return true;
                if (_compatibilityStack.Count == 0)
                {
                    Fault("stack-underflow", "compatibility stack is empty", span, error);
                    return true;
                }
                var duplicate = _compatibilityStack[^1];
                if (_compatibilityStack.Count >= _limits.MaxCompatibilityStack
                    || duplicate.DataBytes > _limits.MaxDataBytes - _dataBytes)
                {
                    Fault("stack-limit-exceeded", "compatibility stack limit exceeded", span, error);
                    return true;
                }
                _compatibilityStack.Add(duplicate);
                _dataBytes += duplicate.DataBytes;
                return true;
            case "stack.pop":
                if (!RequireCount(name, arguments, 0, span, error)
                    || !TryPopCompatibility(span, error, out result))
                    return true;
                AppendOutput(output, result.ToDisplayString() + "\n", span, error);
                return true;
            case "stack.inspect":
                if (!RequireCount(name, arguments, 0, span, error))
                    return true;
                var lines = _compatibilityStack.Select(value => value.ToDisplayString());
                AppendOutput(
                    output,
                    $"<{_compatibilityStack.Count.ToString(CultureInfo.InvariantCulture)}>\n" +
                    string.Join('\n', lines) + (_compatibilityStack.Count > 0 ? "\n" : string.Empty),
                    span,
                    error);
                return true;
            default:
                return false;
        }
    }

    private bool TryPopCompatibility(VodkaSourceSpan span, StringBuilder error, out VodkaValue value)
    {
        if (_compatibilityStack.Count == 0)
        {
            value = VodkaValue.Null;
            Fault("stack-underflow", "compatibility stack is empty", span, error);
            return false;
        }
        value = _compatibilityStack[^1];
        _compatibilityStack.RemoveAt(_compatibilityStack.Count - 1);
        _dataBytes -= value.DataBytes;
        return true;
    }

    private bool OneString(
        string name,
        IReadOnlyList<VodkaValue> arguments,
        VodkaSourceSpan span,
        StringBuilder error,
        out string value)
    {
        value = string.Empty;
        if (!RequireCount(name, arguments, 1, span, error)
            || !RequireKind(name, arguments[0], VodkaValueKind.String, span, error))
            return false;
        value = arguments[0].Text;
        return true;
    }

    private bool RequireCount(
        string name,
        IReadOnlyList<VodkaValue> arguments,
        int expected,
        VodkaSourceSpan span,
        StringBuilder error)
    {
        return arguments.Count == expected
            || Fault("invalid-arguments", $"{name} expects {expected} argument(s)", span, error);
    }

    private bool RequireKind(
        string name,
        VodkaValue value,
        VodkaValueKind expected,
        VodkaSourceSpan span,
        StringBuilder error)
    {
        return value.Kind == expected
            || Fault("type-error", $"{name} received an invalid argument type", span, error);
    }

    private bool AppendOutput(StringBuilder output, string text, VodkaSourceSpan span, StringBuilder error)
    {
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > _limits.MaxOutputBytes - _outputBytes)
            return Fault("output-limit-exceeded", "process terminated: output limit exceeded", span, error);
        output.Append(text);
        _outputBytes += bytes;
        return true;
    }

    private bool TryGetVariable(string name, out VodkaValue value)
    {
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (_scopes[index].TryGetValue(name, out value))
                return true;
        }
        value = VodkaValue.Null;
        return false;
    }

    private bool TryPush(VodkaValue value, VodkaSourceSpan span, StringBuilder error)
    {
        if (!IsValidValue(value))
            return Fault("string-limit-exceeded", "string value exceeds the configured limit", span, error);
        if (_operands.Count >= _limits.MaxOperandStack || value.DataBytes > _limits.MaxDataBytes - _dataBytes)
            return Fault("data-limit-exceeded", "operand data limit exceeded", span, error);
        _operands.Add(value);
        _dataBytes += value.DataBytes;
        return true;
    }

    private bool TryPop(out VodkaValue value, VodkaSourceSpan span, StringBuilder error)
    {
        if (_operands.Count == 0)
        {
            value = VodkaValue.Null;
            return Fault("operand-underflow", "runtime operand stack underflow", span, error);
        }
        value = _operands[^1];
        _operands.RemoveAt(_operands.Count - 1);
        _dataBytes -= value.DataBytes;
        return true;
    }

    private bool TryPopPair(
        out VodkaValue left,
        out VodkaValue right,
        VodkaSourceSpan span,
        StringBuilder error)
    {
        left = VodkaValue.Null;
        right = VodkaValue.Null;
        if (!TryPop(out right, span, error))
            return false;
        return TryPop(out left, span, error);
    }

    private bool IsValidValue(VodkaValue value)
    {
        return value.Kind != VodkaValueKind.String
               || value.DataBytes <= _limits.MaxStringBytes && value.IsWellFormedString;
    }

    private int EstimateInstructionCost(VodkaInstruction instruction)
    {
        long bytes = 0;
        if (instruction.OpCode == VodkaOpCode.Call
            && instruction.Operand >= 0
            && instruction.Operand <= _operands.Count)
        {
            var start = _operands.Count - instruction.Operand;
            for (var index = start; index < _operands.Count; index++)
                bytes += _operands[index].DataBytes;

            if (instruction.Text == "string.repeat"
                && instruction.Operand == 2
                && _operands[start].Kind == VodkaValueKind.String
                && _operands[start + 1].Kind == VodkaValueKind.Integer
                && _operands[start + 1].Integer > 0)
            {
                var repetitions = Math.Min(_operands[start + 1].Integer, _limits.MaxStringBytes);
                bytes = Math.Max(bytes, Math.Min(
                    (long) _operands[start].DataBytes * repetitions,
                    _limits.MaxStringBytes));
            }

            if (instruction.Text == "stack.inspect")
            {
                foreach (var value in _compatibilityStack)
                    bytes += value.DataBytes;
            }
        }
        else if ((instruction.OpCode is VodkaOpCode.Add
                  or VodkaOpCode.Equal
                  or VodkaOpCode.NotEqual
                  or VodkaOpCode.Less
                  or VodkaOpCode.LessOrEqual
                  or VodkaOpCode.Greater
                  or VodkaOpCode.GreaterOrEqual)
                 && _operands.Count >= 2)
        {
            bytes = (long) _operands[^1].DataBytes + _operands[^2].DataBytes;
        }

        return Math.Clamp(1 + (int) Math.Min(bytes / InstructionCostBytes, MaximumInstructionCost - 1),
            1,
            MaximumInstructionCost);
    }

    private static string[] SplitRunes(string value)
    {
        var runes = new List<string>();
        foreach (var rune in value.EnumerateRunes())
            runes.Add(rune.ToString());
        return runes.ToArray();
    }

    private bool Fault(
        string code,
        string message,
        VodkaSourceSpan? span,
        StringBuilder error)
    {
        _state = VodkaExecutionState.Faulted;
        _exitCode = 1;
        _errorCode = code;
        var prefix = span is { } location
            ? $"vodka: line {location.Start.Line}:{location.Start.Column}: "
            : "vodka: ";
        AppendTerminalError(error, prefix + message + "\n");
        return false;
    }

    private void SetTerminalFault(string code)
    {
        _state = VodkaExecutionState.Faulted;
        _exitCode = 1;
        _errorCode = code;
    }

    private static void AppendTerminalError(StringBuilder error, string message)
    {
        const int hardDiagnosticCharacters = 4096;
        if (message.Length <= hardDiagnosticCharacters - error.Length)
            error.Append(message);
        else if (error.Length == 0)
            error.Append("vodka: process terminated: diagnostic limit exceeded\n");
    }

    private VodkaSliceResult Result(int instructions, StringBuilder output, StringBuilder error)
    {
        if (_state == VodkaExecutionState.Faulted && error.Length == 0)
            AppendTerminalError(error, $"vodka: process terminated: {_errorCode.Replace('-', ' ')}\n");
        return new VodkaSliceResult(
            _state,
            instructions,
            output.ToString(),
            error.ToString(),
            _exitCode,
            _returnValue,
            _errorCode);
    }

    private sealed class DeterministicGenerator
    {
        private ulong _state;

        public DeterministicGenerator(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public DeterministicGenerator(DeterministicGenerator source)
        {
            _state = source._state;
        }

        public long Next(long inclusiveMaximum)
        {
            var bound = (ulong) inclusiveMaximum;
            var threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value < threshold);
            return (long) (value % bound) + 1;
        }

        private ulong NextUInt64()
        {
            var value = _state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            _state = value;
            return value * 0x2545F4914F6CDD1DUL;
        }
    }
}
