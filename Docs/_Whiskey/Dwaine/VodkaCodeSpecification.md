<!-- SPDX-FileCopyrightText: 2026 Whiskey Station Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# Vodka Code language specification

Version: 0.1 (initial normative specification)

Canonical extension: `.vodka`

Implementation status: frontend delivered in PR 10, deterministic process runtime delivered in PR 11, and capability/syscall bindings assigned to PR 12-15.

Vodka Code is Whiskey DWAINE's native, deterministic scripting language. DWAINE is the operating environment; Vodka Code is the language executed inside it. It is an original Whiskey design intended to express the useful automation behaviors of the reference environment without executing or translating DM.

## Execution model

- Source is parsed into a Whiskey-owned AST or equivalent bytecode and executed by a purpose-built server VM.
- Execution is deterministic for the same source, inputs, authoritative state, logical clock, and explicitly supplied pseudo-random seed.
- A script runs as a DWAINE process with its own principal, working directory, environment, standard streams, cancellation token, and capability table.
- Scripts cannot access C# reflection, Roslyn, native processes, the host filesystem, unrestricted sockets, the dependency container, or arbitrary entities.
- World access occurs only through validated syscalls and opaque device capabilities.
- A runtime fault terminates only the affected process unless the kernel itself is explicitly shutting down.

## Source and lexical grammar

Source uses UTF-8 text with normalized line endings. Keywords are lowercase and identifiers are case-sensitive.

```ebnf
letter       = "A"…"Z" | "a"…"z" | "_" ;
digit        = "0"…"9" ;
identifier   = letter, { letter | digit } ;
integer      = digit, { digit } ;
string       = '"', { character | escape }, '"' ;
escape       = "\\n" | "\\r" | "\\t" | "\\\"" | "\\\\" ;
comment      = "#", { character - newline }, newline ;
```

Whitespace separates tokens but is otherwise insignificant. Every token retains a one-based line and column for diagnostics. Unterminated strings, invalid escapes, invalid UTF-8, and unexpected characters are syntax errors.

## Values

The core value kinds are:

- signed 64-bit integer, with checked overflow;
- boolean (`true`, `false`);
- immutable string;
- `null`;
- bounded opaque handle values introduced by host APIs.

There is no implicit host object, entity identifier, floating-point value, wall-clock object, or executable code value. String length and aggregate process data are bounded. Conversions are explicit except that conditions accept only booleans.

## Grammar

```ebnf
program      = { statement } ;
statement    = block
             | "let", identifier, [ "=", expression ], ";"
             | identifier, "=", expression, ";"
             | "if", "(", expression, ")", block, [ "else", block ]
             | "while", "(", expression, ")", block
             | "break", ";"
             | "continue", ";"
             | "return", [ expression ], ";"
             | "exit", [ expression ], ";"
             | expression, ";" ;
block        = "{", { statement }, "}" ;
expression   = logical_or ;
logical_or   = logical_xor, { "or", logical_xor } ;
logical_xor  = logical_and, { ( "xor" | "eor" ), logical_and } ;
logical_and  = equality, { "and", equality } ;
equality     = relation, { ( "==" | "!=" ), relation } ;
relation     = sum, { ( "<" | "<=" | ">" | ">=" ), sum } ;
sum          = product, { ( "+" | "-" ), product } ;
product      = unary, { ( "*" | "/" | "%" ), unary } ;
unary        = ( "not" | "!" | "-" ), unary | call ;
call         = primary, { ".", identifier | "(", [ arguments ], ")" } ;
arguments    = expression, { ",", expression } ;
primary      = integer | string | "true" | "false" | "null"
             | identifier | "(", expression, ")" ;
```

PR 10 implements the bounded lexer, parser, source spans, diagnostics, and AST for this grammar. PR 11 implements the deterministic runtime, parity operators, string operations, file predicates, nested control flow, and full-script fixtures. The syntax may only change through synchronized code, tests, this specification, and user documentation.

## Operator semantics

- Integer arithmetic is checked. Overflow, division by zero, and modulo by zero are runtime errors.
- `+` adds integers or concatenates strings when both operands are strings. Mixed-type arithmetic is an error.
- Relational operators compare integers numerically and strings by ordinal code-point order.
- `==` and `!=` compare only values of the same kind, except that `null` compares with `null`.
- `and`, `or`, `xor`/`eor`, and `not`/`!` require booleans and do not expose bitwise host behavior.
- `and` and `or` short-circuit. Evaluation order is left to right.
- Assignment returns no value and cannot create an implicit variable; `let` declares a variable in the current lexical scope.

The reference stack/RPN operators are parity requirements, not the surface syntax of Vodka Code. PR 11 supplies equivalent deterministic language or standard-library behavior for stack inspection, string escaping, assignment, arithmetic, logic, relations, random values, and file predicates.

## PR 11 standard library

These names are implemented by the VM and are part of Vodka Code 0.1:

| Function | Contract |
| --- | --- |
| `print(value)` | Write the value and a newline to bounded stdout. |
| `console.write(value)` | Write the value without a newline. |
| `console.writeln(value)` | Write the value and a newline. |
| `rand(maximum)` | Return a deterministic integer in the inclusive range 1 through `maximum`. |
| `args.count()` | Return the number of server-validated script arguments. |
| `args.get(index)` | Return the zero-based argument or fault when out of range. |
| `string.length(text)` | Count Unicode scalar values. |
| `string.lower(text)` / `string.upper(text)` | Perform invariant case conversion. |
| `string.contains(text, part)` | Perform ordinal substring matching. |
| `string.slice(text, start, length)` | Return a scalar-indexed, bounds-checked substring. |
| `string.repeat(text, count)` | Return a repeated string only when it fits the string ceiling. |
| `stack.push(value)` / `stack.drop()` | Push or discard on the bounded compatibility stack. |
| `stack.depth()` / `stack.dup()` | Inspect depth or duplicate the top value. |
| `stack.pop()` / `stack.inspect()` | Pop-and-print or print the bounded stack without exposing VM internals. |
| `fs.exists(path)` | Return false for missing, broken, or unreadable paths. |
| `fs.is_directory(path)` / `fs.is_file(path)` | Query readable VFS metadata relative to the process working directory. |
| `fs.is_executable(path)` | Require a program node and current execute permission. |

The `fs.*` functions above are deliberately read-only predicates. VFS mutation and broader file access enter through the validated syscall layer in PR 12; scripts never receive a VFS node object.

## Control flow and process result

`break` and `continue` are valid only inside a loop. `return` exits the current script entry point and supplies a value to its parent process when the invocation contract accepts one. `exit` requests process termination with an integer exit code; a missing code means zero. End of source also exits with zero.

Process output is written through bounded `stdout` and `stderr`. Shell pipelines and command substitution consume those streams through kernel-owned buffers; scripts never receive a mutable reference to another process buffer.

## Determinism and resource limits

The initial required defaults are:

| Resource | Default ceiling |
| --- | ---: |
| source size | 65,536 bytes |
| instructions per scheduler slice | 1,024 |
| instructions per invocation | 100,000 |
| lexical/call depth | 64 |
| variables per process | 512 |
| single string | 16,384 bytes |
| stdout | 65,536 bytes |
| runtime diagnostics | 4,096 characters |
| processes per user | 32 |
| processes per mainframe | 256 |
| logical execution timeout | 30 game-time seconds |

Limits are authoritative server configuration and may be reduced by a mainframe profile. Every loop condition, function call, operator, and host call consumes instructions. Syscalls may additionally charge a bounded cost. Exhaustion terminates the process with a stable error and releases its resources.

Pseudo-random behavior uses an execution-context generator seeded by the server when the process is created. No ambient random source, frame delta, system clock, locale-sensitive comparison, or unordered collection iteration may affect a script result.

The shell command `vodka FILE.vodka [argument...]` resolves and reads the source through the authenticated principal, creates a real child process, waits without polling, forwards bounded stdout/stderr, and preserves its exit status. In version 0.1 this process-backed command must be standalone at the top level: pipeline, redirection, command-substitution, `eval`, and shell-loop forms fail before spawning, so asynchronous composition cannot orphan a child.

## Host APIs

Host functions are namespaced and capability-checked. The final names become normative when implemented and tested in PR 12-15. Required families are:

- terminal-safe output and input;
- VFS read, write, append, list, metadata, predicates, and path operations;
- process spawn, wait, list, kill, exit, and cancellation;
- device discovery, capability acquisition, bounded request/reply, and revocation;
- network discovery and bounded messaging;
- email, document, and log services.

The VM validates argument count, type, size, current principal, process ownership, path permission, network membership, device capability, and target state on every call.

## Diagnostics

Player-facing failures are stable terminal messages without C# stack traces:

```text
vodka: line 14:9: unexpected token
vodka: permission denied: /sys/config
vodka: process terminated: instruction budget exceeded
```

Diagnostics include the source location when attributable, a short category, and safe context. Full exceptions and entity details are restricted to server diagnostics.

## Compatibility contract

Conceptual DWAINE scripts are ported by behavior, not transliteration. Whiskey will provide fixtures demonstrating equivalent control flow, file predicates, arithmetic, pipelines/commands where exposed, device automation, and resource exhaustion handling. The `.vodka` extension is canonical; `.vk` is not an alias in version 0.1.
