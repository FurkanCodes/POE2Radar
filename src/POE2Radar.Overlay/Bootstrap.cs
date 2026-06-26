using POE2Radar.Core;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

/// <summary>
/// Resolves the PoE2 GameState global-pointer slot via the "Game States" AOB pattern, validated
/// by confirming the full chain resolves to a real local player. Returns the slot address (the
/// thing the RIP-relative instruction points at); deref it each tick to get the live GameState.
/// </summary>
internal static class Bootstrap
{
    public sealed record Result(nint Slot, string Detail);

    public static Result TryResolveGameStateSlot(ProcessHandle process, MemoryReader reader)
    {
        if (AobPatterns.GameStateRefs.Length == 0)
            return new(0, "No GameState AOB patterns in this build.");

        foreach (var pattern in AobPatterns.GameStateRefs)
        {
            foreach (var slot in AobScanner.ScanForResolvedAddresses(process, reader, pattern).Distinct())
            {
                var live = new Poe2Live(reader, slot);
                if (live.TryResolve(out _, out _, out _))
                    return new(slot, "GameState chain resolved.");
            }
        }

        return new(0, "Load into a zone (not login or character select), then refresh.");
    }

    public static nint ResolveGameStateSlot(ProcessHandle process, MemoryReader reader)
        => TryResolveGameStateSlot(process, reader).Slot;
}
