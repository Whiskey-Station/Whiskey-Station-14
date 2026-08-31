<!-- SPDX-FileCopyrightText: 2026 Whiskey Station Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# DWAINE / Computer3 parity matrix

Audit baseline: Goonstation commit [`20b3e8f442da6c6992b2ca5ca191029465575465`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465), audited on 2026-08-30 for Whiskey PR 01/15.

PRs 06/15 and 07/15 also performed a delta review against Goonstation HEAD
[`6206d395b4fba7169d00692a213770cabe48d8d3`](https://github.com/goonstation/goonstation/tree/6206d395b4fba7169d00692a213770cabe48d8d3)
on 2026-08-30, including every `mainframe2/filetypes` implementation, the OS path parsers, disks,
hard drives, tapes and their repository-wide consumers. The pinned PR 01 baseline remains the stable
clean-room citation set; the delta review found no conflicting VFS/media behavior that changes these contracts.

This is a clean-room behavioral inventory, not a porting ledger. Goonstation's repository is licensed [CC BY-NC-SA 3.0 US](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/LICENSE); no source, prose, map, sprite, or sound from it is incorporated here. All Whiskey implementation code and player-facing content will be original and AGPL-3.0-or-later.

## Audit coverage

The audit inspected all 143 DM files (32,236 lines) under `_std/namespaces/dwaine/`, `_std/defines/mainframe_defines/`, and `code/modules/networks/computer3/`, including every filetype, program, OS, kernel syscall, shell builtin, shell operator, utility, driver, service, and networked machine. Repository-wide tree and content searches covered `DWAINE`, `mainframe2`, `computer3`, `terminal`, `computer file`, `syscall`, `shell`, `driver`, `mount`, `tape`, `disk`, `network`, `email`, `telesci`, `artifact`, `signal`, `process`, and `kernel`.

The external dependency pass classified packet networks, wired data terminals, guardbot tasks/docks, AI/cosmetic references, books/mail text, adventure-zone mainframe variants, and map/asset placements. Map placements do not add runtime semantics; variant mainframes compose the same audited kernel, utilities, and drivers.

Status values used during delivery:

- `SPECIFIED`: a PR 01 contract or architectural decision exists and is tested where executable.
- `IMPLEMENTED`: the assigned runtime behavior exists and is covered by automated tests.
- `PLANNED`: fully classified behavior assigned to one later PR; not claimed as implemented.
- `NOT APPLICABLE`: identified reference material with no functional parity obligation; the reason is recorded.
- The release gate after PR 15 must replace every `SPECIFIED` or `PLANNED` value with `IMPLEMENTED` or a justified `NOT APPLICABLE`; PR 15 remains a development PR for advanced integrations and diagnostics.

## Source index

| Key | Pinned Goon source |
| --- | --- |
| NS | [`_std/namespaces/dwaine/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/_std/namespaces/dwaine) |
| DEF | [`_std/defines/mainframe_defines/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/_std/defines/mainframe_defines) |
| C3 | [`computer3.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/computer3.dm) |
| BUILD | [`buildandrepair.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/buildandrepair.dm) |
| PERIPH | [`peripherals.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/peripherals.dm) |
| TERM | [`terminal.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/terminal.dm) |
| MF | [`mainframe2.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/mainframe2.dm) |
| PROG | [`programs/_program_parent.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/_program_parent.dm) |
| OS | [`programs/os/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os) |
| KERNEL | [`kernel.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os/kernel/kernel.dm) |
| SYSCALL | [`kernel/syscalls/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os/kernel/syscalls) |
| VFS | [`filetypes/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/filetypes) |
| SHELL | [`shell/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os/shell) |
| BUILTIN | [`shell_builtins/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os/shell/shell_builtins) |
| OP | [`shell_script_operators/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/os/shell/shell_script_operators) |
| UTIL | [`utilities/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/programs/utilities) |
| DRV | [`os_drivers.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/os_drivers.dm) |
| MEDIA | [`disks.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/disks.dm), [`harddrives.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/harddrives.dm), [`tapes.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/tapes.dm) |
| MACHINE | [`misc_terms.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/misc_terms.dm) |
| EMAIL | [`emailserv.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/emailserv.dm), [`emailclient.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/emailclient.dm) |
| LOG | [`logreader.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/logreader.dm) |
| DOC | [`documents.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/documents.dm) |
| ART | [`artifact_res.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/artifact_res.dm) |
| TELE | [`telesci.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/mainframe2/telesci.dm) |
| PACKET | [`code/modules/packets/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/packets) |
| DATANET | [`code/modules/power/terminal.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/power/terminal.dm) |
| GUARDBOT | [`guardbot.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/robotics/bot/guardbot.dm) |
| APPS | [`smallprogs.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3/smallprogs.dm) and the top-level [`computer3/`](https://github.com/goonstation/goonstation/tree/20b3e8f442da6c6992b2ca5ca191029465575465/code/modules/networks/computer3) programs |
| EXTERNAL | [`old_bot_factory.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/z_adventurezones/old_bot_factory.dm), [`hemera.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/z_adventurezones/hemera.dm), [`sequestered_cloner.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/z_adventurezones/sequestered_cloner.dm), [`lavamoon.dm`](https://github.com/goonstation/goonstation/blob/20b3e8f442da6c6992b2ca5ca191029465575465/code/z_adventurezones/lavamoon.dm) |

## Architecture, hardware, boot, and processes

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Clean-room functional boundary | all sources; repository license | Pinned-source audit and original AGPL implementation rule | SPECIFIED | 01/15 | Architecture docs review | No DM, protected prose, or assets copied. |
| Shared/server/client dependency boundary | C3, MF, PROG | Shared contracts; authoritative server; presentation-only client | SPECIFIED | 01/15 | `SharedContractsDoNotReferencePresentationOrAuthorityAssemblies` | No Server/Client cycle. |
| Architecture identity/version | NS, C3 | `DwaineArchitecturePrototype` `WhiskeyDwaine` | SPECIFIED | 01/15 | `ArchitecturePrototypeMatchesCanonicalSpecification` | Couples docs to Vodka Code 0.1 and `.vodka`. |
| Computer role | C3 | composed computer hardware entity | IMPLEMENTED | 02/15 | `PrototypeComposesOnlyPhysicalTerminalLayer` | Not a god component. |
| Mainframe role | MF | mainframe component plus focused server transport system | IMPLEMENTED | 03/15 | `InvalidTargetsRangeAndProductionPrototypeHaveTransportComponents` | Owns runtime association, not kernel or shell logic. |
| Terminal role | TERM, C3 | terminal component, BUI and authoritative request events | IMPLEMENTED | 02/15 | `PrototypeComposesOnlyPhysicalTerminalLayer`, `BuiReconnectIsIdempotentAndDestructionCleansPresentationState` | Client never authenticates actions. |
| Display and terminal output | C3, TERM | bounded server output buffer and client presentation state | IMPLEMENTED | 02/15 | `ServerOutputBufferEnforcesBothBounds` | Presentation uses plain text; session output transport lands in PR 03. |
| Keyboard/input abstraction | C3, TERM | BUI input request validated against the active server-side UI actor | IMPLEMENTED | 02/15 | `TerminalInputValidationRejectsUnboundedAndMultilineData` | Input length bounded; session ownership is added in PR 03. |
| Per-user command history | C3, SHELL | bounded shell history owned by server session | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Histories are isolated per authenticated terminal session, evict oldest entries at the configured bound and redact every compound command containing credentials. |
| Storage interface | C3, MF, MEDIA | explicit physical storage connector | IMPLEMENTED | 02/15 | `PrototypeComposesOnlyPhysicalTerminalLayer` | VFS and media behavior land in PR 06 and PR 07. |
| Device bus | PERIPH, DRV | bounded physical bus endpoint | IMPLEMENTED | 02/15 | `PrototypeComposesOnlyPhysicalTerminalLayer` | Capability ABI starts PR 12. |
| Network interface | PERIPH, DATANET | explicit physical connector, network label and link range | IMPLEMENTED | 02/15 | `PrototypeComposesOnlyPhysicalTerminalLayer` | Session topology lands in PR 03; routed networking in PR 13. |
| Power state | C3, MF | powered/offline transitions using ECS power events | IMPLEMENTED | 02/15 | `PowerLifecycleAndInvalidEntityAreServerAuthoritative` | Transient hardware presentation state is cleared on deletion. |
| Computer construction and repair | BUILD | Whiskey construction graph for frame, board, storage and peripherals | PLANNED | 14/15 | Hardware/build/deconstruct | New `_Whiskey` prototypes only. |
| Modular peripherals | PERIPH | installable explicit device components | PLANNED | 14/15 | Hardware/peripheral constraints | Cards do not execute gameplay logic themselves. |
| Portable/luggable computer | C3 | portable terminal hardware profile with cell power | PLANNED | 14/15 | Hardware/portable lifecycle | Reuses the same terminal/runtime contracts. |
| Terminal connect/disconnect/reconnect | TERM, MF | validated server session state machine | IMPLEMENTED | 03/15 | `ConnectInputOutputDisconnectAndReconnectAreAuthoritative` | Repeated BUI open and connect requests preserve one session. |
| Multiple terminals per mainframe | MF, KERNEL | indexed bounded server sessions per mainframe | IMPLEMENTED | 03/15 | `MultipleMachinesDeletionAndTopologyChangesCleanSessions` | Mainframe capacity and multi-mainframe isolation are enforced. |
| Connection heartbeat and timeout | TERM, MF | periodic `IGameTiming` lifecycle validation | IMPLEMENTED | 03/15 | `MultipleMachinesDeletionAndTopologyChangesCleanSessions` | Equivalent lifecycle validation avoids a client-controlled heartbeat. |
| Device connection records | MF | opaque internal session records scoped to mainframe | IMPLEMENTED | 03/15 | `SessionIdentityNeverAppearsInClientMessages` | Session ID and ownership never cross the BUI contract. |
| POWER → POST | C3, MF | deterministic hardware POST stage | IMPLEMENTED | 04/15 | `BootShutdownAndRepeatedRebootAreDeterministic`, `FailedBootPanicPowerLossAndDeletionAreContained` | Required hardware, storage, power loss, and deletion are validated. |
| Bootloader lifecycle and local source validation | OS, MF | timed bootloader stage with an explicit storage prerequisite | IMPLEMENTED | 04/15 | `BootShutdownAndRepeatedRebootAreDeterministic` | Logical volumes and actual boot files remain owned by PR 06/07. |
| Network bootloader/recovery | OS, MEDIA, MF | bootloader provider that discovers authorized recovery media/services | PLANNED | 13/15 | Kernel/netboot recovery | Depends on persistent media from PR 07 and authenticated networking from PR 13. |
| Kernel startup | KERNEL | bounded per-mainframe server kernel runtime | IMPLEMENTED | 04/15 | `BootShutdownAndRepeatedRebootAreDeterministic` | Does not create a process scheduler early. |
| Kernel-ready handoff | KERNEL, OS | typed server event for the later login/process layers | IMPLEMENTED | 04/15 | `BootShutdownAndRepeatedRebootAreDeterministic` | The event carries only the server-assigned boot generation. |
| Login/shell startup | KERNEL, OS | authenticated process handoff after kernel readiness | IMPLEMENTED | 09/15 | `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Kernel-ready mainframes reconcile transport/identity event ordering, spawn one owned shell process per session and replace it after `su`/logout without stale authority. |
| Reboot and failed boot | MF, OS | cleanup-first reboot/fault transitions | IMPLEMENTED | 04/15 | `FailedBootPanicPowerLossAndDeletionAreContained` | Controlled failures never expose a C# stack trace to terminals. |
| Kernel service registry and shutdown hooks | KERNEL | bounded named services with deterministic reverse shutdown | IMPLEMENTED | 04/15 | `ServiceRegistryIsBoundedAndShutsDownInReverseOrder` | Hook failures are contained and converted to safe diagnostics. |
| Mainframe system clock | KERNEL | game-time-fed boot generation and uptime clock | IMPLEMENTED | 04/15 | `SystemClockUsesOnlyObservedGameTime` | Uses authoritative observations; no wall clock or frame-time arithmetic. |
| Kernel panic and clean shutdown | KERNEL, MF | controlled panic, shutdown, reboot, and power-loss cleanup | IMPLEMENTED | 04/15 | `FailedBootPanicPowerLossAndDeletionAreContained` | Service state is revoked before the terminal state changes. |
| Process program abstraction | PROG | immutable program descriptor plus server-owned step implementation | IMPLEMENTED | 05/15 | `CreationSchedulingStreamsMetadataAndPidUniqueness` | VFS executable resolution belongs to PR 06; callers never submit native code from the client. |
| PID and PPID | PROG, KERNEL | monotonic server-assigned process identifiers and validated parent links | IMPLEMENTED | 05/15 | `CreationSchedulingStreamsMetadataAndPidUniqueness`, `RebootAndDestructionCancelAllProcessesWithoutPidReuse` | PID zero is invalid and identifiers are not reused across reboot. |
| Process owner | PROG, KERNEL | opaque authoritative principal reference with control and IPC checks | IMPLEMENTED | 05/15 | `FaultStopContinueInstructionBudgetAndIpcAreContained`, `ProcessLimitsAndOneHundredTwentyEightProcessChurnStayBounded` | PR 08 maps authenticated accounts to owner references; the client never assigns runtime ownership. |
| Process states | PROG, MF | Created/Ready/Running/Waiting/Stopped/Exited/Faulted state machine | IMPLEMENTED | 05/15 | `ParentChildWaitKillAndCleanupAreDeterministic`, `FaultStopContinueInstructionBudgetAndIpcAreContained` | Every scheduler/control transition is explicit and observable through a server event. |
| stdin/stdout/stderr | SHELL, PROG | independent bounded FIFO text streams that reject overflow | IMPLEMENTED | 05/15 | `TextStreamsRejectOverflowWithoutLosingUnreadData`, `CreationSchedulingStreamsMetadataAndPidUniqueness` | Unread output is never silently evicted to accept newer output. |
| Process start time, exit code, cwd, environment | PROG, SHELL | game-time metadata, terminal result, VFS-validated opaque cwd handle and bounded copy-on-spawn environment | IMPLEMENTED | 05/15 + 06/15 | `EnvironmentsAreBoundedValidatedAndClonedByValue`, `CreationSchedulingStreamsMetadataAndPidUniqueness`, `ProcessWorkingDirectoriesAndProcViewsUseValidatedServerHandles` | PR 06 rejects nonexistent, cross-volume-unavailable and non-directory working handles before process creation. |
| Scheduler/processing list | MF, KERNEL | fair bounded per-mainframe ready queue with one logical program step per dispatch | IMPLEMENTED | 05/15 | `ProcessLimitsAndOneHundredTwentyEightProcessChurnStayBounded` | Uses no arbitrary background `Task`, wall clock, or direct `frameTime` accounting. |
| Spawn and child creation | SYSCALL, PROG | validated kernel spawn with parent, inherited environment and owner policy | IMPLEMENTED | 05/15 | `ParentChildWaitKillAndCleanupAreDeterministic`, `ProcessLimitsAndOneHundredTwentyEightProcessChurnStayBounded` | Per-owner and per-mainframe limits are clamped by hard server ceilings; syscall façades arrive in PR 12. |
| Exit, kill, wait and child lifecycle | SYSCALL, PROG | ownership-checked control with recursive cancellation and deterministic reaping | IMPLEMENTED | 05/15 | `ParentChildWaitKillAndCleanupAreDeterministic`, `RebootAndDestructionCancelAllProcessesWithoutPidReuse` | Awaited results are delivered once; kernel shutdown and entity deletion revoke the whole table. |
| Inter-process signal/reply | MF, PROG | typed bounded mailbox messages between authorized related/owned processes | IMPLEMENTED | 05/15 | `MailboxesRejectMalformedAndOverCapacityMessages`, `FaultStopContinueInstructionBudgetAndIpcAreContained` | Payloads are plain bounded text; no privileged object or `EntityUid` deserialization. |
| Process directory (`/proc`) | NS, MF | read-only virtual VFS process views generated from authoritative process events | IMPLEMENTED | 06/15 | `ProcessWorkingDirectoriesAndProcViewsUseValidatedServerHandles` | Views carry PID/state/boot generation, disappear on reap and are cleared across kernel generations. |

## Virtual filesystem, users, and permissions

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Root and directory tree | VFS, OS | bounded logical volume tree with explicit root and stable node identity | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined`, `StructuralLimitClampsAlwaysPreserveTheCanonicalSystemTree` | Pure server state; physical volume attachment remains PR 07. |
| Absolute and relative paths | OS, NS | deterministic canonical path parser resolved from an opaque server cwd handle | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined`, `RelativePathsCrudAndStableHandlesWorkAcrossRenameAndMove` | Authorization is deliberately layered after canonicalization in PR 08. |
| `.` and `..` | OS | lexical normalization with explicit root-escape rejection | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined` | Repeated separators and `.` normalize; `..` can never traverse above root. |
| Filename validation | PROG, OS | length-bounded names with control, separator and reserved-name rejection; case-insensitive lookup | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined`, `RelativePathsCrudAndStableHandlesWorkAcrossRenameAndMove` | Comparison is ordinal and locale-independent; case-only rename preserves identity. |
| Create/read/write/append/delete | SYSCALL, VFS | typed bounded node operations with explicit result codes | IMPLEMENTED | 06/15 | `RelativePathsCrudAndStableHandlesWorkAcrossRenameAndMove`, `DirectoryDeletionRequiresExplicitRecursiveCleanup`, `RecordAndTextMutationsAreBoundedAndAtomic` | Failed writes and record mutations preserve the previous value; root and non-empty directories are protected. |
| Rename/copy/move | UTIL, VFS | VFS-native operations with stable source handles and independent deep copies | IMPLEMENTED | 06/15 | `RelativePathsCrudAndStableHandlesWorkAcrossRenameAndMove`, `CopyIsIndependentAndMoveRejectsDescendantDestinations` | Descendant destinations are rejected; cross-volume moves return an explicit result used by PR 07. |
| List/mkdir | UTIL, VFS | sorted bounded enumeration and optional parent creation | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined`, `DirectoryDeletionRequiresExplicitRecursiveCleanup` | Permission decisions consume the metadata hooks in PR 08. |
| Symbolic directory links | VFS | stable-handle symbolic links with no-follow-final operations | IMPLEMENTED | 06/15 | `LinksDetectCyclesDepthAndBrokenTargetsWithoutFollowingFinalWhenRequested` | Broken targets, cycles and maximum traversal depth produce distinct controlled errors. |
| Mount points and unmount | VFS, DRV, SYSCALL | volume/device mounts with explicit detach | IMPLEMENTED | 07/15 | `MountedVolumesSupportRelativePathsCrossVolumeCopiesAndStableHandles`, `ShutdownUnmountsButKeepsInsertedPersistentMedia` | Mountpoints cannot hide non-empty trees; normal unmount is denied while a live process uses the volume as cwd. |
| Volumes and storage quotas | VFS, MEDIA | bounded per-medium volumes with node, path, payload, child, link and archive quotas | IMPLEMENTED | 07/15 | `MountedVolumeReadOnlyAndPerVolumeLimitsAreEnforced`, `MountedVolumeStructuralLimitsCannotBeBypassedByRenameMoveOrCopy`, `RemovableDiskPersistsAcrossFlushEjectAndReinsert` | Detached handles are revoked and every media volume retains its own independently clamped limits; rename, move and copy cannot bypass structural quotas. |
| Metadata: date, owner, group, mode | VFS | typed owner/group/mode/flags plus authoritative game-time creation/modification values | IMPLEMENTED | 06/15 | `MetadataHooksPreserveOwnershipAndReadOnlyNodesRejectMutation` | Central ownership/mode authorization consumes these hooks in PR 08. |
| Directory depth and archive depth | VFS | configuration clamped by non-bypassable server hard ceilings | IMPLEMENTED | 06/15 | `NodeAndDepthLimitsContainMassCreation`, `ArchivesRoundTripAndCannotContainTheirOwnDestination` | Traversals are bounded independently from client input. |
| Text file | VFS | bounded text node with overwrite and append | IMPLEMENTED | 06/15 | `RecordAndTextMutationsAreBoundedAndAtomic` | Overflow is rejected without truncating or replacing existing content. |
| Record file | VFS | bounded ordinal key/value record node copied at API boundaries | IMPLEMENTED | 06/15 | `StructuredFileTypesRoundTripWithoutLeakingMutableInputs`, `RecordAndTextMutationsAreBoundedAndAtomic` | Safe nullable string values only; rejected mutations are atomic. |
| User-data file | VFS | typed name/assignment/access-tag payload copied at API boundaries | IMPLEMENTED | 06/15 | `StructuredFileTypesRoundTripWithoutLeakingMutableInputs` | Credentials are not part of this file payload; protected account storage and verification remain PR 08. |
| Clone/genome record | VFS | typed opaque station record payload | PLANNED | 14/15 | Driver/medical record capability | Only if a Whiskey cloning integration exists. |
| Image-like metadata file | VFS | bounded display/description/text-preview metadata, not imported imagery | IMPLEMENTED | 06/15 | `StructuredFileTypesRoundTripWithoutLeakingMutableInputs` | No Goon asset copied and no arbitrary binary decoder is exposed. |
| Galactic-position record | VFS, TELE | typed coordinate document | PLANNED | 15/15 | Driver/telesci coordinates | Validated by driver capability. |
| Signal file | VFS, PACKET | bounded structured message metadata document | IMPLEMENTED | 06/15 | `StructuredFileTypesRoundTripWithoutLeakingMutableInputs` | VFS payload has no privileged runtime fields; network serialization and delivery remain PR 13. |
| Archive file | VFS, UTIL | bounded recursively copied archive metadata with controlled extraction | IMPLEMENTED | 06/15 | `ArchivesRoundTripAndCannotContainTheirOwnDestination` | Entry/depth/node quotas apply; an archive cannot be written into the source subtree. Media persistence lands in PR 07. |
| Program/script file | PROG, SHELL | bounded executable descriptor and source node prepared for `.vodka` | IMPLEMENTED | 06/15 | `StructuredFileTypesRoundTripWithoutLeakingMutableInputs` | Native descriptors are immutable through text writes; the Vodka lexer/runtime remain PRs 10/11. |
| System/virtual file | NS, KERNEL | flagged system node and read-only virtual record representation | IMPLEMENTED | 06/15 | `MetadataHooksPreserveOwnershipAndReadOnlyNodesRejectMutation`, `ProcessWorkingDirectoriesAndProcViewsUseValidatedServerHandles` | Virtual process records are generated server-side and are never client-authored. |
| Mountpoint file proxy | DRV, VFS | server-owned mounted-volume adapter with opaque volume/node handles | IMPLEMENTED | 07/15 | `DetachInvalidatesHandlesAndReattachPreservesMediaState`, `MediaAndMainframeDestructionCleanBothSidesOfTheRelationship`, `ExternalContainerRemovalInvalidatesMountAndBothRelationshipIndexes` | Device loss or removal outside the normal ejection API detaches the volume, clears both relationship indexes and immediately invalidates handles in that mainframe VFS. |
| Guardbot task payload | GUARDBOT, VFS | typed device document through guardbot driver | PLANNED | 15/15 | Driver/guardbot task | Never exposes bot entity IDs to scripts. |
| Canonical system layout | NS, MEDIA | `/sys`, `/sys/drvr`, `/sys/srv`, `/bin`, `/conf`, `/usr`, `/home`, `/dev`, `/mnt`, `/proc`, `/tmp`, `/var`, `/etc`, `/etc/mail` | IMPLEMENTED | 06/15 | `BootstrapAndCanonicalizationAreDeterministicAndRootConfined`, `StructuralLimitClampsAlwaysPreserveTheCanonicalSystemTree` | Structural minimum clamps guarantee the complete layout even under undersized prototype configuration. |
| Temporary and full users | KERNEL | unauthenticated terminal session and authenticated account | IMPLEMENTED | 08/15 | `TemporarySessionsAreRevokedOnDisconnectAndElevation`, `TransportLifecycleOwnsLoginLogoutReconnectAndRebootSessions` | Temporary identity has minimal authority and is deleted with its session. |
| UID and username | KERNEL, VFS | server-assigned UID plus validated display/login name | IMPLEMENTED | 08/15 | `CredentialsSessionsAndExpiryAreServerAuthoritative` | Client username is input, not identity proof. |
| Groups and sysop/root-like account | NS, KERNEL | typed groups and privileged system principal | IMPLEMENTED | 08/15 | `OperatorsManageGroupsAndDisabledUsersLoseSessions` | Least privilege; no magic client flag. |
| Login credential authentication | OS, TERM | server-owned account credential verifier | IMPLEMENTED | 08/15 | `CredentialsSessionsAndExpiryAreServerAuthoritative` | Passwords use salted PBKDF2-SHA256 verifiers and fixed-time comparison. |
| Station card authentication adapter | OS, TERM | station identity credential adapter resolving to a server principal | PLANNED | 14/15 | Station integration/auth adapter | Card identity remains untrusted input until validated against the station identity service. |
| Login/logout/session expiry | KERNEL, TERM | authoritative session lifecycle | IMPLEMENTED | 08/15 | `CredentialsSessionsAndExpiryAreServerAuthoritative`, `TransportLifecycleOwnsLoginLogoutReconnectAndRebootSessions` | Disconnect, expiry, kernel shutdown and account disablement clean sessions. |
| File owner/group/mode enforcement | NS, PROG | centralized VFS authorization service | IMPLEMENTED | 08/15 | `AuthorizedVfsEnforcesReadWriteChmodAndChown` | Read, write, execute and metadata checks are separate. |
| Process ownership enforcement | KERNEL, SYSCALL | kernel authorization policy | IMPLEMENTED | 08/15 | `FaultStopContinueInstructionBudgetAndIpcAreContained`, `OperatorsManageGroupsAndDisabledUsersLoseSessions` | Authenticated principal IDs map directly to opaque process owners; parenthood alone does not elevate authority. |
| `chmod` semantics | UTIL, NS | mode-changing utility and syscall policy | IMPLEMENTED | 08/15 | `AuthorizedVfsEnforcesReadWriteChmodAndChown` | Owners or operators may change mode; shell octal parsing lands in PR 09. |
| `chown` semantics | UTIL, NS | privileged owner/group change | IMPLEMENTED | 08/15 | `AuthorizedVfsEnforcesReadWriteChmodAndChown` | Only operators may select existing users and groups. |
| `su` semantics | UTIL, TERM | explicit privilege elevation through fresh credential proof | IMPLEMENTED | 08/15 | `TemporarySessionsAreRevokedOnDisconnectAndElevation` | Fresh verifier proof replaces the session principal and revokes a temporary account. |
| Deleted-user and disconnect cleanup | KERNEL | identity/session invalidation and permission-reference denial | IMPLEMENTED | 08/15 | `DeletingUsersRevokesSessionsAndDoesNotDeleteOnDeniedRequests`, `TransportLifecycleOwnsLoginLogoutReconnectAndRebootSessions` | Removed principals lose live sessions; stale VFS owner IDs confer no access. |

## Shell and builtins

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Interactive shell | SHELL | server shell process | IMPLEMENTED | 09/15 | `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | One scheduler-owned process per transport session presents the prompt and sleeps without update churn while waiting for input. |
| Tokenization, quoting and escaping | SHELL, PROG | dedicated bounded lexer | IMPLEMENTED | 09/15 | `ParserHandlesQuotesEscapesSubstitutionAndBoundedOperators` | Single/double quotes, escapes and operators have stable diagnostics; no host shell or HTML execution exists. |
| Arguments and environment | SHELL | bounded argument vector and environment | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | `set`/`unset` operate only on bounded server-owned session state; `HOME`, `PATH` and `USER` are protected from removal. |
| Working directory | SHELL, UTIL | canonical VFS cwd | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Cwd is an opaque VFS handle and every path is canonicalized/authorized server-side. |
| PATH-like command resolution | SHELL | `/bin`, current directory and explicit path resolution | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak` | `/bin:/usr/bin:.` and explicit paths revalidate execute permission; native/Vodka program execution remains owned by its runtime PR. |
| Pipes and stream chaining | SHELL | bounded stdout-to-stdin pipelines | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | Pipeline stages and total commands are server-clamped. |
| Command substitution | SHELL | bounded nested command capture | IMPLEMENTED | 09/15 | `ParserHandlesQuotesEscapesSubstitutionAndBoundedOperators`, `NestedEvaluationOutputRegexAndLogicalWaitRemainBounded` | Quote-aware nested capture shares one top-level instruction budget and a hard evaluation-depth limit. |
| Output redirection to VFS | SHELL | explicit redirection syntax and VFS write | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Input, truncate and append redirects use the centralized authorized VFS façade. |
| Exit status and stderr | SHELL | conventional integer status and separate stderr | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | `$STATUS`, `&&`, `||` and stable player-facing errors never expose C# stack traces. |
| `break` builtin | BUILTIN | break current script/loop context | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Returns a stable error outside a bounded loop. |
| `cls` / `clear` builtin | BUILTIN | clear terminal presentation | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | The owning server session clears only its output buffer. |
| `echo` builtin | BUILTIN | bounded stdout output, newline option | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | Supports pipelines and `-n` under the shared output ceiling. |
| `else` builtin | BUILTIN | shell compatibility conditional branch | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Consumes the immediately preceding server-side status. |
| `eval` builtin | BUILTIN | evaluates only shell expressions, never host code | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, `NestedEvaluationOutputRegexAndLogicalWaitRemainBounded` | No Roslyn, reflection, native process or OS shell path exists. |
| `goonsay` novelty builtin | BUILTIN | clean-room `whiskeysay` equivalent | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak` | Original name/output is not copied; the registered manual exposes the Whiskey-specific command. |
| `history` builtin | BUILTIN | list/clear bounded per-user history | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Session isolation, eviction and compound-`su` credential redaction are enforced. |
| `if` builtin | BUILTIN | shell compatibility conditional | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection`, `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | String and checked integer comparisons produce conventional statuses. |
| `logout` / `logoff` builtin | BUILTIN | identity logout request | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Revokes the authenticated session, installs a new guest identity and replaces the owned shell process. |
| `man` / `help` builtin | BUILTIN, DOC | registered command help index | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak` | Every registered command and alias has synchronized help; undocumented fictional commands are absent. |
| `mesg` builtin | BUILTIN | opt in/out of user messages | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Acceptance is isolated to the live shell session. |
| `sleep` builtin | BUILTIN | logical-time process wait | IMPLEMENTED | 09/15 | `NestedEvaluationOutputRegexAndLogicalWaitRemainBounded` | Uses mainframe game time, caps waits at 300 seconds and disconnect cleanup cancels the owning process. |
| `talk` builtin | BUILTIN | bounded user-to-user mainframe message | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Resolves only live consenting users on the same mainframe; no session IDs are exposed. |
| `unset` builtin | BUILTIN | remove environment/local variables | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection` | Invalid names and protected system variables are rejected. |
| `while` builtin | BUILTIN | bounded shell compatibility loop | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, `NestedEvaluationOutputRegexAndLogicalWaitRemainBounded` | Iteration, depth, output and one shared top-level instruction budget prevent nested-loop monopolization. |
| `who` builtin | BUILTIN | permission-safe session listing | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Lists public usernames/guest state only, never credentials or opaque session identifiers. |

## Vodka Code and scripting operators

All rows below describe functional equivalents. Vodka Code uses the grammar in `VodkaCodeSpecification.md`; it does not copy the reference RPN implementation.

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Lexer, source locations and diagnostics | OP, SHELL | bounded Vodka lexer with decoded literals and line/column spans | IMPLEMENTED | 10/15 | `LexerPreservesDecodedValuesKeywordsAndSourceLocations`, `LexerRejectsInvalidInputAndEnforcesHardBounds` | Specification 0.1 is normative; source, token and diagnostic ceilings are enforced before runtime. |
| Parser and AST/IR | OP, SHELL | error-recovering recursive-descent parser and immutable Whiskey-owned AST | IMPLEMENTED | 10/15 | `ParserBuildsStructuredAstWithStablePrecedenceAndCalls`, `MalformedCorpusProducesPlayerSafeDiagnostics`, `ParserFuzzSeedsAreDeterministicBoundedAndNeverThrow` | No arbitrary C# compilation or host evaluation; syntax and argument depth are bounded. |
| Variables, literals and lexical scopes | OP | `let`, assignment, integer, boolean, string, null | SPECIFIED | 11/15 | Vodka/runtime values | Bounded per process. |
| Structured `if` / `else` | BUILTIN, OP | Vodka conditional statements | SPECIFIED | 11/15 | Vodka/conditionals | Boolean conditions only. |
| Structured `while`, `break`, `continue` | BUILTIN, SHELL | budgeted Vodka control flow | SPECIFIED | 11/15 | Vodka/infinite loop termination | Every iteration consumes instructions. |
| Return and exit semantics | SHELL, SYSCALL | script result and process exit code | SPECIFIED | 11/15 | Vodka/return/exit | Stable parent notification. |
| Instruction, recursion, source, output and process limits | NS, SHELL | VM resource governor | SPECIFIED | 11/15 | Vodka/resource exhaustion | Defaults recorded in language spec. |
| `+` add/concatenate | OP | typed `+` | PLANNED | 11/15 | Vodka/operator add | Mixed types rejected. |
| `-` subtract | OP | checked integer subtraction | PLANNED | 11/15 | Vodka/operator subtract | String slicing becomes explicit library behavior. |
| `*` multiply | OP | checked integer multiplication | PLANNED | 11/15 | Vodka/operator multiply | String repeat becomes explicit bounded function. |
| `/` divide | OP | checked integer division | PLANNED | 11/15 | Vodka/operator divide/zero | String split becomes explicit library behavior. |
| `%` modulo | OP | checked integer remainder | PLANNED | 11/15 | Vodka/operator modulo/zero | Zero is a runtime error. |
| `rand` | OP | seeded deterministic random function | PLANNED | 11/15 | Vodka/random reproducibility | No ambient RNG. |
| `and` | OP | short-circuit boolean conjunction | PLANNED | 11/15 | Vodka/operator and | Bitwise operations, if added, use distinct names. |
| `or` | OP | short-circuit boolean disjunction | PLANNED | 11/15 | Vodka/operator or | Deterministic order. |
| `xor` / `eor` | OP | boolean exclusive-or | PLANNED | 11/15 | Vodka/operator xor | Compatibility alias may be library-only. |
| `not` / `!` | OP | boolean negation | PLANNED | 11/15 | Vodka/operator not | Strict boolean type. |
| `eq` | OP | `==` equality | PLANNED | 11/15 | Vodka/operator equality | Same-kind comparison. |
| `ne` | OP | `!=` inequality | PLANNED | 11/15 | Vodka/operator inequality | Same-kind comparison. |
| `gt` | OP | `>` relation | PLANNED | 11/15 | Vodka/operator greater | Ordinal strings. |
| `ge` | OP | `>=` relation | PLANNED | 11/15 | Vodka/operator greater-equal | Ordinal strings. |
| `lt` | OP | `<` relation | PLANNED | 11/15 | Vodka/operator less | Ordinal strings. |
| `le` | OP | `<=` relation | PLANNED | 11/15 | Vodka/operator less-equal | Ordinal strings. |
| file `e` predicate | OP | `fs.exists(path)` | PLANNED | 11/15 | Vodka/file exists permissions | Cannot reveal inaccessible nodes. |
| file `d` predicate | OP | `fs.is_directory(path)` | PLANNED | 11/15 | Vodka/file directory | Canonical path. |
| file `f` predicate | OP | `fs.is_file(path)` | PLANNED | 11/15 | Vodka/file regular | Canonical path. |
| file `x` predicate | OP | `fs.is_executable(path)` | PLANNED | 11/15 | Vodka/file executable | Includes execute permission. |
| `to` / `value` assignment | OP | declaration and assignment statements | PLANNED | 11/15 | Vodka/assignment/scope | No implicit undeclared global. |
| quoted string escape operator | OP | Vodka string literals and escapes | PLANNED | 11/15 | Vodka/string escapes | Lexer owns quoting. |
| `del` stack operation | OP | compatibility stack library `drop` | PLANNED | 11/15 | Vodka/stack drop | Bounded explicit compatibility library. |
| `#` stack depth | OP | compatibility stack library `depth` | PLANNED | 11/15 | Vodka/stack depth | Does not conflict with comments. |
| `dup` stack operation | OP | compatibility stack library `dup` | PLANNED | 11/15 | Vodka/stack dup/limit | Checks data limit. |
| `.` stack pop/print | OP | compatibility stack library `pop` plus output | PLANNED | 11/15 | Vodka/stack pop/underflow | Safe error on underflow. |
| `.s` stack print | OP | compatibility stack library `inspect` | PLANNED | 11/15 | Vodka/stack inspect/output cap | Player-safe output. |
| Full script fixtures | SHELL, DOC | at least 50 `.vodka` programs | PLANNED | 11/15 | Vodka/fixture suite | Includes branches, files and exhaustion. |

## Core utilities

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| `cat` | UTIL | VFS concatenate/read utility | IMPLEMENTED | 09/15 | `EngineSupportsEnvironmentPipesChainsSubstitutionAndRedirection`, `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | File/stdin output is permission checked and bounded. |
| `cd` | UTIL | change canonical cwd | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable`, `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Defaults to server-owned `HOME` and accepts only executable directory handles. |
| `chmod` | UTIL | mode utility | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Parses exact three-digit octal modes and delegates ownership policy to the authorized VFS. |
| `chown` | UTIL | owner/group utility | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak` | Only operators may select existing users/groups. |
| `cp` | UTIL | copy file/tree policy | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Source subtrees are readable, destination parents writable and copied nodes are owned by the requesting principal. |
| `date` | UTIL | deterministic game-time formatting | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Uses mainframe logical time only; no wall clock. |
| `getopt` | UTIL | bounded POSIX-like option parser | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Short options, required values, `--`, positional arguments and errors have stable output/status. |
| `grep` | UTIL | bounded text/record search | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable`, `NestedEvaluationOutputRegexAndLogicalWaitRemainBounded` | Searches stdin, text/program files, record/signal fields or a recursive directory tree; traversal skips symlinks and caps depth, files, aggregate input, pattern size and regex time at 50 ms. |
| `ln` | UTIL | create VFS symlink | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Target read access and destination-parent mutation are revalidated; VFS cycle/depth controls remain authoritative. |
| `ls` | UTIL | permission-aware listing and long metadata | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Normal and long modes use authorized VFS snapshots and stable owner/group/mode formatting. |
| `mkdir` | UTIL | create directory, including `-p` | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Batch creation is capped at 32 paths and every parent transition is authorized. |
| `mount` | UTIL | privileged capability-backed mount | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, PR 07 storage integration suite | Labels resolve only against media inserted in this mainframe; mount/unmount require operator authority and call the server storage service. |
| `mv` | UTIL | atomic move/rename where possible | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Source/destination parents are authorized and raw VFS atomic/cross-volume rules prevent copy-delete loss. |
| `pwd` | UTIL | print canonical cwd | IMPLEMENTED | 09/15 | `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries` | Opaque cwd handles resolve to a stable canonical path. |
| `rm` | UTIL | file/tree removal with force/interactive/recursive modes | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Interactive mode stores canonical paths server-side for 30 seconds and requires `rm --confirm`; root and subtree permissions remain protected. |
| `scnt` | UTIL | authorized network/device rescan | PLANNED | 13/15 | Utility/scnt topology | Deliberately not registered before PR 13: the real command requires topology/capabilities and must never fall back to a global entity scan or placeholder result. |
| `su` | UTIL | credential-backed privilege elevation | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak`, `RemainingBuiltinsExposeStableStatusesAndServerHostBoundaries`, `ShellRunsAsWaitingProcessesAndSurvivesLoginCommandsAndReconnect` | Password verification stays server-side, failed PBKDF attempts receive capped logical-time backoff, compound/dynamic history is redacted and success replaces process ownership/environment. |
| `tar` | UTIL | bounded archive create/list/extract | IMPLEMENTED | 09/15 | `UtilitiesOperateOnAuthorizedVfsAndInteractiveRemovalIsStable` | Uses VFS archive depth/quota validation and recursively lists canonical internal paths. |
| Man pages and exit codes for every utility | UTIL, DOC | registered help entries synchronized with commands | IMPLEMENTED | 09/15 | `CredentialsManualsAndPrivilegeBoundariesDoNotLeak` | Tests enumerate every registered command/alias and require an exact help entry; no undocumented command or stale page exists. |

## Syscalls and device ABI

The reference declares IDs 1-25 and 30. Twenty-three are kernel-dispatched calls; `TEXIT`, `RECVFILE`, `BREAK`, and `REPLY` are typed inter-process messages rather than callable handlers.

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Syscall dispatcher and stable errors | SYSCALL, NS | typed Vodka syscall registry/result codes | PLANNED | 12/15 | Syscall/valid/invalid | No arbitrary reflection dispatch. |
| `MSG_TERM` | SYSCALL | capability-scoped terminal output/file delivery | PLANNED | 12/15 | Syscall/msg-term | Process cannot select an unrelated terminal. |
| `ULOGIN` | SYSCALL | kernel authentication request | PLANNED | 12/15 | Syscall/login spoof denial | Identity derived server-side. |
| `UGROUP` | SYSCALL | privileged group update | PLANNED | 12/15 | Syscall/group authorization | Cannot self-elevate. |
| `ULIST` | SYSCALL | permission-safe session listing | PLANNED | 12/15 | Syscall/user list | Bounded/redacted. |
| `UMSG` | SYSCALL | authenticated user message | PLANNED | 12/15 | Syscall/user message | Honors recipient policy. |
| `UINPUT` | SYSCALL | trusted driver-to-session input bridge | PLANNED | 12/15 | Syscall/input capability | Not exposed as client identity override. |
| `DMSG` | SYSCALL | message an opaque device handle | PLANNED | 12/15 | Syscall/device message | No raw `EntityUid`. |
| `DLIST` | SYSCALL | list authorized device capabilities | PLANNED | 12/15 | Syscall/device list | Topology and permissions filter results. |
| `DGET` | SYSCALL | acquire device capability by discoverable address/type | PLANNED | 12/15 | Syscall/device get | Generation checked. |
| `DSCAN` | SYSCALL | bounded topology rescan | PLANNED | 12/15 | Syscall/device scan | Rate limited. |
| `EXIT` | SYSCALL | exit calling process | PLANNED | 12/15 | Syscall/exit | Caller derived from execution context. |
| `TSPAWN` | SYSCALL | spawn authorized executable | PLANNED | 12/15 | Syscall/spawn limits | Execute and ownership checks. |
| `TFORK` | SYSCALL | fork current runtime where supported | PLANNED | 12/15 | Syscall/fork depth | Vodka context copied within data limits. |
| `TKILL` | SYSCALL | ownership-checked child/process kill | PLANNED | 12/15 | Syscall/kill authorization | Stale PID safe. |
| `TLIST` | SYSCALL | list visible child processes | PLANNED | 12/15 | Syscall/task list | Does not leak other users. |
| `FGET` | SYSCALL | permission-checked VFS stat/read handle | PLANNED | 12/15 | Syscall/file get | No direct mutable node exposure. |
| `FKILL` | SYSCALL | permission-checked VFS delete | PLANNED | 12/15 | Syscall/file kill | Root/proc/run protected. |
| `FMODE` | SYSCALL | mode metadata update | PLANNED | 12/15 | Syscall/file mode | Uses centralized policy. |
| `FOWNER` | SYSCALL | owner/group metadata update | PLANNED | 12/15 | Syscall/file owner | Uses centralized policy. |
| `FWRITE` | SYSCALL | create/replace/append through bounded VFS API | PLANNED | 12/15 | Syscall/file write | Atomic and quota-aware. |
| `CONFGET` | SYSCALL | read authorized configuration document | PLANNED | 12/15 | Syscall/config authorization | No host configuration exposure. |
| `MOUNT` | SYSCALL | attach mountable device capability | PLANNED | 12/15 | Syscall/mount capability | Consumes the complete server-authoritative media semantics delivered by PR 07. |
| `TEXIT` message | SYSCALL, SHELL | child-exit notification | PLANNED | 12/15 | Syscall/message child exit | Typed kernel event. |
| `RECVFILE` message | SYSCALL, SHELL | bounded file-transfer notification | PLANNED | 12/15 | Syscall/message file | Data copied/validated. |
| `BREAK` message | SYSCALL, SHELL | cancellation/break request | PLANNED | 12/15 | Syscall/message break | Cooperates with process cancellation. |
| `REPLY` message | SYSCALL, DRV | typed request/reply response | PLANNED | 12/15 | Syscall/message reply correlation | Correlation IDs unguessable/scoped. |
| 1,000-call stress and malformed handles | SYSCALL | ABI stress/security suite | PLANNED | 12/15 | Syscall/stress | Includes disappearing devices and concurrency. |

## Networking

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Network addresses and device tags | PACKET, MF | server-assigned unique address plus typed capability tags | PLANNED | 13/15 | Network/address uniqueness | Duplicate protection and deterministic conflict policy. |
| Wired datanet topology | DATANET, MF | explicit graph of connected data ports/links | PLANNED | 13/15 | Network/wired partitions | Never a global entity query. |
| Radio topology/frequencies | PACKET, PERIPH, DRV | frequency-scoped links with range/interference policy | PLANNED | 13/15 | Network/radio range/jammer | Uses relevant Robust abstractions. |
| Terminal/mainframe addressing | TERM, MF | connection endpoint identities bound to sessions | PLANNED | 13/15 | Network/two terminals/mainframes | Session cannot migrate by forged address. |
| Discovery ping and filtered scan | TERM, KERNEL, PACKET | bounded discovery request/reply | PLANNED | 13/15 | Network/discovery/rate limit | Amplification prevention retained semantically. |
| Request/reply correlation | DRV, PACKET | bounded pending-request table and logical timeout | PLANNED | 13/15 | Network/request timeout | Cleans on disconnect. |
| Packet/file payload | PACKET, TERM | validated DTO payload and bounded VFS transfer | PLANNED | 13/15 | Network/malformed/oversize | No privileged object graph deserialization. |
| Routing by address/tag | PACKET | indexed exact/tag routing | PLANNED | 13/15 | Network/routing/isolation | Broadcast only when protocol explicitly permits. |
| Network partitions and reconnect | DATANET, MF | topology-change events and deterministic disconnect/recovery | PLANNED | 13/15 | Network/partition/reconnect | Processes receive stable failure. |
| Packet loss/timeout behavior | PACKET | explicit bounded failure contract | PLANNED | 13/15 | Network/loss behavior | No hidden infinite retry. |
| Cross-network denial | PACKET, DATANET | membership/routing boundary | PLANNED | 13/15 | Network/cross-network denial | Capability cannot bypass topology. |
| Wireless, wired and omni adapters | PERIPH | port profiles over common DWAINE network API | PLANNED | 13/15 | Network/adapter profiles | Hardware constraints remain explicit. |
| Network radio mount/channel files | DRV | capability-backed channel endpoints | PLANNED | 13/15 | Network/radio VFS | Frequency and payload bounds. |
| Packet sniffer | APPS, PACKET | permission-gated diagnostic capture | PLANNED | 13/15 | Network/sniffer redaction | Privileged fields and secrets redacted. |
| Network metrics | PACKET | counters for messages, drops, broadcasts and queue pressure | PLANNED | 15/15 | Hardening/network metrics | Debug-only exposure is access controlled. |

## Storage media and services

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Fixed disks and memory cores | MEDIA, MF | persistent fixed-media entity and bounded volume | IMPLEMENTED | 07/15 | `SlotsKindsAndReadOnlyMediaAreValidatedServerSide`, `ProductionPrototypesComposeDrivesAndEveryMediaKind` | Fixed media cannot be normally ejected; destruction cleans both sides without leaving a mounted volume. Bootstrap program content remains owned by its program/service PR. |
| Floppy/removable disks | MEDIA, PERIPH | removable bounded volume media held in physical ECS container slots | IMPLEMENTED | 07/15 | `RemovableDiskPersistsAcrossFlushEjectAndReinsert`, `SlotsKindsAndReadOnlyMediaAreValidatedServerSide` | Read-only media, slot collisions, dirty eject denial and reinsertion persistence are enforced server-side. |
| Tapes and tape drive | MEDIA, PERIPH, MACHINE | removable tape-profile media volume | IMPLEMENTED | 07/15 | `SlotsKindsAndReadOnlyMediaAreValidatedServerSide` | The audited tape is a data-volume/profile carrier rather than a separate sequential-access protocol; specialized boot/research contents land with their owning subsystems. |
| Boot/recovery tape | MEDIA, OS | authorized recovery-media profile over the PR 07 tape volume | PLANNED | 15/15 | Storage/recovery boot | Untrusted media cannot inject host code; this waits for the real shell/program registry. |
| Databank remote storage | MACHINE, DRV | network storage service and mounted volume | PLANNED | 14/15 | Storage/databank sync/removal | Builds on PR 07 mounts and PR 13 networking; persistence and disconnect semantics are tested. |
| Archive persistence | VFS, UTIL, MEDIA | archives stored across removal, flush, reinsertion and reboot | IMPLEMENTED | 07/15 | `ArchivesNestedInsideArchivesPreserveEmbeddedPayload`, `RemovableDiskPersistsAcrossFlushEjectAndReinsert` | Nested archive payload, quota/depth and deep-copy boundaries are preserved. |
| Email backend | EMAIL | mailbox service over VFS records | PLANNED | 14/15 | Service/email send/receive/delete | Users, groups and destinations validated. |
| Email client protocol | EMAIL, DRV | terminal/service API for index/get/send/delete | PLANNED | 14/15 | Service/email protocol | Original UI text/localization. |
| Group and broadcast mail | EMAIL | authorized distribution groups | PLANNED | 14/15 | Service/email groups/isolation | Prevents unauthorized broadcast. |
| Document store and help records | DOC, VFS | localized generated manuals and user documents | PLANNED | 14/15 | Service/documents persistence | No reference prose copied. |
| Access/system logging service | LOG | bounded append-only structured logs | PLANNED | 14/15 | Service/log write/query/rotation | Permissions and retention enforced. |
| Log reader/mount/archive exchange | LOG, DRV | capability-backed log query and export | PLANNED | 14/15 | Service/log reader malformed query | No arbitrary entity lookup. |
| Printer service and spool | DRV, MACHINE | bounded print queue and printer driver | PLANNED | 14/15 | Service/printer status/queue/device loss | Queue cannot grow without bound. |
| Service terminals | DRV | noninteractive least-privilege service sessions | PLANNED | 14/15 | Service/terminal identity/cleanup | No implicit sysop login. |
| System records and MOTD/help | MEDIA, DOC | original localized system documents | PLANNED | 14/15 | Service/bootstrap documents | Reflects implemented behavior only. |

## Station devices, drivers, and Computer3 applications

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Base driver status/message contract | DRV | typed Vodka Device ABI adapter | PLANNED | 12/15 | Driver/base malformed/offline | Explicit capability per command. |
| User-terminal driver | DRV | terminal session capability | PLANNED | 12/15 | Driver/user terminal | Cannot impersonate another user. |
| Databank driver | DRV, MACHINE | network storage driver | PLANNED | 14/15 | Driver/databank | Mount lifecycle authoritative. |
| Printer driver | DRV, MACHINE | printer status/spool driver | PLANNED | 14/15 | Driver/printer | Bounded queue. |
| Logreader driver | LOG | log query/export driver | PLANNED | 14/15 | Driver/logreader | Permission isolated. |
| Radio driver | DRV, PACKET | frequency/channel messaging driver | PLANNED | 13/15 | Driver/radio | Topology and authorization checked. |
| Service-terminal driver | DRV | least-privilege service invocation driver | PLANNED | 14/15 | Driver/service terminal | No blanket root account. |
| Communication-dish driver | DRV, APPS | communications report capability | PLANNED | 14/15 | Driver/communications dish | Uses existing Whiskey communications where appropriate. |
| Telepad driver and `teleman` interface | DRV, TELE | coordinate/send/receive/portal/scan capability | PLANNED | 15/15 | Driver/telesci commands/offline/access | Strong access and safety policy. |
| Long-range destination records | TELE, VFS | validated named coordinate documents | PLANNED | 15/15 | Driver/telesci record | No raw world coordinates from client. |
| Nuclear-charge driver and manager | DRV, MACHINE | multi-authorization audited device capability | PLANNED | 15/15 | Driver/nuke auth/timer/abort | Uses existing nuke safety/access rules. |
| Guardbot dock driver and `prman` | DRV, GUARDBOT | explicit bot task/status/recall capability | PLANNED | 15/15 | Driver/guardbot upload/wake/wipe/recall | No arbitrary bot entity access. |
| Guardbot task documents | GUARDBOT, DOC | typed task/config documents | PLANNED | 15/15 | Driver/guardbot task validation | Original examples only. |
| IR security detector driver | DRV, MACHINE | sensor status capability | PLANNED | 15/15 | Driver/IR detector | Reference activate/deactivate stubs are not claimed. |
| APC remote-power driver | DRV, EXTERNAL | scoped equipment/light/environment control capability | PLANNED | 15/15 | Driver/APC access/offline | Uses explicit APC network membership. |
| HEPT emitter driver and manager | DRV, MACHINE | explicit emitter capability if Whiskey has a matching machine | PLANNED | 15/15 | Driver/HEPT | PR 15 may mark N/A only with repository evidence. |
| H7 automated security init | DRV, EXTERNAL | bounded event-driven sensor/APC/guardbot automation | PLANNED | 15/15 | Driver/H7 multi-device automation | Demonstrates emergent Vodka automation safely. |
| Generic test apparatus driver | DRV, MACHINE | typed sensor/enactor ABI: info/status/peek/poke/read/pulse | PLANNED | 15/15 | Driver/test apparatus matrix | Field schema is per device capability. |
| Pitching machine | MACHINE, ART | enactor driver profile | PLANNED | 15/15 | Driver/pitcher | Bounded actuation. |
| Impact pad | MACHINE, ART | sensor driver profile | PLANNED | 15/15 | Driver/impact sensor | Bounded readings. |
| Electrical apparatus | MACHINE, ART | sensor/enactor profile | PLANNED | 15/15 | Driver/electrical apparatus | Validated fields. |
| X-ray scanner | MACHINE, ART | research sensor profile | PLANNED | 15/15 | Driver/xray | Privacy/access enforced. |
| Heater plate | MACHINE, ART | bounded heater enactor profile | PLANNED | 15/15 | Driver/heater safety | Server clamps safe range. |
| Laser emitter/receiver | MACHINE, ART | explicit paired sensor/enactor profiles | PLANNED | 15/15 | Driver/laser pair | Topology and safety checked. |
| Gas sensor | MACHINE, ART | atmosphere-reading profile | PLANNED | 15/15 | Driver/gas sensor | Safe bounded data. |
| Mechanics I/O block | MACHINE, ART | explicit logic I/O capability | PLANNED | 15/15 | Driver/mechanics I/O | No universal arbitrary event API. |
| Artifact console and `gptio` | ART | artifact research coordinator and apparatus capabilities | PLANNED | 15/15 | Driver/artifact workflow | Integrates only supported Whiskey artifact mechanics. |
| Medical records application | APPS | permission-scoped medical record service/driver | PLANNED | 14/15 | Driver/medical records | Uses Whiskey data models, not copied UI. |
| Security records application | APPS | permission-scoped security record service/driver | PLANNED | 14/15 | Driver/security records | Audit trail required. |
| Bank/account records application | APPS | permission-scoped economy account service/driver | PLANNED | 14/15 | Driver/bank transfers/logs | Monetary mutations transactional. |
| Job-control application | APPS | command/job-management capability | PLANNED | 14/15 | Driver/job control access | Uses current Whiskey job systems. |
| Communications application | APPS | announcements/report communication service | PLANNED | 14/15 | Driver/communications authorization | Existing station policy preserved. |
| Engine-control application | APPS | explicit engine telemetry/control drivers | PLANNED | 15/15 | Driver/engine controls | No universal machinery API. |
| Writer/editor application | APPS | terminal document create/edit workflow | PLANNED | 14/15 | Service/document editor | Bounded and VFS-backed. |
| Signal catcher | APPS, PACKET | permission-gated bounded receive queue | PLANNED | 14/15 | Driver/signal catcher | No unrestricted eavesdropping. |
| Ping utility application | APPS, PACKET | diagnostic request/reply tool | PLANNED | 13/15 | Network/ping tool | Rate limited. |
| File-transfer application | APPS, TERM | VFS/network transfer command | PLANNED | 13/15 | Network/file transfer | Validates size, type and destination. |
| SigPal signal viewer | APPS, PACKET | structured signal inspection tool | PLANNED | 14/15 | Driver/signal viewer redaction | Secrets redacted. |
| SigCraft signal authoring | APPS, PACKET | schema-validated signal construction tool | PLANNED | 14/15 | Driver/signal craft authorization | Cannot fabricate privileged capabilities. |
| Disease research compatibility entry | APPS | supported research service alias or justified N/A | PLANNED | 15/15 | Driver/research alias | Reference type has no independent implementation. |
| Artifact research compatibility entry | APPS, ART | artifact service launcher | PLANNED | 15/15 | Driver/artifact launcher | Uses actual Whiskey driver. |
| Manifest application | APPS | read-only crew manifest service | PLANNED | 14/15 | Driver/manifest privacy | Redacts protected data. |
| Robotics research compatibility entry | APPS, GUARDBOT | robotics/guardbot service launcher | PLANNED | 15/15 | Driver/robotics launcher | Reference type has minimal independent behavior. |
| Code reader/authentication disks | APPS, MEDIA | validated code-document reader if supported | PLANNED | 15/15 | Driver/code reader | Never imports reference codes/content. |

## Hardening, acceptance, and classified non-functional material

| GOON FEATURE | GOON SOURCE | WHISKEY EQUIVALENT | STATUS | TARGET PR | TEST | NOTES |
| --- | --- | --- | --- | --- | --- | --- |
| Parser and path fuzzing | SHELL, VFS | malformed-input corpora and property tests | PLANNED | RELEASE GATE | Hardening/fuzz | Includes Unicode, depth and cycle cases. |
| Network message fuzzing | PACKET, DRV | bounded DTO fuzz corpus | PLANNED | RELEASE GATE | Hardening/network fuzz | No privileged deserialization. |
| Process and VM stress | MF, KERNEL, SHELL | 512-process and hostile-script scenarios | PLANNED | RELEASE GATE | Hardening/process stress | Scheduler remains bounded. |
| Four mainframes / 32 terminals / 128 sessions | MF, TERM | scale integration scenario | PLANNED | RELEASE GATE | Hardening/many terminals | Includes partitions and reconnects. |
| Thousands of files and concurrent devices | VFS, DRV | quota/performance scenario | PLANNED | RELEASE GATE | Hardening/VFS/device stress | Allocation and asymptotic review. |
| Repeated boot/shutdown/round restart | MF, KERNEL | lifecycle soak test | PLANNED | RELEASE GATE | Hardening/cleanup soak | No subscriptions or sessions leak. |
| Runtime diagnostics | PACKET, KERNEL | process, instruction, load, message and VFS counters | PLANNED | 15/15 | Hardening/metrics | Access-controlled server-side debug surface is a functional PR 15 deliverable. |
| End-to-end player smoke route | all functional sources | power → connect → boot → login → shell → VFS → Vodka → device → service → reconnect | PLANNED | RELEASE GATE | DWAINE/E2E smoke | Persistent consistency verified. |
| Final HEAD re-audit | all sources | rerun pinned methodology against then-current Goon HEAD | PLANNED | RELEASE GATE | Parity ledger audit | New findings must be implemented/classified in the owning layer. |
| Local teaching guide | DOC and implemented Whiskey code | `Docs/DWAINE_VODKA_CODE_TEACHING_LOCAL.txt` generated from exact registered commands/contracts | PLANNED | RELEASE GATE | Teaching command validation | No fictional commands, paths, drivers, syscalls or devices. |
| Legacy ThinkDOS as a separate operating system | C3 and `base_os.dm` | DWAINE hardware/VFS/shell absorbs relevant behavior | NOT APPLICABLE | 01/15 | Matrix review | Goal is DWAINE; recreating a second obsolete OS adds no DWAINE capability. |
| Reference `file_run` command | TERM | none | NOT APPLICABLE | 01/15 | Matrix review | The audited command is explicitly inoperative, so there is no functional behavior to reproduce. |
| Adventure-zone lore records and random mail prose | EXTERNAL, DOC | original Whiskey documents where gameplay needs fixtures | NOT APPLICABLE | 01/15 | License review | Creative text is not a subsystem and is not copied. |
| Existing Goon maps and placements | EXTERNAL and repository maps | Whiskey `_Whiskey` prototypes/maps only where maintainers permit | NOT APPLICABLE | 01/15 | License review | Placement data adds no new runtime feature and is not imported. |
| Goon sprites, sounds, computer ambience, and AI skin reward | C3, external assets/reward references | original/licensed Whiskey presentation assets only | NOT APPLICABLE | 01/15 | License review | Functional state does not depend on protected media. |
| `DWAINE for Dummies` and guardbot book prose | repository-wide book search | original in-game help plus release-gate teaching guide | NOT APPLICABLE | 01/15 | License review | The educational function is implemented; source prose is not copied. |

## Closure rule

A row may become `IMPLEMENTED` only when its gameplay path, authority checks, failure behavior, cleanup, tests, and documentation are present. UI-only, debug-only, mocked, hardcoded-test, placeholder, or permission-incomplete work remains unimplemented. Any newly discovered dependency is added as a row and assigned to the earliest technically correct remaining PR.
