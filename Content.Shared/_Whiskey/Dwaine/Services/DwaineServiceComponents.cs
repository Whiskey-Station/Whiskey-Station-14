// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Services;

/// <summary>
/// Configures the bounded, server-authoritative DWAINE service suite. No mailbox, log entry,
/// station record or credential is networked to clients through this component.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineServiceSuiteComponent : Component
{
    public const int HardMaxMailMessages = 4096;
    public const int HardMaxMailPerUser = 256;
    public const int HardMaxMailSubjectCharacters = 256;
    public const int HardMaxMailBodyCharacters = 8192;
    public const int HardMaxLogEntries = 8192;
    public const int HardMaxServiceOutputCharacters = 32768;

    [DataField]
    public int MaxMailMessages = 1024;

    [DataField]
    public int MaxMailPerUser = 64;

    [DataField]
    public int MaxMailSubjectCharacters = 128;

    [DataField]
    public int MaxMailBodyCharacters = 2048;

    [DataField]
    public int MaxLogEntries = 1024;

    [DataField]
    public int MaxServiceOutputCharacters = 16384;
}

/// <summary>
/// Enables read-only station services and tightly scoped economy operations for a mainframe that
/// is physically part of a station. DWAINE accounts remain distinct from player/net identities.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineStationServiceBridgeComponent : Component
{
    [DataField]
    public bool Manifest = true;

    [DataField]
    public bool Bank = true;

    [DataField]
    public bool Records = true;

    [DataField]
    public bool Jobs = true;
}

/// <summary>
/// Marks a physical paper printer exposed only through an opaque DWAINE Device ABI handle.
/// Jobs are executed synchronously and never retained in an unbounded spool.
/// </summary>
[RegisterComponent]
public sealed partial class DwainePrinterComponent : Component
{
    public const int HardMaxDocumentCharacters = 8192;

    [DataField]
    public string PaperPrototype = "Paper";

    [DataField]
    public int MaxDocumentCharacters = 4096;

    [DataField]
    public bool Enabled = true;
}

/// <summary>
/// Opt-in marker for an APC variant deliberately exposed through the DWAINE Device ABI.
/// Ordinary station APCs are never globally discovered or remotely controlled.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineApcInterfaceComponent : Component
{
    [DataField]
    public bool Enabled = true;
}
