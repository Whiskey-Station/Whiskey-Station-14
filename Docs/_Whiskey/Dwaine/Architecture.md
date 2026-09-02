<!-- SPDX-FileCopyrightText: 2026 Whiskey Station Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# DWAINE architecture

Status: specification baseline for PR 01/15.

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
| Hardware | computer, mainframe, terminal, power, device bus, network port, storage port | bounded connection/session indexes | PR 02 |
| Kernel | mainframe entity and lifecycle events | scheduler, processes, streams, cancellation | PR 03 |
| VFS | storage media and mount-capable device entities | volumes, nodes, paths, metadata, links | PR 04 |
| Identity | terminal session and authenticated principal references | accounts, groups, password verifier, permissions | PR 05 |
| Shell | terminal input/output events | parser, history, environment, command registry | PR 06 |
| Vodka Code | process-owned execution requests | lexer, parser, AST/IR, deterministic VM | PR 07-08 |
| Utilities | executable descriptors | command implementations over kernel/VFS APIs | PR 09 |
| Syscalls/devices | capability-bearing device entities | syscall dispatcher and opaque handle table | PR 10 |
| Network | ports, links, topology membership | bounded routing, discovery, request/reply | PR 11 |
| Storage/services | removable media and service hosts | persistent volume and mailbox/log/document stores | PR 12 |
| Station drivers | explicit components and systems per supported machine | validated command adapters | PR 13 |
| Quality | diagnostics components only where operationally useful | fuzz/stress harnesses and metrics | PR 14 |
| Acceptance | end-to-end gameplay fixtures | parity audit and showcase route | PR 15 |

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
