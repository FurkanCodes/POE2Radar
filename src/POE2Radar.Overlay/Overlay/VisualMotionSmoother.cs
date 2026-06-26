using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;
using NumVec3 = System.Numerics.Vector3;

namespace POE2Radar.Overlay;

public sealed class VisualMotionSmoother
{
    private const float PlayerGridSnapDistance = 35f;
    private const float PlayerWorldSnapDistance = 35f * POE2Radar.Core.Pathfinding.GridConstants.GridToWorld;

    private bool _hasState;
    private VisualFrameState _state = VisualFrameState.Empty;
    private VisualResetKey _key;
    private long _lastStamp;

    public VisualFrameState Update(
        long nowStamp,
        bool enabled,
        int smoothingMs,
        LiveVisualSample sample)
    {
        var key = VisualResetKey.From(sample);
        if (!enabled || smoothingMs <= 0 || !_hasState || !_key.Equals(key) || ShouldSnap(_state, sample))
        {
            _hasState = true;
            _key = key;
            _lastStamp = nowStamp;
            _state = VisualFrameState.From(sample);
            return _state;
        }

        var elapsedMs = Math.Max(0.001f, (float)((nowStamp - _lastStamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
        _lastStamp = nowStamp;
        var alpha = 1f - MathF.Exp(-elapsedMs / Math.Max(1f, smoothingMs));

        _state = new VisualFrameState(
            Lerp(_state.PlayerGrid, sample.PlayerGrid, alpha),
            Lerp(_state.PlayerWorld, sample.PlayerWorld, alpha),
            Lerp(_state.PlayerTerrainHeight, sample.PlayerTerrainHeight, alpha),
            // Map pan must track the game HUD pixel-perfect — smoothing here causes jiggle.
            sample.MapFrame);
        return _state;
    }

    public void Reset()
    {
        _hasState = false;
        _state = VisualFrameState.Empty;
        _key = default;
        _lastStamp = 0;
    }

    private static bool ShouldSnap(VisualFrameState state, LiveVisualSample sample)
    {
        if (NumVec2.Distance(state.PlayerGrid, sample.PlayerGrid) > PlayerGridSnapDistance) return true;
        if (NumVec3.Distance(state.PlayerWorld, sample.PlayerWorld) > PlayerWorldSnapDistance) return true;
        return false;
    }

    private static NumVec2 Lerp(NumVec2 a, NumVec2 b, float t)
        => a + (b - a) * t;

    private static NumVec3 Lerp(NumVec3 a, NumVec3 b, float t)
        => a + (b - a) * t;

    private static float Lerp(float a, float b, float t)
        => a + (b - a) * t;

    private readonly record struct VisualResetKey(
        bool InGame,
        uint AreaHash,
        int WindowWidth,
        int WindowHeight,
        nint MapElement,
        bool MapVisible,
        bool AtlasOpen,
        bool HasCamera)
    {
        public static VisualResetKey From(LiveVisualSample sample)
            => new(
                sample.InGame,
                sample.AreaHash,
                sample.WindowWidth,
                sample.WindowHeight,
                sample.MapFrame.MapElement,
                sample.Map.IsVisible,
                sample.AtlasOpen,
                sample.CameraMatrix is { Length: >= 16 });
    }
}

public readonly record struct LiveVisualSample(
    bool InGame,
    uint AreaHash,
    int WindowWidth,
    int WindowHeight,
    NumVec2 PlayerGrid,
    NumVec3 PlayerWorld,
    float PlayerTerrainHeight,
    Poe2Live.MapUi Map,
    MapFrame MapFrame,
    bool AtlasOpen,
    float[]? CameraMatrix);

public readonly record struct VisualFrameState(
    NumVec2 PlayerGrid,
    NumVec3 PlayerWorld,
    float PlayerTerrainHeight,
    MapFrame MapFrame)
{
    public static readonly VisualFrameState Empty = new(
        NumVec2.Zero,
        NumVec3.Zero,
        0f,
        default);

    public static VisualFrameState From(LiveVisualSample sample)
        => new(
            sample.PlayerGrid,
            sample.PlayerWorld,
            sample.PlayerTerrainHeight,
            sample.MapFrame);
}
