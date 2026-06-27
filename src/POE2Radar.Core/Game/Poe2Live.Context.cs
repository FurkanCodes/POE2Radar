using System.Numerics;

namespace POE2Radar.Core.Game;

public partial class Poe2Live
{
    private long _gameContextGeneration;

    /// <summary>Resolve the common game chain once and return an immutable snapshot.</summary>
    public bool TryCaptureGameContext(out GameContextSnapshot snapshot)
    {
        if (!TryResolve(out var inGameState, out var areaInstance, out var localPlayer))
        {
            snapshot = GameContextSnapshot.Invalid;
            return false;
        }

        _gameContextGeneration++;
        var grid = PlayerGrid(localPlayer);
        snapshot = new GameContextSnapshot(
            Valid: true,
            InGameState: inGameState,
            AreaInstance: areaInstance,
            LocalPlayer: localPlayer,
            AreaHash: AreaHash(areaInstance),
            AreaCode: AreaCode(areaInstance),
            AreaLevel: AreaLevel(areaInstance),
            LeagueName: LeagueName(areaInstance),
            PlayerGrid: grid,
            Generation: _gameContextGeneration);
        return true;
    }

    /// <summary>Invalidate UI/entity derived caches when the area instance changes.</summary>
    public void OnAreaInstanceChanged(nint priorArea, nint newArea)
    {
        if (priorArea == 0 || priorArea == newArea) return;
        InvalidateLandmarks();
        _areaCodeFor = -1;
        _areaCode = "";
    }
}
