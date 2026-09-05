<!-- SPDX-FileCopyrightText: 2026 Whiskey Station Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# DWAINE architecture

Status: final architecture through implementation PR 14/14.

This document defines the dependency, authority, timing, and clean-room boundaries for Whiskey Station's DWAINE implementation. `DwaineParityMatrix.md` is the feature ledger and `VodkaCodeSpecification.md` is the language contract.

## Clean-room boundary

The behavioral reference is Goonstation commit [`20b3e8f442da6c6992b2ca5ca191029465575465`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465). Its repository license is [CC BY-NC-SA 3.0 US](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/LICENSE), which is not treated as compatible source material for Whiskey's AGPL codebase.

The reference is therefore a behavioral specification only. Whiskey code is an original C#/RobustToolbox implementation. No DM implementation, prose, mail content, map, sprite, sound, or other creative asset is copied or transliterated. Names needed to identify interoperable concepts are recorded in the parity matrix. Player-facing text and media will be newly authored and localized.

## Layer dependency rule

```text
Content.Shared._Whiskey.Dwaine        Content.Shared._Whiskey.VodkaCode
                 ^                                  ^
                 |                                  |
       +---------+------------------+---------------+
       |                            |
Content.Server._Whiskey.Dwaine   Content.Client._Whiskey.Dwaine
       |
Content.Server._Whiskey.VodkaCode
```

- Shared contains serializable contracts, events, enums, prototype schemas, and presentation-safe state only.
- Server owns hardware state transitions, kernel/process state, users, sessions, VFS, storage, execution, networking, services, devices, drivers, and every gameplay mutation.
- Client depends only on Shared and renders server-provided presentation state. It never resolves permissions or executes Vodka Code.
- Server and Client do not reference one another. Vodka Code host bindings depend on explicit DWAINE server interfaces, never on the client.
- Domain dependencies point inward: devices and services use kernel/VFS/network contracts; the kernel does not depend on a concrete station device driver.

PR 01 intentionally defines no gameplay component. Empty marker components would be placeholders and would violate the requirement that components carry meaningful ECS state. Hardware components and their systems start in PR 02.

## Major domains

| Domain | World-facing ECS ownership | Internal server model | Target |
| --- | --- | --- | --- |
| Hardware | computer, terminal, power, device bus, network port, storage port | bounded presentation state and input validation | PR 02 |
| Session transport | mainframe and terminal entities | bounded connection/session indexes and text queues | PR 03 |
| Kernel | mainframe entity and lifecycle events | boot state and kernel service registry | PR 04 |
| Processes | process-owned execution requests | scheduler, processes, streams and cancellation | PR 05 |
| VFS | logical filesystem ownership | nodes, paths, metadata and links | PR 06 |
| Storage | mount-capable device and media entities | volumes, mounts and persistent media | PR 07 |
| Identity | terminal session and authenticated principal references | accounts, groups, password verifier and permissions | PR 08 |
| Shell/utilities | terminal input/output events and executable descriptors | parser, history, environment and command registry | PR 09 |
| Vodka Code frontend | source files | lexer, parser and AST/IR | PR 10 |
| Vodka Code runtime | process-owned execution requests | deterministic sandboxed VM and resource accounting | PR 11 |
| Syscalls/devices | capability-bearing device entities | syscall dispatcher, drivers and opaque handle table | PR 12 |
| Network | ports, links and topology membership | bounded routing, discovery and request/reply | PR 13 |
| Services/station integration | service hosts and common station devices | mailbox, log, document, diagnostics and validated device adapters | PR 14 |
| Advanced automation | Vodka Code over bounded services, networking and explicit device capabilities | orchestration without a universal machinery API | PR 14 |
| Release gate | no new world-facing domain | local audit, hardening, fuzz/stress and end-to-end acceptance | After PR 14 |

## Authority and trust boundary

A client request identifies only the terminal entity and the requested input. The server derives the actor's session, mainframe, principal, process, working directory, permissions, network membership, and device capabilities from authoritative state. Client-provided UID, username, PID, PPID, path authorization, owner, group, mainframe, network address, device entity, device handle, or script result is never trusted.

Every privileged operation is checked at the narrowest server boundary:

1. terminal ownership/range and active UI session;
2. terminal-to-mainframe connectivity;
3. authenticated DWAINE session;
4. process ownership and state;
5. VFS path resolution and permissions;
6. syscall and opaque capability validity;
7. network topology and message bounds;
8. target driver authorization and current entity state.

The sole first-run exception is account bootstrap: while a mainframe has zero persistent
accounts, one live temporary terminal session may atomically convert its own principal into
the initial operator with a caller-supplied password. That path closes permanently after the
first success. Further persistent accounts require the operator-only `useradd` command, and
all three credential-bearing commands (`bootstrap`, `useradd`, and `su`) are redacted from
shell history even when their names are produced by expansion.

Opaque, generation-checked handles replace arbitrary `EntityUid` exposure. Handles are scoped to a process/session and revoked when the device, process, session, mainframe, network link, or round ends.

## Lifecycle and timing

World-facing state changes are event-driven. No subsystem scans every DWAINE entity or every VFS node per frame. The kernel owns bounded ready/wait queues and advances them from deterministic game-time scheduling. Timeouts and sleeps use `IGameTiming`, never client frame time or wall-clock tasks.

The observable boot contract is:

```text
PoweredOff -> Post -> Bootloader -> Kernel -> Login -> Shell
                    \-------------------------------> Faulted
```

Deletion, shutdown, loss of power, disconnect, round restart, and failed boot all revoke sessions, cancel processes, detach mounts and devices, clear transient queues, and release subscriptions. Persistent media retains only its explicitly persistent volume state.

## Bounded data invariants

- Terminal input, output, history, packet payloads, queues, process counts, VFS nodes, archive depth, link traversal, and mount depth have configured hard limits.
- Script work is charged by deterministic instructions rather than frame time. Each slice and total execution has a budget.
- Network discovery uses explicit topology indexes. Broadcasts are bounded and cannot request recursive reply amplification.
- VFS lookup is canonicalized before authorization. Links and mounts use depth/cycle detection.
- Exceptions are contained at the process boundary and converted to player-safe errors; C# stack traces are server diagnostics only.

## Verification strategy

Each PR adds unit tests for pure domain code and integration tests for ECS, prototypes, networking, cleanup, and gameplay boundaries. The full DebugOpt and Release builds, `Content.Tests`, relevant integration tests, YAML/prototype validation, packaging, and current Whiskey CI are required before the next PR begins. A discovered defect receives a regression test before its root-cause fix.

The architecture test introduced in PR 01 validates the canonical architecture prototype and prevents Shared from acquiring a reference to the Client or Server authority assemblies.
