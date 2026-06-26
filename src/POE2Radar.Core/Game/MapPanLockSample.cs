using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Core.Game;

/// <summary>Fresh player + map pan/zoom read for HUD-locked overlay drawing.</summary>
public readonly record struct MapPanLockSample(
    NumVec2 PlayerGrid,
    float ShiftX,
    float ShiftY,
    float Zoom);
