// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Trauma.Common.Wizard;

/// <summary>
/// Entity with this component will be deleted if user attempts to drop it
/// </summary>
[RegisterComponent]
public sealed partial class DeleteOnDropAttemptComponent : Component
{
    [DataField]
    public bool DeleteOnAttempt = true; // Whiskey - some temporary rites must refuse an accidental drop instead.
}
