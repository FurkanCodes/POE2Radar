namespace POE2Radar.Core.Game;

/// <summary>
/// Entity list + area identity for radar, loot, HP bars, and future entity features.
/// Built from the world snapshot — no hot-path component-map walks here.
/// </summary>
public readonly record struct EntityContextSnapshot(
    bool Valid,
    nint AreaInstance,
    uint AreaHash,
    int AreaLevel,
    IReadOnlyList<Poe2Live.EntityDot> Entities,
    long Generation)
{
    public static readonly EntityContextSnapshot Invalid = new(
        false, 0, 0, 0, Array.Empty<Poe2Live.EntityDot>(), 0);

    public static EntityContextSnapshot FromWorld(WorldEntitySource world, long generation)
    {
        if (!world.InGame)
            return Invalid;
        return new EntityContextSnapshot(
            true,
            world.AreaInstance,
            world.AreaHash,
            world.AreaLevel,
            world.Entities,
            generation);
    }
}

/// <summary>Minimal world-side inputs for <see cref="EntityContextSnapshot"/>.</summary>
public readonly record struct WorldEntitySource(
    bool InGame,
    nint AreaInstance,
    uint AreaHash,
    int AreaLevel,
    IReadOnlyList<Poe2Live.EntityDot> Entities);
