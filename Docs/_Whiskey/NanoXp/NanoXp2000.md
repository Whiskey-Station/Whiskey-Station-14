<!-- SPDX-FileCopyrightText: 2026 Whiskey Station Contributors -->
<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->

# Nano XP 2000

Nano XP 2000 is Whiskey Station's compact, station-local desktop for computers, handheld computers, and PDAs. It is an additional interface: existing console, PDA, DWAINE, wire, uplink, instrument, health-analyzer, and cartridge interfaces are not replaced.

Players open it from the entity's alternative-interaction menu. Every `BaseComputer`, `BaseHandheldComputer`, and `BasePDA` descendant inherits the launcher. The server registers the Nano XP BUI dynamically, so child prototypes can keep their existing `UserInterface` dictionaries unchanged.

## Apps

- **Desktop** presents device, account, job, and department information.
- **G-Mail** is the in-universe Galactic Mail app requested for PDAs. Its `@gmail.nano` addresses and messages exist only for the current station round; it is not Google Gmail and makes no external HTTP request.
- **Department** reports whether the authenticated account is authorized on the current computer.
- **NanoNet** is the station-local account directory and service browser.
- **DWAINE** opens the existing DWAINE terminal BUI when that device actually has DWAINE terminal hardware.

## Identity and access flow

```text
named ID inside PDA
        |
        v
server derives name, job, departments, and access tags
        |
        v
player chooses password -> salted PBKDF2 verifier in station NanoNet
        |
        v
account + @gmail.nano address
        |
        +--> same PDA: ID binding + password required
        |
        +--> computer: password + that computer's AccessReader policy required
```

Account creation is possible only from a PDA containing a named ID card. The client sends a password but never supplies the account name, job, department, access tags, session identifier, station, or authorization result. The server obtains those values from the contained ID and the target computer.

On subsequent successful PDA logins, display name, job, department, and access tags are refreshed from the current authoritative ID state. A PDA accepts only the account bound to its inserted ID. A computer without an access reader is public; a computer with an access reader evaluates the account's stored access set through the existing access system, including contained access providers.

## Security and resource bounds

- Passwords and password verifiers never enter a component state or UI response. Input fields are cleared immediately after submission.
- UI state is sent directly to the requesting actor rather than stored as shared BUI state. Concurrent viewers cannot receive one another's mailbox or session data.
- Login failures use per-actor exponential backoff. Mail submission has a server-time rate limit.
- Each NanoNet supports at most 256 accounts, 512 sessions, 64 messages per mailbox, and bounded address, subject, and body sizes.
- NanoNet ownership is the owning station, falling back to the map for test and off-station environments. Deleting that owner deletes its server-only store.
- NanoNet is logical station infrastructure. It does not expose the host network or perform DNS, HTTP, filesystem, or process access.

## Compatibility

Nano XP depends on the existing DWAINE identity primitive only for password hashing, constant-time password comparison, bounded sessions, and revocation. It does not bypass DWAINE transport, shell, VFS, process, device, or service authority. Launching the DWAINE app delegates to the existing DWAINE terminal interface on compatible hardware.

PR 15 also fixes DWAINE storage cleanup during map/grid termination: forced container removal no longer attempts to reparent media into an already terminating world hierarchy.
