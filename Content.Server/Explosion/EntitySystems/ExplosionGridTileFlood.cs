using System.Numerics;
using System.Linq;
using Content.Shared.Atmos;
using Content.Shared.FixedPoint;
using Robust.Shared.Map.Components;
using static Content.Server.Explosion.Components.ExplosionAirtightGridComponent;
using static Content.Server.Explosion.EntitySystems.ExplosionSystem;

namespace Content.Server.Explosion.EntitySystems;

/// <summary>
///     See <see cref="ExplosionTileFlood"/>. Each instance of this class corresponds to a seperate grid.
/// </summary>
public sealed class ExplosionGridTileFlood : ExplosionTileFlood
{
    private readonly ExplosionSystem _explosionSystem;

    public Entity<MapGridComponent> Grid;
    private bool _needToTransform = false;

    private Matrix3x2 _matrix = Matrix3x2.Identity;
    private Vector2 _offset;

    // Tiles which neighbor an exploding tile, but have not yet had the explosion spread to them due to an
    // airtight entity on the exploding tile that prevents the explosion from spreading in that direction. These
    // will be added as a neighbor after some delay, once the explosion on that tile is sufficiently strong to
    // destroy the airtight entity.
    private Dictionary<int, List<(Vector2i, AtmosDirection)>> _delayedNeighbors = new();

    private Dictionary<Vector2i, TileData> _airtightMap;

    private float _maxIntensity;
    private float _intensityStepSize;
    private int _typeIndex;

    private UniqueVector2iSet _spaceTiles = new();
    private UniqueVector2iSet _processedSpaceTiles = new();

    public HashSet<Vector2i> SpaceJump = new();

    private Dictionary<Vector2i, NeighborFlag> _edgeTiles;

    public int LastAddedTileCount { get; private set; }

    public ExplosionGridTileFlood(
        Entity<MapGridComponent> grid,
        Dictionary<Vector2i, TileData> airtightMap,
        float maxIntensity,
        float intensityStepSize,
        int typeIndex,
        Dictionary<Vector2i, NeighborFlag> edgeTiles,
        EntityUid? referenceGrid,
        Matrix3x2 spaceMatrix,
        Angle spaceAngle,
        ExplosionSystem explosionSystem)
    {
        Grid = grid;
        _airtightMap = airtightMap;
        _maxIntensity = maxIntensity;
        _intensityStepSize = intensityStepSize;
        _typeIndex = typeIndex;
        _edgeTiles = edgeTiles;
        _explosionSystem = explosionSystem;

        // initialise SpaceTiles
        foreach (var (tile, spaceNeighbors) in _edgeTiles)
        {
            for (var i = 0; i < NeighbourVectors.Length; i++)
            {
                var dir = (NeighborFlag) (1 << i);
                if ((spaceNeighbors & dir) != NeighborFlag.Invalid)
                    _spaceTiles.Add(tile + NeighbourVectors[i]);
            }
        }

        if (referenceGrid == Grid.Owner)
            return;

        _needToTransform = true;
        var entityManager = IoCManager.Resolve<IEntityManager>();

        var transformSystem = entityManager.System<SharedTransformSystem>();
        var transform = entityManager.GetComponent<TransformComponent>(Grid.Owner);
        var size = (float)Grid.Comp.TileSize;

        _matrix.M31 = size / 2;
        _matrix.M32 = size / 2;
        Matrix3x2.Invert(spaceMatrix, out var invSpace);
        var (_, relativeAngle, worldMatrix) = transformSystem.GetWorldPositionRotationMatrix(transform);
        relativeAngle -= spaceAngle;
        _matrix *= worldMatrix * invSpace;
        _offset = relativeAngle.RotateVec(new Vector2(size / 4, size / 4));
    }

    public override void InitTile(Vector2i initialTile)
    {
        TileLists[0] = new() { initialTile };

        if (_airtightMap.ContainsKey(initialTile))
            EnteredBlockedTiles.Add(initialTile);
        else
            ProcessedTiles.Add(initialTile);
    }

    public IEnumerable<int> AddNewTiles(int iteration, HashSet<Vector2i>? gridJump)
    {
        SpaceJump = new();
        NewTiles = new();
        NewBlockedTiles = new();

        // Mark tiles as entered if any were just freed due to airtight/explosion blockers being destroyed.
        if (FreedTileLists.TryGetValue(iteration, out var freed))
        {
            NewFreedTiles = new();
            var freedFrontier = freed.ToArray();
            foreach (var tile in freedFrontier)
            {
                if (EnteredBlockedTiles.Add(tile))
                    NewFreedTiles.Add(tile);
                yield return 1;
            }
            FreedTileLists[iteration] = NewFreedTiles;
        }
        else
        {
            NewFreedTiles = new();
            FreedTileLists[iteration] = NewFreedTiles;
        }

        // Add adjacent tiles
        if (TileLists.TryGetValue(iteration - 2, out var adjacent))
        {
            foreach (var work in AddNewAdjacentTiles(iteration, adjacent, false))
                yield return work;
        }
        if (FreedTileLists.TryGetValue(iteration - 2, out var delayedAdjacent))
        {
            foreach (var work in AddNewAdjacentTiles(iteration, delayedAdjacent, true))
                yield return work;
        }

        // Add diagonal tiles
        if (TileLists.TryGetValue(iteration - 3, out var diagonal))
        {
            foreach (var work in AddNewDiagonalTiles(iteration, diagonal, false))
                yield return work;
        }
        if (FreedTileLists.TryGetValue(iteration - 3, out var delayedDiagonal))
        {
            foreach (var work in AddNewDiagonalTiles(iteration, delayedDiagonal, true))
                yield return work;
        }

        // Add delayed tiles
        foreach (var work in AddDelayedNeighbors(iteration))
            yield return work;

        // Tiles from Spaaaace
        if (gridJump != null)
        {
            var jumpFrontier = gridJump.ToArray();
            foreach (var tile in jumpFrontier)
            {
                ProcessNewTile(iteration, tile, AtmosDirection.Invalid);
                yield return 1;
            }
        }

        // Store new tiles
        if (NewTiles.Count != 0)
            TileLists[iteration] = NewTiles;
        if (NewBlockedTiles.Count != 0)
            BlockedTileLists[iteration] = NewBlockedTiles;

        LastAddedTileCount = NewTiles.Count + NewBlockedTiles.Count;
    }

    protected override void ProcessNewTile(int iteration, Vector2i tile, AtmosDirection entryDirections)
    {
        // Is there an airtight blocker on this tile?
        if (!_airtightMap.TryGetValue(tile, out var tileData))
        {
            // No blocker. Ezy. Though maybe this a space tile?

            if (_spaceTiles.Contains(tile))
                JumpToSpace(tile);
            else if (ProcessedTiles.Add(tile))
                NewTiles.Add(tile);

            return;
        }

        // If the explosion is entering this new tile from an unblocked direction, we add it directly. Note that because
        // for space -> grid jumps, we don't have a direction from which the explosion came, we will only assume it is
        // unblocked if all space-facing directions are unblocked. Though this could eventually be done properly.

        bool blocked;
        var blockedDirections = tileData.BlockedDirections;
        if (entryDirections == AtmosDirection.Invalid) // is coming from space?
        {
            blocked = AnyNeighborBlocked(_edgeTiles[tile], blockedDirections); // at least one space direction is blocked.
        }
        else
            blocked = (blockedDirections & entryDirections) == entryDirections;// **ALL** entry directions are blocked

        if (blocked)
        {
            // was this tile already entered from some other direction?
            if (EnteredBlockedTiles.Contains(tile))
                return;

            // Did the explosion already attempt to enter this tile from some other direction?
            if (!UnenteredBlockedTiles.Add(tile))
                return;

            NewBlockedTiles.Add(tile);

            // At what explosion iteration would this blocker be destroyed?
            var required = _explosionSystem.GetToleranceValues(tileData.ToleranceCacheIndex).Values[_typeIndex];
            if (required > _maxIntensity)
                return; // blocker is never destroyed.

            var clearIteration = GetClearIteration(iteration, required, _intensityStepSize);
            if (FreedTileLists.TryGetValue(clearIteration, out var list))
                list.Add(tile);
            else
                FreedTileLists[clearIteration] = new() { tile };

            return;
        }

        // was this tile already entered from some other direction?
        if (!EnteredBlockedTiles.Add(tile))
            return;

        // Did the explosion already attempt to enter this tile from some other direction?
        if (UnenteredBlockedTiles.Contains(tile))
        {
            NewFreedTiles.Add(tile);
            return;
        }

        // This is a completely new tile, and we just so happened to enter it from an unblocked direction.
        NewTiles.Add(tile);
    }

    private void JumpToSpace(Vector2i tile)
    {
        // Did we already jump/process this tile?
        if (!_processedSpaceTiles.Add(tile))
            return;

        if (!_needToTransform)
        {
            SpaceJump.Add(tile);
            return;
        }

        var center = Vector2.Transform(tile, _matrix);
        SpaceJump.Add(new((int) MathF.Floor(center.X + _offset.X), (int) MathF.Floor(center.Y + _offset.Y)));
        SpaceJump.Add(new((int) MathF.Floor(center.X - _offset.Y), (int) MathF.Floor(center.Y + _offset.X)));
        SpaceJump.Add(new((int) MathF.Floor(center.X - _offset.X), (int) MathF.Floor(center.Y - _offset.Y)));
        SpaceJump.Add(new((int) MathF.Floor(center.X + _offset.Y), (int) MathF.Floor(center.Y - _offset.X)));
    }

    private IEnumerable<int> AddDelayedNeighbors(int iteration)
    {
        if (!_delayedNeighbors.TryGetValue(iteration, out var delayed))
            yield break;

        var frontier = delayed.ToArray();
        foreach (var (tile, direction) in frontier)
        {
            ProcessNewTile(iteration, tile, direction);
            yield return 1;
        }

        _delayedNeighbors.Remove(iteration);
    }

    // Gets the tiles that are directly adjacent to other tiles. If a currently exploding tile has an airtight entity
    // that blocks the explosion from propagating in some direction, those tiles are added to a list of delayed tiles
    // that will be added to the explosion in some future iteration.
    private IEnumerable<int> AddNewAdjacentTiles(int iteration, IEnumerable<Vector2i> tiles, bool ignoreTileBlockers = false)
    {
        var frontier = tiles.ToArray();
        foreach (var tile in frontier)
        {
            var blockedDirections = AtmosDirection.Invalid;
            FixedPoint2 sealIntegrity = 0;

            // Note that if (grid, tile) is not a valid key, then airtight.BlockedDirections will default to 0 (no blocked directions)
            if (_airtightMap.TryGetValue(tile, out var tileData))
            {
                blockedDirections = tileData.BlockedDirections;
                sealIntegrity = _explosionSystem.GetToleranceValues(tileData.ToleranceCacheIndex).Values[_typeIndex];
            }

            // First, yield any neighboring tiles that are not blocked by airtight entities on this tile
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (ignoreTileBlockers || !blockedDirections.IsFlagSet(direction))
                {
                    ProcessNewTile(iteration, tile.Offset(direction), i.ToOppositeDir());
                }
            }

            // If there are no blocked directions, we are done with this tile.
            if (ignoreTileBlockers || blockedDirections == AtmosDirection.Invalid)
            {
                yield return 1;
                continue;
            }

            // This tile has one or more airtight entities anchored to it blocking the explosion from traveling in
            // some directions. First, check whether this blocker can even be destroyed by this explosion?
            if (sealIntegrity > _maxIntensity)
            {
                yield return 1;
                continue;
            }

            // At what explosion iteration would this blocker be destroyed?
            var clearIteration = GetClearIteration(iteration, sealIntegrity, _intensityStepSize);

            // Get the delayed neighbours list
            if (!_delayedNeighbors.TryGetValue(clearIteration, out var list))
            {
                list = new();
                _delayedNeighbors[clearIteration] = list;
            }

            // Check which directions are blocked, and add them to the list.
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (blockedDirections.IsFlagSet(direction))
                {
                    list.Add((tile.Offset(direction), i.ToOppositeDir()));
                }
            }

            yield return 1;
        }
    }

    /// <summary>
    /// Calculates when an airtight blocker can be cleared without ever scheduling work into an iteration
    /// that has already been processed.
    /// </summary>
    /// <remarks>
    /// A stale airtight cache can briefly report negative remaining resistance when an entity has already taken
    /// enough damage to reach its destruction threshold. Scheduling that value into a past iteration can mutate the
    /// same freed-tile set currently being enumerated.
    /// </remarks>
    internal static int GetClearIteration(int iteration, FixedPoint2 required, float intensityStepSize)
    {
        var delay = Math.Max(0, (int) MathF.Ceiling((float) required / intensityStepSize));
        return iteration + delay;
    }

    protected override AtmosDirection GetUnblockedDirectionOrAll(Vector2i tile)
    {
        return ~_airtightMap.GetValueOrDefault(tile).BlockedDirections;
    }
}
