using System.Numerics;

namespace POE2Radar.Core.Game;

/// <summary>
/// Immutable resolved game chain for one cadence tick. Features should consume this instead of
/// calling <see cref="Poe2Live.TryResolve"/> on hot paths.
/// </summary>
public readonly record struct GameContextSnapshot(
    bool Valid,
    nint InGameState,
    nint AreaInstance,
    nint LocalPlayer,
    uint AreaHash,
    string AreaCode,
    int AreaLevel,
    string LeagueName,
    System.Numerics.Vector2? PlayerGrid,
    long Generation)
{
    public static readonly GameContextSnapshot Invalid = new(
        false, 0, 0, 0, 0, "", 0, "", null, 0);

    public bool AreaChanged(GameContextSnapshot prior)
        => Valid && prior.Valid && AreaInstance != prior.AreaInstance;
}
