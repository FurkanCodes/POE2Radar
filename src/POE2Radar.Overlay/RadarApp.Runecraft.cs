using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Pricing;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private static string RunecraftDir => Path.Combine(AppContext.BaseDirectory, "Runecraft");
    private static string RunecraftRecipePath => Path.Combine(RunecraftDir, "expedition2_recipes.json");

    private bool _runecraftInitialized;
    private string _runecraftPricingConfigKey = "";
    private RunecraftPriceLabel[] _runecraftLabels = [];
    private RunecraftMapLabel[] _runecraftMapLabels = [];
    private RunecraftMonolithPanelRow[] _runecraftMonolithRows = [];
    private DateTime _nextRunecraftRecomputeUtc = DateTime.MinValue;
    private DateTime _nextMonolithScanUtc = DateTime.MinValue;
    private nint _runecraftAreaInstance;
    private bool _wasRunecraftPanelOpen;
    private readonly RunecraftRecipeCatalog _runecraftCatalog = new();
    private readonly List<RunecraftRecipeCatalog.MonolithView> _runecraftMonoliths = new();
    private readonly Dictionary<long, (NumVec2 Pos, NumVec2 Size, int Stale)> _runecraftGeomCache = new();
    private RunecraftRuntimeStatus _runecraftStatus = RunecraftRuntimeStatus.Empty;
    private string _lockedPanelMetaId = "";
    private string _lockedPanelName = "";
    private readonly PerformanceCadence _runecraftTickCadence = new();
    private readonly PerformanceCadence _runecraftHudIdleCadence = new();
    private bool _runecraftMonolithWindowActive;

    private const int RunecraftMonolithOnlyTickHz = 8;
    private const int RunecraftHudIdleScanHz = 6;
    private const int RunecraftMonolithScanMs = 1500;
    private const int RunecraftMonolithScanMsActive = 750;

    private sealed record RunecraftRuntimeStatus(
        bool PanelOpen,
        string Branch,
        int RowCount,
        int LabelCount,
        int MonolithCount,
        string League,
        string Note)
    {
        public static readonly RunecraftRuntimeStatus Empty = new(false, "", 0, 0, 0, "", "");
    }

    private bool RunecraftMonolithWindowActive()
        => _runecraftMonolithWindowActive;

    private void RefreshRunecraftMonolithWindowGate()
    {
        var r = _settings.Runecraft;
        _runecraftMonolithWindowActive = r.ShowMonolithWindow
            || (r.AutoShowMonolithWithGamepad && GamepadInput.IsConnected(_settings.GamepadUserIndex));
    }

    private bool NeedsRunecraftWork(bool drawActive)
    {
        if (_settings.Runecraft.DiagnosePricing) return drawActive;
        if (!drawActive) return false;
        if (_settings.Runecraft.ShowOverlay) return true;
        if (_settings.Runecraft.ShowMapLabels) return true;
        if (_settings.Runecraft.ShowMonolithWindow) return true;
        // Pad auto-open only: entity scans at monolith-only cadence, no HUD overlay reads.
        return RunecraftMonolithWindowActive() && !_settings.Runecraft.ShowMonolithWindow;
    }

    private bool NeedsRunecraftMonolithScans()
        => _settings.Runecraft.ShowMonolithWindow || RunecraftMonolithWindowActive();

    private bool RunecraftMonolithOnlyMode()
        => !_settings.Runecraft.ShowOverlay
           && !_settings.Runecraft.ShowMapLabels
           && NeedsRunecraftMonolithScans();

    private void RefreshRunecraft(LiveFrameState live, WorldSnapshot snap, int windowWidth, int windowHeight, bool drawActive)
    {
        RefreshRunecraftMonolithWindowGate();

        if (!NeedsRunecraftWork(drawActive))
        {
            ClearRunecraftSession(open: false);
            _runecraftMonolithWindowActive = false;
            return;
        }

        if (RunecraftMonolithOnlyMode() && !_runecraftTickCadence.IsDue(RunecraftMonolithOnlyTickHz))
            return;

        if (!live.InGame || windowWidth <= 0 || windowHeight <= 0)
        {
            ClearRunecraftSession(open: false);
            return;
        }

        InitializeRunecraftPricing(live);
        _runecraftCatalog.TryLoad(RunecraftRecipePath);

        if (live.AreaInstance != _runecraftAreaInstance)
        {
            _runecraftAreaInstance = live.AreaInstance;
            _live.InvalidateRunecraftUiCache();
            ClearRunecraftSession(open: false);
        }

        var wantHudOverlay = drawActive && _settings.Runecraft.ShowOverlay;
        var readHudThisTick = wantHudOverlay
            && (_wasRunecraftPanelOpen || _runecraftHudIdleCadence.IsDue(RunecraftHudIdleScanHz));

        var now = DateTime.UtcNow;
        if (NeedsRunecraftMonolithScans())
        {
            var scanIntervalMs = RunecraftMonolithOnlyMode() ? RunecraftMonolithScanMs : RunecraftMonolithScanMsActive;
            if (now >= _nextMonolithScanUtc)
            {
                _nextMonolithScanUtc = now.AddMilliseconds(scanIntervalMs);
                ScanRunecraftMonoliths(live, snap);
                ResolveLockedPanelReward();
            }
        }

        if (wantHudOverlay && !readHudThisTick)
        {
            if (_settings.Runecraft.ShowMapLabels)
                RefreshRunecraftMapLabels(live, _wasRunecraftPanelOpen, windowWidth, windowHeight, drawActive);
            return;
        }

        var panel = readHudThisTick
            ? _live.ReadRuneshapePanel(live.InGameState, windowWidth, windowHeight)
            : Poe2Live.RunecraftPanelRead.Closed;

        if (!panel.IsOpen)
        {
            ClearRunecraftSession(open: false);
            _runecraftStatus = new RunecraftRuntimeStatus(
                false, "", 0, 0, _runecraftMonoliths.Count, EffectiveRunecraftLeague(live), "");
            RefreshRunecraftMapLabels(live, panel.IsOpen, windowWidth, windowHeight, drawActive);
            return;
        }

        _wasRunecraftPanelOpen = true;
        PoeNinjaPriceFetcher.RefreshIfNeeded();

        if (panel.Rows.Length == 0)
        {
            if (_runecraftLabels.Length > 0)
            {
                _runecraftLabels = [];
                _runecraftGeomCache.Clear();
                _nextRunecraftRecomputeUtc = DateTime.MinValue;
                _live.InvalidateRunecraftUiCache();
            }

            _runecraftStatus = new RunecraftRuntimeStatus(
                true,
                panel.Branch.ToString(),
                0,
                0,
                _runecraftMonoliths.Count,
                EffectiveRunecraftLeague(live),
                "");
            RefreshRunecraftMapLabels(live, true, windowWidth, windowHeight, drawActive);
            return;
        }

        var intervalMs = _imguiOverlay?.IsSettingsOpen == true ? 200 : 120;
        if (now < _nextRunecraftRecomputeUtc && _runecraftLabels.Length > 0 && panel.Rows.Length > 0)
        {
            _runecraftStatus = _runecraftStatus with
            {
                PanelOpen = true,
                Branch = panel.Branch.ToString(),
                RowCount = panel.Rows.Length,
                LabelCount = _runecraftLabels.Length,
                MonolithCount = _runecraftMonoliths.Count,
                League = EffectiveRunecraftLeague(live),
            };
            RefreshRunecraftMapLabels(live, true, windowWidth, windowHeight, drawActive);
            return;
        }

        _nextRunecraftRecomputeUtc = now.AddMilliseconds(intervalMs);
        _runecraftLabels = BuildRunecraftLabels(panel);
        _runecraftStatus = new RunecraftRuntimeStatus(
            true,
            panel.Branch.ToString(),
            panel.Rows.Length,
            _runecraftLabels.Length,
            _runecraftMonoliths.Count,
            EffectiveRunecraftLeague(live),
            "");

        RefreshRunecraftMapLabels(live, true, windowWidth, windowHeight, drawActive);
    }

    private void RefreshRunecraftMapLabels(LiveFrameState live, bool panelOpen, int windowWidth, int windowHeight, bool drawActive)
    {
        if (!drawActive || !_settings.Runecraft.ShowMapLabels || !live.Maps.LargeMap.IsVisible)
        {
            if (_runecraftMapLabels.Length > 0) _runecraftMapLabels = [];
            return;
        }

        if (_settings.Runecraft.HideMapValueWhenPanelOpen && panelOpen && _settings.Runecraft.ShowOverlay)
        {
            if (_runecraftMapLabels.Length > 0) _runecraftMapLabels = [];
            return;
        }

        var minEx = Math.Max(0f, _settings.Runecraft.MapLabelMinExalted);
        var labels = new List<RunecraftMapLabel>(_runecraftMonoliths.Count);
        var large = live.Maps.LargeMap;
        var player = live.PlayerGrid;

        foreach (var m in _runecraftMonoliths)
        {
            if (m.Best < minEx) continue;
            var screen = ProjectMonolithMapLabel(m.Grid, m.TerrainHeight, player, live.PlayerTerrainHeight, large, windowWidth, windowHeight);
            if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y)) continue;
            var color = PickMonolithMapColor(m.Best);
            labels.Add(new RunecraftMapLabel(screen, $"{m.Best:F0} ex", color));
        }

        _runecraftMapLabels = labels.Count == 0 ? [] : labels.ToArray();
    }

    private NumVec2 ProjectMonolithMapLabel(
        NumVec2 grid,
        float terrainHeight,
        NumVec2 playerGrid,
        float playerHeight,
        Poe2Live.MapUi largeMap,
        int windowWidth,
        int windowHeight)
    {
        if (!largeMap.HasScreenRect) return new NumVec2(float.NaN, float.NaN);

        var scaleMul = Math.Max(0.1f, _settings.Runecraft.MapValueScaleMultiplier);
        var scale = scaleMul * largeMap.Zoom * 0.187812f;
        if (scale <= 0) return new NumVec2(float.NaN, float.NaN);

        const double angle = 38.7 * Math.PI / 180.0;
        var baseDiag = Math.Sqrt(Poe2.UiElement.BaseResW * Poe2.UiElement.BaseResW + Poe2.UiElement.BaseResH * Poe2.UiElement.BaseResH);
        var diag = baseDiag * largeMap.Height / Poe2.UiElement.BaseResH;
        if (diag <= 0) return new NumVec2(float.NaN, float.NaN);

        float mapScale = 240f / scale;
        float cos = (float)(diag * Math.Cos(angle) / mapScale);
        float sin = (float)(diag * Math.Sin(angle) / mapScale);

        var center = new NumVec2(
            largeMap.CenterX + largeMap.ShiftX + largeMap.DefaultShiftX + 0.6f + _settings.Runecraft.MapValueXOffset,
            largeMap.CenterY + largeMap.ShiftY + largeMap.DefaultShiftY + 0.3f + _settings.Runecraft.MapValueYOffset);

        var delta = grid - playerGrid;
        float deltaZ = (terrainHeight - playerHeight) / 10.86957f;
        var fpos = new NumVec2((delta.X - delta.Y) * cos, (deltaZ - (delta.X + delta.Y)) * sin);
        return center + fpos;
    }

    private uint PickMonolithMapColor(double bestEx)
    {
        var threshold = _settings.Runecraft.MonolithHighlightThreshold;
        if (threshold <= 0)
            return RunecraftPriceMath.PickColor(bestEx, 0, (RunecraftColorMode)Math.Clamp(_settings.Runecraft.ColorMode, 0, 2));
        if (bestEx >= threshold) return 0xFF55FF55u;
        if (bestEx >= threshold * 0.6) return 0xFF55FFFFu;
        return 0xFFFFFFFFu;
    }

    private void ScanRunecraftMonoliths(LiveFrameState live, WorldSnapshot snap)
    {
        _runecraftMonoliths.Clear();
        if (!_runecraftCatalog.IsLoaded) return;

        var player = live.PlayerGrid;
        foreach (var e in snap.Entities)
        {
            if (e.Metadata.IndexOf("Expedition2Encounter", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!_live.TryReadRunecraftMonolithStation(e.Address, out var st))
                continue;

            var view = new RunecraftRecipeCatalog.MonolithView
            {
                DeviceAddress = e.Address,
                Grid = e.Grid,
                TerrainHeight = e.TerrainHeight,
                Distance = NumVec2.Distance(player, e.Grid),
                HoleCount = st.HoleCount,
                AnchorIdx = st.AnchorIndex,
                AnchorPos = st.AnchorPos,
                AnchorName = _runecraftCatalog.RuneName(st.AnchorIndex),
                IsUnique = st.IsUnique,
                IsRerolled = st.IsRerolled,
                PanelOpen = st.PanelOpen,
                SelectedRecipeId = st.SelectedRecipeId,
            };

            _runecraftCatalog.BuildCandidates(view, snap.AreaLevel, RecipeUnitPrice);
            _runecraftMonoliths.Add(view);
        }

        _runecraftMonoliths.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        _runecraftMonolithRows = BuildMonolithPanelRows(_runecraftMonoliths);
    }

    private RunecraftMonolithPanelRow[] BuildMonolithPanelRows(List<RunecraftRecipeCatalog.MonolithView> monoliths)
    {
        if (monoliths.Count == 0) return [];

        var s = _settings.Runecraft;
        var colorMode = (RunecraftColorMode)Math.Clamp(s.ColorMode, 0, 2);
        var minEx = Math.Max(0f, s.MonolithRewardsMinExalted);
        var pricedTotals = new List<double>();
        foreach (var v in monoliths)
        {
            foreach (var c in v.Candidates)
            {
                if (!c.Priced) continue;
                pricedTotals.Add(c.UnitEx * c.Count);
            }
        }

        var median = colorMode == RunecraftColorMode.Relative
            ? RunecraftPriceMath.MedianOf(pricedTotals.ToArray())
            : 0;

        var rows = new RunecraftMonolithPanelRow[monoliths.Count];
        for (var i = 0; i < monoliths.Count; i++)
            rows[i] = BuildMonolithPanelRow(monoliths[i], colorMode, median, minEx);
        return rows;
    }

    private RunecraftMonolithPanelRow BuildMonolithPanelRow(
        RunecraftRecipeCatalog.MonolithView v,
        RunecraftColorMode colorMode,
        double median,
        float minEx)
    {
        string hdr;
        if (v.IsRerolled && v.Candidates.Count > 0)
            hdr = $"{(v.PanelOpen ? "▶ " : "")}[locked] {v.Candidates[0].Reward} · {v.Distance:F0}";
        else if (v.IsUnique)
            hdr = $"{(v.PanelOpen ? "▶ " : "")}Unique · {v.HoleCount} holes · {v.Distance:F0}";
        else if (v.AnchorIdx >= 0)
            hdr = $"{(v.PanelOpen ? "▶ " : "")}{v.AnchorName} · hole {v.AnchorPos + 1}/{v.HoleCount} · {v.Distance:F0}";
        else
            hdr = $"{(v.PanelOpen ? "▶ " : "")}(anchor ?) · {v.HoleCount} holes · {v.Distance:F0}";

        var showAnchorWarning = v.AnchorIdx < 0 && !v.IsUnique && !(v.IsRerolled && v.Candidates.Count > 0);
        var headerColor = colorMode == RunecraftColorMode.Off
            ? 0xFFFFFFFFu
            : RunecraftPriceMath.PickColor(v.Best, median, colorMode);

        var candidates = new List<RunecraftMonolithCandidate>(v.Candidates.Count);
        foreach (var c in v.Candidates)
        {
            var total = c.UnitEx * c.Count;
            if (c.Priced && total < minEx) continue;
            if (!c.Priced && minEx > 0f) continue;

            var totalColor = colorMode == RunecraftColorMode.Off || !c.Priced
                ? 0xFFFFFFFFu
                : RunecraftPriceMath.PickColor(total, median, colorMode);

            var runes = c.Runes.Length > 0 ? $"[{c.Size}] {c.Runes}" : $"[{c.Size} sockets]";
            candidates.Add(new RunecraftMonolithCandidate(
                c.Reward,
                c.Count,
                c.UnitEx,
                total,
                c.Priced,
                runes,
                c.Size,
                totalColor));
        }

        return new RunecraftMonolithPanelRow(
            (long)v.DeviceAddress,
            hdr,
            v.Best,
            headerColor,
            showAnchorWarning,
            v.PanelOpen,
            candidates.ToArray());
    }

    private double RecipeUnitPrice(RunecraftRecipeCatalog.RecipeRow rec)
    {
        var metaId = RunecraftPriceMath.LastMetaSegment(rec.reward?.id ?? "");
        var english = rec.reward?.name ?? "";
        RunecraftPriceMath.TryGetUnitPriceExalted(metaId, "", english, english, out var ex);
        return ex;
    }

    private void ResolveLockedPanelReward()
    {
        _lockedPanelMetaId = "";
        _lockedPanelName = "";
        if (!_settings.Runecraft.HighlightLockedRecipe) return;

        foreach (var v in _runecraftMonoliths)
        {
            if (!v.PanelOpen || !v.IsRerolled || string.IsNullOrEmpty(v.SelectedRecipeId)) continue;
            foreach (var c in v.Candidates)
            {
                if (!string.IsNullOrEmpty(c.MetaId))
                {
                    _lockedPanelMetaId = c.MetaId;
                    _lockedPanelName = c.Reward;
                    return;
                }
            }
        }
    }

    private RunecraftPriceLabel[] BuildRunecraftLabels(Poe2Live.RunecraftPanelRead panel)
    {
        if (panel.Rows.Length == 0) return [];

        var s = _settings.Runecraft;
        var colorMode = (RunecraftColorMode)Math.Clamp(s.ColorMode, 0, 2);
        var pricedRows = new List<(Poe2Live.RunecraftRecipeRow Row, double Total, bool Locked)>(panel.Rows.Length);

        foreach (var row in panel.Rows)
        {
            var english = _runecraftCatalog.EnglishNameForMetaId(row.MetaId);
            if (!RunecraftPriceMath.TryGetUnitPriceExalted(
                    row.MetaId, row.DdsArtId, row.Name, english, out var unit))
                continue;

            var total = unit * Math.Max(1, row.Count);
            bool locked =
                (s.HighlightLockedRecipe && _lockedPanelMetaId.Length > 0 &&
                 string.Equals(row.MetaId, _lockedPanelMetaId, StringComparison.Ordinal)) ||
                (s.HighlightLockedRecipe && _lockedPanelName.Length > 0 &&
                 string.Equals(row.Name, _lockedPanelName, StringComparison.Ordinal));
            pricedRows.Add((row, total, locked));
        }

        if (pricedRows.Count == 0) return [];

        var median = colorMode == RunecraftColorMode.Relative
            ? RunecraftPriceMath.MedianOf(pricedRows.Select(p => p.Total).ToArray())
            : 0;

        var result = new List<RunecraftPriceLabel>(pricedRows.Count);
        foreach (var (row, total, locked) in pricedRows)
        {
            if (!TryResolveRunecraftGeom(row, panel, out var pos, out var size))
                continue;

            if (panel.ViewportAddress != 0 && panel.ViewportRect.H > 1f)
            {
                float centreY = pos.Y + size.Y * 0.5f;
                if (centreY < panel.ViewportRect.Y || centreY > panel.ViewportRect.Y + panel.ViewportRect.H)
                    continue;
            }

            var text = RunecraftPriceMath.FormatExalted(total);
            var color = RunecraftPriceMath.PickColor(total, median, colorMode);
            float fontPx = Math.Clamp(size.Y * 0.5f, 12f, 40f);
            float gap = Math.Clamp(size.Y * 0.22f, 12f, 28f);
            float contentRight = row.ContentRightX > 0f
                ? row.ContentRightX
                : pos.X + size.X;
            float priceLeftX = contentRight + gap;

            result.Add(new RunecraftPriceLabel(
                pos,
                size,
                text,
                color,
                fontPx,
                locked,
                panel.ViewportRect.Y,
                panel.ViewportRect.Y + panel.ViewportRect.H,
                panel.ViewportRect.H > 1f,
                priceLeftX,
                s.OverlayXOffset));
        }

        return result.Count == 0 ? [] : result.ToArray();
    }

    private bool TryResolveRunecraftGeom(
        Poe2Live.RunecraftRecipeRow row,
        Poe2Live.RunecraftPanelRead panel,
        out NumVec2 pos,
        out NumVec2 size)
    {
        pos = new NumVec2(row.Rect.X, row.Rect.Y);
        size = new NumVec2(row.Rect.W, row.Rect.H);
        if (row.Rect.W > 1f && row.Rect.H > 1f)
        {
            _runecraftGeomCache[(long)row.RowAddress] = (pos, size, 0);
            return true;
        }

        long key = row.RowAddress;
        if (_runecraftGeomCache.TryGetValue(key, out var cached) && cached.Stale < 6)
        {
            pos = cached.Pos;
            size = cached.Size;
            _runecraftGeomCache[key] = (pos, size, cached.Stale + 1);
            return true;
        }

        _runecraftGeomCache.Remove(key);
        return false;
    }

    private void InitializeRunecraftPricing(LiveFrameState live)
    {
        Directory.CreateDirectory(RunecraftDir);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "RitualHelper"));

        var source = Math.Clamp(_settings.Runecraft.PriceSource, 0, 1);
        var league = EffectiveRunecraftLeague(live);
        var refresh = Math.Max(1, _settings.Runecraft.RefreshIntervalMin);
        var key = $"{source}|{league}|{refresh}";

        LeagueProvider.EnsureLoaded();
        var ritualDir = Path.Combine(AppContext.BaseDirectory, "RitualHelper");
        PoeNinjaPriceFetcher.Configure(source, league, refresh);

        if (!_runecraftInitialized)
        {
            RitualCurrencyIcons.Initialize(ritualDir);
            PoeNinjaPriceFetcher.Initialize(ritualDir);
            _runecraftInitialized = true;
            _runecraftPricingConfigKey = key;
            return;
        }

        if (!string.Equals(_runecraftPricingConfigKey, key, StringComparison.Ordinal))
        {
            _runecraftPricingConfigKey = key;
            PoeNinjaPriceFetcher.ForceRefresh(ritualDir, ignoreCooldown: true);
        }
    }

    private string EffectiveRunecraftLeague(LiveFrameState live)
    {
        var configured = _settings.Runecraft.League?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured, "Auto", StringComparison.OrdinalIgnoreCase))
            return configured!;

        if (live.AreaInstance != 0)
        {
            var liveLeague = _live.LeagueName(live.AreaInstance);
            if (!string.IsNullOrWhiteSpace(liveLeague))
                return liveLeague.Trim();
        }

        return "Runes of Aldur";
    }

    private void ClearRunecraftSession(bool open)
    {
        if (!open && _wasRunecraftPanelOpen)
        {
            _nextRunecraftRecomputeUtc = DateTime.MinValue;
            _runecraftGeomCache.Clear();
            _live.InvalidateRunecraftUiCache();
        }

        _wasRunecraftPanelOpen = open;
        if (_runecraftLabels.Length > 0) _runecraftLabels = [];
    }

    private object RunecraftApiJson() => new
    {
        settings = _settings.Runecraft,
        status = _runecraftStatus,
        source = _settings.Runecraft.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja ? "poe.ninja" : "poe2scout",
        currentLeague = _runecraftStatus.League.Length > 0 ? _runecraftStatus.League : _settings.Runecraft.League,
        loadedPriceCount = PoeNinjaPriceFetcher.LoadedItemCount,
        isFetching = PoeNinjaPriceFetcher.IsFetching,
        lastFetchUtc = PoeNinjaPriceFetcher.LastFetchUtc == DateTime.MinValue ? (DateTime?)null : PoeNinjaPriceFetcher.LastFetchUtc,
        leagues = LeagueProvider.Leagues.ToArray(),
        monoliths = _runecraftMonoliths.Count,
        recipesLoaded = _runecraftCatalog.IsLoaded,
    };

    private void ApplyRunecraftApi(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("settings", out var nested) && nested.ValueKind == JsonValueKind.Object)
                root = nested;

            var r = _settings.Runecraft;
            if (TryGetBool(root, "showOverlay", out var show)) r.ShowOverlay = show;
            if (TryGetBool(root, "showMapLabels", out var map)) r.ShowMapLabels = map;
            if (TryGetBool(root, "hideMapValueWhenPanelOpen", out var hideMap)) r.HideMapValueWhenPanelOpen = hideMap;
            if (TryGetBool(root, "highlightLockedRecipe", out var hl)) r.HighlightLockedRecipe = hl;
            if (TryGetBool(root, "showMonolithWindow", out var win)) r.ShowMonolithWindow = win;
            if (TryGetBool(root, "autoShowMonolithWithGamepad", out var autoWin)) r.AutoShowMonolithWithGamepad = autoWin;
            if (TryGetBool(root, "diagnosePricing", out var diag)) r.DiagnosePricing = diag;
            if (TryGetInt(root, "priceSource", out var source)) r.PriceSource = Math.Clamp(source, 0, 1);
            if (TryGetInt(root, "refreshIntervalMin", out var refresh)) r.RefreshIntervalMin = Math.Max(1, refresh);
            if (TryGetInt(root, "colorMode", out var color)) r.ColorMode = Math.Clamp(color, 0, 2);
            if (TryGetFloat(root, "overlayXOffset", out var ox)) r.OverlayXOffset = Math.Clamp(ox, -400f, 400f);
            if (TryGetFloat(root, "mapLabelMinExalted", out var minMap)) r.MapLabelMinExalted = Math.Clamp(minMap, 0f, 1000f);
            if (TryGetFloat(root, "monolithHighlightThreshold", out var thr)) r.MonolithHighlightThreshold = Math.Max(0f, thr);
            if (TryGetFloat(root, "mapValueScaleMultiplier", out var scale)) r.MapValueScaleMultiplier = Math.Clamp(scale, 0.1f, 3f);
            if (TryGetFloat(root, "mapValueXOffset", out var mx)) r.MapValueXOffset = Math.Clamp(mx, -200f, 200f);
            if (TryGetFloat(root, "mapValueYOffset", out var my)) r.MapValueYOffset = Math.Clamp(my, -200f, 200f);
            if (TryGetFloat(root, "monolithRewardsMinExalted", out var minMono)) r.MonolithRewardsMinExalted = Math.Max(0f, minMono);
            if (TryGetString(root, "league", out var league)) r.League = string.IsNullOrWhiteSpace(league) ? "Runes of Aldur" : league.Trim();

            _runecraftPricingConfigKey = "";
            _settings.Save();
        }
        catch (JsonException) { }
    }
}
