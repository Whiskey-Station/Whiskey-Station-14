// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.VodkaCode.Frontend;
using System.Text;

namespace Content.Server._Whiskey.VodkaCode.Runtime;

internal enum VodkaOpCode : byte
{
    Push,
    Pop,
    Load,
    Declare,
    Store,
    EnterScope,
    ExitScope,
    LeaveScopes,
    Negate,
    Not,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Xor,
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    Call,
    Return,
    Exit,
}

internal readonly record struct VodkaInstruction(
    VodkaOpCode OpCode,
    VodkaSourceSpan Span,
    int Operand = 0,
    string Text = "",
    VodkaValue Value = default);

internal sealed record VodkaCompiledProgram(IReadOnlyList<VodkaInstruction> Instructions);

internal sealed record VodkaCompilationResult(
    VodkaCompiledProgram? Program,
    IReadOnlyList<VodkaDiagnostic> Diagnostics)
{
    public bool Succeeded => Program is not null && Diagnostics.Count == 0;
}

internal sealed class VodkaCompiler
{
    private const int MaxBytecodeInstructions = 131_072;

    private readonly List<VodkaInstruction> _instructions = [];
    private readonly List<VodkaDiagnostic> _diagnostics = [];
    private readonly int _maxDepth;
    private readonly Stack<LoopContext> _loops = [];
    private int _scopeDepth;
    private int _expressionDepth;

    private sealed class LoopContext(int conditionTarget, int scopeDepth)
    {
        public int ConditionTarget { get; } = conditionTarget;
        public int ScopeDepth { get; } = scopeDepth;
        public readonly List<int> BreakJumps = [];
    }

    private VodkaCompiler(int maxDepth)
    {
        _maxDepth = Math.Clamp(maxDepth, 1, 64);
    }

    public static VodkaCompilationResult Compile(string source, int maxDepth = 64)
    {
        var parsed = VodkaParser.Parse(source);
        if (!parsed.Succeeded)
            return new VodkaCompilationResult(null, parsed.Diagnostics);

        return new VodkaCompiler(maxDepth).Run(parsed.Program);
    }

    private VodkaCompilationResult Run(VodkaProgramSyntax program)
    {
        foreach (var statement in program.Statements)
        {
            CompileStatement(statement);
            if (_diagnostics.Count > 0 || _instructions.Count >= MaxBytecodeInstructions)
                break;
        }

        if (_diagnostics.Count == 0)
            Emit(VodkaOpCode.Exit, program.Span);

        return _diagnostics.Count == 0
            ? new VodkaCompilationResult(new VodkaCompiledProgram(_instructions.ToArray()), Array.Empty<VodkaDiagnostic>())
            : new VodkaCompilationResult(null, _diagnostics.ToArray());
    }

    private void CompileStatement(VodkaStatementSyntax statement)
    {
        if (!CanEmit(statement.Span))
            return;

        switch (statement)
        {
            case VodkaBlockStatementSyntax block:
                CompileBlock(block);
                break;
            case VodkaLetStatementSyntax declaration:
                if (declaration.Initializer is { } initializer)
                    CompileExpression(initializer);
                else
                    Emit(VodkaOpCode.Push, declaration.Span, value: VodkaValue.Null);
                Emit(VodkaOpCode.Declare, declaration.Span, text: declaration.Name);
                break;
            case VodkaAssignmentStatementSyntax assignment:
                CompileExpression(assignment.Value);
                Emit(VodkaOpCode.Store, assignment.Span, text: assignment.Name);
                break;
            case VodkaIfStatementSyntax conditional:
                CompileExpression(conditional.Condition);
                var falseJump = Emit(VodkaOpCode.JumpIfFalse, conditional.Condition.Span);
                CompileBlock(conditional.Then);
                if (conditional.Else is { } otherwise)
                {
                    var endJump = Emit(VodkaOpCode.Jump, conditional.Span);
                    Patch(falseJump, _instructions.Count);
                    CompileBlock(otherwise);
                    Patch(endJump, _instructions.Count);
                }
                else
                {
                    Patch(falseJump, _instructions.Count);
                }
                break;
            case VodkaWhileStatementSyntax loop:
                var conditionTarget = _instructions.Count;
                CompileExpression(loop.Condition);
                var exitJump = Emit(VodkaOpCode.JumpIfFalse, loop.Condition.Span);
                var context = new LoopContext(conditionTarget, _scopeDepth);
                _loops.Push(context);
                CompileBlock(loop.Body);
                _loops.Pop();
                Emit(VodkaOpCode.Jump, loop.Span, conditionTarget);
                var end = _instructions.Count;
                Patch(exitJump, end);
                foreach (var jump in context.BreakJumps)
                    Patch(jump, end);
                break;
            case VodkaBreakStatementSyntax:
                if (_loops.TryPeek(out var breakLoop))
                {
                    Emit(VodkaOpCode.LeaveScopes, statement.Span, _scopeDepth - breakLoop.ScopeDepth);
                    breakLoop.BreakJumps.Add(Emit(VodkaOpCode.Jump, statement.Span));
                }
                break;
            case VodkaContinueStatementSyntax:
                if (_loops.TryPeek(out var continueLoop))
                {
                    Emit(VodkaOpCode.LeaveScopes, statement.Span, _scopeDepth - continueLoop.ScopeDepth);
                    Emit(VodkaOpCode.Jump, statement.Span, continueLoop.ConditionTarget);
                }
                break;
            case VodkaReturnStatementSyntax returned:
                if (returned.Value is { } returnValue)
                    CompileExpression(returnValue);
                else
                    Emit(VodkaOpCode.Push, returned.Span, value: VodkaValue.Null);
                Emit(VodkaOpCode.Return, returned.Span);
                break;
            case VodkaExitStatementSyntax exited:
                if (exited.Code is { } exitCode)
                    CompileExpression(exitCode);
                else
                    Emit(VodkaOpCode.Push, exited.Span, value: VodkaValue.FromInteger(0));
                Emit(VodkaOpCode.Exit, exited.Span, operand: 1);
                break;
            case VodkaExpressionStatementSyntax expression:
                CompileExpression(expression.Expression);
                Emit(VodkaOpCode.Pop, expression.Span);
                break;
        }
    }

    private void CompileBlock(VodkaBlockStatementSyntax block)
    {
        Emit(VodkaOpCode.EnterScope, block.Span);
        _scopeDepth++;
        foreach (var statement in block.Statements)
        {
            CompileStatement(statement);
            if (_diagnostics.Count > 0)
                break;
        }
        _scopeDepth--;
        Emit(VodkaOpCode.ExitScope, block.Span);
    }

    private void CompileExpression(VodkaExpressionSyntax expression)
    {
        if (_expressionDepth >= _maxDepth)
        {
            AddDiagnostic(VodkaDiagnosticCode.SyntaxDepthExceeded, "runtime expression nesting limit exceeded", expression.Span);
            return;
        }

        _expressionDepth++;
        try
        {
            switch (expression)
            {
                case VodkaLiteralExpressionSyntax literal:
                    Emit(VodkaOpCode.Push, literal.Span, value: Literal(literal));
                    break;
                case VodkaIdentifierExpressionSyntax identifier:
                    Emit(VodkaOpCode.Load, identifier.Span, text: identifier.Name);
                    break;
                case VodkaUnaryExpressionSyntax unary:
                    CompileExpression(unary.Operand);
                    Emit(unary.Operator == VodkaUnaryOperator.Not ? VodkaOpCode.Not : VodkaOpCode.Negate, unary.Span);
                    break;
                case VodkaBinaryExpressionSyntax binary:
                    CompileBinary(binary);
                    break;
                case VodkaCallExpressionSyntax call:
                    CompileCall(call);
                    break;
                case VodkaMemberExpressionSyntax member:
                    AddDiagnostic(VodkaDiagnosticCode.UnexpectedToken, "member access is only valid as a function target", member.Span);
                    break;
                case VodkaErrorExpressionSyntax error:
                    AddDiagnostic(VodkaDiagnosticCode.UnexpectedToken, "cannot compile an invalid expression", error.Span);
                    break;
            }
        }
        finally
        {
            _expressionDepth--;
        }
    }

    private void CompileBinary(VodkaBinaryExpressionSyntax binary)
    {
        CompileExpression(binary.Left);
        if (binary.Operator is VodkaBinaryOperator.And or VodkaBinaryOperator.Or)
        {
            var shortcut = Emit(
                binary.Operator == VodkaBinaryOperator.And ? VodkaOpCode.JumpIfFalse : VodkaOpCode.JumpIfTrue,
                binary.Left.Span);
            CompileExpression(binary.Right);
            var end = Emit(VodkaOpCode.Jump, binary.Span);
            Patch(shortcut, _instructions.Count);
            Emit(
                VodkaOpCode.Push,
                binary.Span,
                value: VodkaValue.FromBoolean(binary.Operator == VodkaBinaryOperator.Or));
            Patch(end, _instructions.Count);
            return;
        }

        CompileExpression(binary.Right);
        Emit(binary.Operator switch
        {
            VodkaBinaryOperator.Add => VodkaOpCode.Add,
            VodkaBinaryOperator.Subtract => VodkaOpCode.Subtract,
            VodkaBinaryOperator.Multiply => VodkaOpCode.Multiply,
            VodkaBinaryOperator.Divide => VodkaOpCode.Divide,
            VodkaBinaryOperator.Modulo => VodkaOpCode.Modulo,
            VodkaBinaryOperator.Equal => VodkaOpCode.Equal,
            VodkaBinaryOperator.NotEqual => VodkaOpCode.NotEqual,
            VodkaBinaryOperator.Less => VodkaOpCode.Less,
            VodkaBinaryOperator.LessOrEqual => VodkaOpCode.LessOrEqual,
            VodkaBinaryOperator.Greater => VodkaOpCode.Greater,
            VodkaBinaryOperator.GreaterOrEqual => VodkaOpCode.GreaterOrEqual,
            VodkaBinaryOperator.Xor => VodkaOpCode.Xor,
            _ => throw new InvalidOperationException("short-circuit operators are compiled separately"),
        }, binary.Span);
    }

    private void CompileCall(VodkaCallExpressionSyntax call)
    {
        if (!TryGetCallName(call.Target, out var name))
        {
            AddDiagnostic(VodkaDiagnosticCode.UnexpectedToken, "function target must be a static name", call.Target.Span);
            return;
        }

        foreach (var argument in call.Arguments)
            CompileExpression(argument);
        Emit(VodkaOpCode.Call, call.Span, call.Arguments.Count, name);
    }

    private static bool TryGetCallName(VodkaExpressionSyntax expression, out string name)
    {
        const int maximumNameLength = 128;
        var members = new List<string>();
        var length = 0;
        while (expression is VodkaMemberExpressionSyntax member)
        {
            if (members.Count >= maximumNameLength
                || member.Member.Length + 1 > maximumNameLength - length)
            {
                name = string.Empty;
                return false;
            }

            members.Add(member.Member);
            length += member.Member.Length + 1;
            expression = member.Target;
        }

        if (expression is not VodkaIdentifierExpressionSyntax identifier
            || identifier.Name.Length > maximumNameLength - length)
        {
            name = string.Empty;
            return false;
        }

        var builder = new StringBuilder(identifier.Name, length + identifier.Name.Length);
        for (var index = members.Count - 1; index >= 0; index--)
            builder.Append('.').Append(members[index]);
        name = builder.ToString();
        return true;
    }

    private static VodkaValue Literal(VodkaLiteralExpressionSyntax literal)
    {
        return literal.Kind switch
        {
            VodkaLiteralKind.Integer => VodkaValue.FromInteger((long) literal.Value!),
            VodkaLiteralKind.String => VodkaValue.FromString((string) literal.Value!),
            VodkaLiteralKind.Boolean => VodkaValue.FromBoolean((bool) literal.Value!),
            _ => VodkaValue.Null,
        };
    }

    private int Emit(
        VodkaOpCode opCode,
        VodkaSourceSpan span,
        int operand = 0,
        string text = "",
        VodkaValue value = default)
    {
        if (!CanEmit(span))
            return Math.Max(0, _instructions.Count - 1);

        _instructions.Add(new VodkaInstruction(opCode, span, operand, text, value));
        return _instructions.Count - 1;
    }

    private bool CanEmit(VodkaSourceSpan span)
    {
        if (_instructions.Count < MaxBytecodeInstructions)
            return true;

        if (_diagnostics.Count == 0)
            AddDiagnostic(VodkaDiagnosticCode.TokenLimitExceeded, "compiled program is too large", span);
        return false;
    }

    private void Patch(int instruction, int target)
    {
        if (instruction < 0 || instruction >= _instructions.Count)
            return;
        _instructions[instruction] = _instructions[instruction] with { Operand = target };
    }

    private void AddDiagnostic(VodkaDiagnosticCode code, string message, VodkaSourceSpan span)
    {
        if (_diagnostics.Count < 16)
            _diagnostics.Add(new VodkaDiagnostic(code, message, span));
    }
}
