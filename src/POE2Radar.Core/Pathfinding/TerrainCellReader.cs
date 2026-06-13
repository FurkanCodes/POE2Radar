using POE2Radar.Core.Game;

namespace POE2Radar.Core.Pathfinding;

/// <summary>
/// <see cref="ICellReader"/> adapter over POE2Radar's binary walkable grid
/// (<see cref="Poe2Live.TerrainData"/>: <c>byte[] Walkable</c>, 0 = blocked / 1 = walkable,
/// indexed <c>Walkable[y * Width + x]</c>).
///
/// <para>The grid is binary, so every walkable cell has the same value (1). Pathfinding over
/// this reader should pass <c>flatCost: true</c> to A* so every walkable step costs the same.
/// <see cref="Read"/> returns the raw cell value (1 or 0) and 0 for out-of-bounds.</para>
/// </summary>
public sealed class TerrainCellReader : ICellReader
{
    private readonly byte[] _walkable;
    private readonly HashSet<int>? _forcedWalkable;

    public int Width  { get; }
    public int Height { get; }

    public TerrainCellReader(Poe2Live.TerrainData terrain, IEnumerable<PathCell>? forcedWalkable = null)
    {
        _walkable = terrain.Walkable;
        Width  = terrain.Width;
        Height = terrain.Height;
        if (forcedWalkable is not null)
        {
            _forcedWalkable = new HashSet<int>();
            foreach (var c in forcedWalkable)
            {
                if ((uint)c.X >= (uint)Width || (uint)c.Y >= (uint)Height) continue;
                _forcedWalkable.Add(c.Y * Width + c.X);
            }
            if (_forcedWalkable.Count == 0) _forcedWalkable = null;
        }
    }

    public int Read(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        var idx = y * Width + x;
        if (_forcedWalkable?.Contains(idx) == true) return 1;
        return _walkable[idx];
    }
}
