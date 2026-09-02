// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Whiskey.Dwaine.Prototypes;

/// <summary>
/// Identifies the canonical DWAINE architecture and scripting specification.
/// Runtime tuning and gameplay state deliberately do not belong in this contract.
/// </summary>
[Prototype]
public sealed partial class DwaineArchitecturePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string SpecificationVersion { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string VodkaCodeFileExtension { get; private set; } = string.Empty;
}
