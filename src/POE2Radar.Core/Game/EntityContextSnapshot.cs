namespace POE2Radar.Core.Game;

/// <summary>
/// Entity lists for feature modules. Component-map walks stay inside <see cref="Poe2Live"/> only —
/// features consume <see cref="EntityContextSnapshot"/> and call Poe2Live helpers, never ResolveComponent directly.
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

    public static EntityContextSnapshot FromGame(GameContextSnapshot game, IReadOnlyList<Poe2Live.EntityDot> entities, long generation)
    {
        if (!game.Valid)
            return Invalid;
        return new EntityContextSnapshot(
            true,
            game.AreaInstance,
            game.AreaHash,
            game.AreaLevel,
            entities,
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
