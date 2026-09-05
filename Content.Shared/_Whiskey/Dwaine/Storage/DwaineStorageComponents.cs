// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Whiskey.Dwaine.Storage;

using Robust.Shared.Prototypes;

public enum DwaineStorageMediaKind : byte
{
    HardDrive,
    RemovableDisk,
    Tape,
}

/// <summary>
/// Physical storage medium configuration. The logical tree is server-only and lives in the runtime component.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineStorageMediaComponent : Component
{
    public const int HardMaxLabelLength = 48;

    [DataField]
    public DwaineStorageMediaKind Kind = DwaineStorageMediaKind.RemovableDisk;

    [DataField]
    public string Label = "media";

    [DataField]
    public bool Removable = true;

    [DataField]
    public bool ReadOnly;

    [DataField]
    public int MaxNodes = 4096;

    [DataField]
    public int MaxDepth = 24;

    [DataField]
    public int MaxNameLength = 64;

    [DataField]
    public int MaxPathLength = 1024;

    [DataField]
    public int MaxChildrenPerDirectory = 256;

    [DataField]
    public int MaxLinkDepth = 16;

    [DataField]
    public int MaxTextCharacters = 65_536;

    [DataField]
    public int MaxRecordEntries = 256;

    [DataField]
    public int MaxRecordCharacters = 65_536;

    [DataField]
    public int MaxArchiveEntries = 1024;

    [DataField]
    public int MaxArchiveDepth = 16;
}

/// <summary>
/// Server-authoritative drive/bay policy. Slots accept only explicitly enabled media classes.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineStorageDriveComponent : Component
{
    [DataField]
    public bool AcceptHardDrives = true;

    [DataField]
    public bool AcceptRemovableDisks = true;

    [DataField]
    public bool AcceptTapes = true;

    /// <summary>
    /// Fixed media created and inserted during map initialization, bounded by the physical slot count.
    /// </summary>
    [DataField]
    public List<EntProtoId> StartingMedia = [];
}

/// <summary>
/// Declares a fixed, data-only boot profile. It authorizes a boot source but never carries native code.
/// </summary>
[RegisterComponent]
public sealed partial class DwaineBootMediaComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField(required: true)]
    public string Profile = string.Empty;
}
