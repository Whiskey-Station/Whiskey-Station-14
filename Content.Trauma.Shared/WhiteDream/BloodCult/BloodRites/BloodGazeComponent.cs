// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Trauma.Shared.WhiteDream.BloodCult.BloodRites;

// Whiskey - a toned-down, one-use blood rite based on the Star Gazer's beam.
[RegisterComponent]
public sealed partial class BloodGazeComponent : Component
{
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(9); // Whiskey - swapped with Blood Beam.

    [DataField]
    public TimeSpan ChantInterval = TimeSpan.FromSeconds(3); // Whiskey - swapped with Blood Beam.

    [DataField]
    public TimeSpan BloodTrailInterval = TimeSpan.FromSeconds(0.1);

    [DataField]
    public int ChantWords = 3; // Whiskey - swapped with Blood Beam.

    [DataField]
    public string BloodReagent = "Blood";

    [DataField]
    public FixedPoint2 BloodPerTile = 20; // Whiskey - a full, clearly visible puddle.

    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(1); // Whiskey

    [DataField]
    public EntProtoId ChargeEffect = "EffectBloodGazeOrb"; // Whiskey

    [DataField]
    public string CultTile = "CultFloor"; // Whiskey

    [DataField]
    public EntProtoId TileEffect = "CultTileSpawnEffect"; // Whiskey

    [DataField]
    public EntProtoId CultWall = "WallCult"; // Whiskey

    [DataField]
    public EntProtoId CultDoor = "CultDoor"; // Whiskey

    [DataField]
    public SoundSpecifier ChargeSound = new SoundPathSpecifier(
        "/Audio/_Goobstation/Wizard/lightning_chargeup.ogg"); // Whiskey - swapped with Blood Beam.

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Charging;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Fired;

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? User;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextChant;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextBloodTrail;

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? ChargeEffectEntity;

    public readonly HashSet<(EntityUid Grid, Vector2i Tile)> BloodiedTiles = new();
    public readonly HashSet<EntityUid> ConvertedStructures = new();
}
