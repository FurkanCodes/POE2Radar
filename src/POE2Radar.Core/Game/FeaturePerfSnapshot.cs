using System.Diagnostics;

namespace POE2Radar.Core.Game;

/// <summary>Per-tick feature prep timings — regression gate for shared-context rollout.</summary>
public readonly record struct FeaturePerfSnapshot(
    float GameContextMs,
    float UiContextMs,
    float RitualMs,
    float LootTagsMs,
    float MapUiMs,
    float AtlasMs,
    float ApiSerializeMs,
    float RenderBuildMs,
    long GameContextTicks,
    long UiContextTicks,
    long RitualTicks,
    long LootTagsTicks)
{
    public static readonly FeaturePerfSnapshot Empty = default;
}

/// <summary>EMA-smoothed feature prep accumulator (main tick thread only).</summary>
public sealed class FeaturePerfAccumulator
{
    private const double Alpha = 0.12;
    private double _gameContextMs;
    private double _uiContextMs;
    private double _ritualMs;
    private double _lootTagsMs;
    private double _mapUiMs;
    private double _atlasMs;
    private double _apiSerializeMs;
    private double _renderBuildMs;
    private long _gameContextTicks;
    private long _uiContextTicks;
    private long _ritualTicks;
    private long _lootTagsTicks;

    public FeaturePerfSnapshot Snapshot => new(
        (float)_gameContextMs,
        (float)_uiContextMs,
        (float)_ritualMs,
        (float)_lootTagsMs,
        (float)_mapUiMs,
        (float)_atlasMs,
        (float)_apiSerializeMs,
        (float)_renderBuildMs,
        _gameContextTicks,
        _uiContextTicks,
        _ritualTicks,
        _lootTagsTicks);

    public void RecordGameContext(double ms) { _gameContextTicks++; _gameContextMs = Smooth(_gameContextMs, ms); }
    public void RecordUiContext(double ms) { _uiContextTicks++; _uiContextMs = Smooth(_uiContextMs, ms); }
    public void RecordRitual(double ms) { _ritualTicks++; _ritualMs = Smooth(_ritualMs, ms); }
    public void RecordLootTags(double ms) { _lootTagsTicks++; _lootTagsMs = Smooth(_lootTagsMs, ms); }
    public void RecordMapUi(double ms) => _mapUiMs = Smooth(_mapUiMs, ms);
    public void RecordAtlas(double ms) => _atlasMs = Smooth(_atlasMs, ms);
    public void RecordApiSerialize(double ms)
    {
        lock (_apiLock)
        {
            _apiSerializeMs = Smooth(_apiSerializeMs, ms);
        }
    }

    private readonly object _apiLock = new();
    public void RecordRenderBuild(double ms) => _renderBuildMs = Smooth(_renderBuildMs, ms);

    public static double ElapsedMs(long start)
        => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static double Smooth(double current, double sample)
        => current <= 0 ? sample : current + Alpha * (sample - current);
}
