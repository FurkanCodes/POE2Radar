namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    /// <summary>One map viewport (large Tab map or corner minimap): projection params + screen clip rect.</summary>
    public readonly record struct MapViewport(
        bool Visible, float ShiftX, float ShiftY, float Zoom,
        float ScreenLeft, float ScreenTop, float ScreenRight, float ScreenBottom)
    {
        public bool HasScreenRect => ScreenRight > ScreenLeft + 1f && ScreenBottom > ScreenTop + 1f;
        public float ScreenWidth => ScreenRight - ScreenLeft;
        public float ScreenHeight => ScreenBottom - ScreenTop;
    }

    /// <summary>Large map + corner minimap UI state, discovered once per area.</summary>
    public readonly record struct MapState(MapViewport Large, MapViewport Mini)
    {
        public static readonly MapState Empty = new(default, default);
        public bool IsLargeOpen => Large.Visible;
    }


    public MapState ReadMapState(nint inGameState, nint areaInstance, int windowWidth, int windowHeight)
    {
        if (areaInstance != _mapCacheKey || _mapEls.Count == 0)
        {
            _mapCacheKey = areaInstance;
            _mapEls.Clear();
            _everHidden.Clear();
            _everVisible.Clear();
            _classifiedLargeEl = _classifiedMiniEl = 0;
            DiscoverMapElements(inGameState, windowWidth, windowHeight);
        }

        ClassifyMapPair();

        var uiScale = windowHeight > 0 ? windowHeight / 1600f : 1f;

        // ── Tab large map: corner element local-vis (live-validated toggle signal).
        MapViewport large = default;
        if (_classifiedMiniEl != 0
            && TryReadMapElement(_classifiedMiniEl, out var cornerLocal, out _, out var csx, out var csy, out var czoom))
        {
            var tabOpen = MapViewportLogic.IsTabMapOpen(cornerLocal);
            if (!tabOpen && _classifiedLargeEl != 0
                && TryReadMapElement(_classifiedLargeEl, out var fullLocal, out _, out var fsx, out var fsy, out var fzoom))
            {
                large = new MapViewport(false, fsx, fsy, fzoom, 0, 0, 0, 0);
            }
            else
                large = new MapViewport(tabOpen, csx, csy, czoom, 0, 0, 0, 0);
        }
        else
            large = FallbackLargeMapViewport();

        // ── Corner minimap: only while Tab map is closed; clip rect from the square frame sibling
        // (live --map-scan-frames), not the 0×0 MapUiElement's parent-chain rect.
        MapViewport mini = default;
        if (!large.Visible && _classifiedMiniEl != 0
            && TryReadActiveMapProjection(_classifiedMiniEl, _classifiedLargeEl, out var msx, out var msy, out var mzoom))
        {
            float cl, ct, cr, cb;
            if (!TryReadMinimapFrameRect(
                    _classifiedMiniEl, uiScale, windowWidth, windowHeight, out _, out cl, out ct, out cr, out cb))
            {
                var rect = ReadElementScreenRect(_classifiedMiniEl, uiScale, windowWidth, windowHeight);
                (cl, ct, cr, cb) = MapViewportLogic.ResolveMinimapClipRect(
                    rect.left, rect.top, rect.right, rect.bottom, windowWidth, windowHeight, uiScale);
            }

            mini = new MapViewport(true, msx, msy, mzoom, cl, ct, cr, cb);

            if (mini.Zoom > 0.05f)
                mini = mini with { Visible = true };
        }

        return new MapState(large, mini);
    }

    /// <summary>Assign Tab-map vs corner-minimap from intrinsic UiElement size (MapParent is not valid in PoE2).</summary>
    private void ClassifyMapPair()
    {
        if (_classifiedLargeEl != 0 && _classifiedMiniEl != 0) return;
        if (_mapEls.Count < 2) return;

        nint bestLarge = 0, bestMini = 0;
        var bestArea = -1f;
        foreach (var el in _mapEls)
        {
            var (w, h) = ReadElementIntrinsicArea(el);
            var area = w * h;
            if (area > bestArea) { bestArea = area; bestLarge = el; }
        }

        foreach (var el in _mapEls)
        {
            if (el == bestLarge) continue;
            bestMini = el;
            break;
        }

        if (bestLarge != 0 && bestMini != 0)
        {
            _classifiedLargeEl = bestLarge;
            _classifiedMiniEl = bestMini;
        }
    }

    private (float w, float h) ReadElementIntrinsicArea(nint el)
    {
        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeW, out var w);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeH, out var h);
        if (w <= 0f || h <= 0f) { w = 250f; h = 250f; }
        return (w, h);
    }

    /// <summary>v0.8.2 toggler fallback when the two-element classification is not ready yet.</summary>
    private MapViewport FallbackLargeMapViewport()
    {
        var visibleCount = 0;
        var any = false;
        MapViewport anyVp = default;
        var sawToggler = false;
        var togglerVisible = false;
        var haveTogglerUi = false;
        MapViewport togglerVp = default;

        foreach (var el in _mapEls)
        {
            if (!TryReadMapElement(el, out var localVis, out _, out var sx, out var sy, out var zoom)) continue;
            if (localVis) { _everVisible.Add(el); visibleCount++; } else _everHidden.Add(el);

            var vp = new MapViewport(localVis, sx, sy, zoom, 0, 0, 0, 0);
            if (!any) { any = true; anyVp = vp; }

            if (_everVisible.Contains(el) && _everHidden.Contains(el))
            {
                sawToggler = true;
                if (el == _classifiedMiniEl && localVis) togglerVisible = true;
                else if (_classifiedMiniEl == 0 && localVis) togglerVisible = true;
                if (localVis || !haveTogglerUi) { togglerVp = vp; haveTogglerUi = true; }
            }
        }

        if (!any) return default;
        if (sawToggler) return togglerVp with { Visible = togglerVisible };
        return anyVp with { Visible = visibleCount >= 2 };
    }

    /// <summary>GH2 MapParent (+0x738) — not valid in PoE2 live (both ptrs identical). Research only.</summary>
    private bool TryMapParentElements(nint inGameState, out nint largeEl, out nint miniEl)
    {
        largeEl = miniEl = 0;
        var uiRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        if (uiRoot == 0) return false;

        var trueRoot = Ptr(uiRoot + Poe2.UiElement.Parent);
        if (trueRoot == 0 || trueRoot == uiRoot) trueRoot = uiRoot;
        var uiRootStruct = Ptr(inGameState + Poe2.InGameState.UiRootStructPtr);

        nint[] anchors = [uiRoot, trueRoot, uiRootStruct];
        foreach (var anchor in anchors)
        {
            if (anchor == 0) continue;
            var mapParent = Ptr(anchor + Poe2.ImportantUi.MapParentPtr);
            if (mapParent == 0) continue;
            var lg = Ptr(mapParent + Poe2.MapParent.LargeMapPtr);
            var mn = Ptr(mapParent + Poe2.MapParent.MiniMapPtr);
            if (lg == 0 || mn == 0 || lg == mn) continue;
            largeEl = lg;
            miniEl = mn;
            return true;
        }
        return false;
    }

    private bool TryBuildMapViewport(nint el, float uiScale, int windowWidth, int windowHeight, out MapViewport vp)
    {
        vp = default;
        if (!TryReadMapElement(el, out var localVis, out var hierVis, out var sx, out var sy, out var zoom)) return false;
        var rect = ReadElementScreenRect(el, uiScale, windowWidth, windowHeight);
        vp = new MapViewport(hierVis, sx, sy, zoom, rect.left, rect.top, rect.right, rect.bottom);
        return zoom > 0.05f;
    }

    private readonly record struct ScreenRect(float left, float top, float right, float bottom)
    {
        public bool HasArea => MapViewportLogic.HasArea(left, top, right, bottom);
    }

    /// <summary>
    /// While the corner minimap is shown, the fullscreen MapUiElement is locally visible and carries
    /// the live pan/zoom; the 0×0 corner MapUiElement does not.
    /// </summary>
    private bool TryReadActiveMapProjection(nint miniEl, nint largeEl, out float shiftX, out float shiftY, out float zoom)
    {
        if (largeEl != 0
            && TryReadMapElement(largeEl, out var largeLocal, out _, out shiftX, out shiftY, out zoom)
            && largeLocal)
            return true;
        return TryReadMapElement(miniEl, out _, out _, out shiftX, out shiftY, out zoom);
    }

    /// <summary>
    /// The visible minimap border is a square UiElement sibling under the corner MapUiElement's parent
    /// (Research --map-scan-frames: parent.children → 402×402 @ top-right).
    /// </summary>
    private bool TryReadMinimapFrameRect(
        nint miniMapEl, float uiScale, int windowWidth, int windowHeight,
        out nint frameEl, out float left, out float top, out float right, out float bottom)
    {
        frameEl = 0;
        left = top = right = bottom = 0;
        var parent = Ptr(miniMapEl + Poe2.UiElement.Parent);
        if (parent == 0) return false;

        var first = Ptr(parent + Poe2.UiElement.Children);
        if (first == 0 || !_reader.TryReadStruct<nint>(parent + Poe2.UiElement.Children + 8, out var lastC))
            return false;
        var n = ((long)lastC - (long)first) / 8;
        if (n is <= 0 or > 256) return false;

        var candidates = new List<MapViewportLogic.MinimapFrameCandidate>((int)n);
        var children = new List<nint>((int)n);
        for (long k = 0; k < n; k++)
        {
            var child = Ptr(first + (nint)(k * 8));
            if (child == 0 || child == miniMapEl) continue;
            children.Add(child);
            _reader.TryReadStruct<float>(child + Poe2.UiElement.SizeW, out var w);
            _reader.TryReadStruct<float>(child + Poe2.UiElement.SizeH, out var h);
            var rect = ReadElementScreenRect(child, uiScale, windowWidth, windowHeight);
            candidates.Add(new MapViewportLogic.MinimapFrameCandidate(
                w, h, rect.left, rect.top, rect.right, rect.bottom,
                IsVisible(child)));
        }

        var bestIdx = -1;
        var bestArea = 0f;
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.Width <= 0f || c.Height <= 0f) continue;
            var aspect = c.Width / c.Height;
            if (aspect is < 0.85f or > 1.15f) continue;
            if (!c.Visible) continue;
            if (!MapViewportLogic.IsTopRightMinimapRect(c.Left, c.Top, c.Right, c.Bottom, windowWidth, windowHeight)) continue;
            var area = (c.Right - c.Left) * (c.Bottom - c.Top);
            if (bestIdx >= 0 && area <= bestArea) continue;
            bestIdx = i;
            bestArea = area;
            left = c.Left;
            top = c.Top;
            right = c.Right;
            bottom = c.Bottom;
        }

        if (bestIdx < 0) return false;
        frameEl = children[bestIdx];
        return true;
    }

    /// <summary>Walk the UiElement parent chain summing RelativePos and apply Size × UI scale for clip bounds.</summary>
    private ScreenRect ReadElementScreenRect(nint el, float uiScale, int windowWidth, int windowHeight)
    {
        float x = 0f, y = 0f;
        var cur = el;
        for (var depth = 0; depth < 24 && cur != 0; depth++)
        {
            if (_reader.TryReadStruct<float>(cur + Poe2.UiElement.RelativePos, out var rx))
                x += rx;
            if (_reader.TryReadStruct<float>(cur + Poe2.UiElement.RelativePos + 4, out var ry))
                y += ry;
            if (!_reader.TryReadStruct<nint>(cur + Poe2.UiElement.Parent, out var par) || par == 0 || par == cur) break;
            cur = par;
        }

        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeW, out var w);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeH, out var h);
        var (left, top, right, bottom) = MapViewportLogic.ClampScreenRect(x, y, w, h, uiScale, windowWidth, windowHeight);
        return new ScreenRect(left, top, right, bottom);
    }

    private void DiscoverMapElements(nint inGameState, int windowWidth, int windowHeight)
    {
        _mapEls.Clear();
        _classifiedLargeEl = _classifiedMiniEl = 0;

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        Span<nint> anchors = stackalloc nint[] { controllerGameUi, gameUi, uiRoot };

        var uiScale = windowHeight > 0 ? windowHeight / 1600f : 1f;
        var scratch = new List<nint>();
        var bestScore = -1;

        for (var i = 0; i < anchors.Length; i++)
        {
            var anchor = anchors[i];
            if (anchor == 0) continue;
            var duplicate = false;
            for (var j = 0; j < i; j++)
            {
                if (anchors[j] != anchor) continue;
                duplicate = true;
                break;
            }
            if (duplicate) continue;

            CollectMapElementsFromRoot(anchor, scratch);
            if (scratch.Count < 2) continue;
            if (!TryClassifyMapPairFromEls(scratch, out var largeEl, out var miniEl)) continue;

            var score = ScoreDiscoveredPair(largeEl, miniEl, uiScale, windowWidth, windowHeight);
            if (score <= bestScore) continue;

            bestScore = score;
            _mapEls.Clear();
            _mapEls.AddRange(scratch);
            _classifiedLargeEl = largeEl;
            _classifiedMiniEl = miniEl;
        }
    }

    private nint ResolveBfsRoot(nint anchor)
    {
        if (anchor == 0) return 0;
        var parent = Ptr(anchor + Poe2.UiElement.Parent);
        return parent != 0 && parent != anchor ? parent : anchor;
    }

    private void CollectMapElementsFromRoot(nint anchor, List<nint> results)
    {
        results.Clear();
        var root = ResolveBfsRoot(anchor);
        if (root == 0) return;

        var queue = new Queue<nint>();
        queue.Enqueue(root);
        var visited = new HashSet<nint>();
        var body = new byte[Poe2.MapUiElement.Zoom + 8];
        while (queue.Count > 0 && visited.Count < 30000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            var first = Ptr(el + Poe2.UiElement.Children);
            if (first != 0 && _reader.TryReadStruct<nint>(el + Poe2.UiElement.Children + 8, out var lastC))
            {
                var n = ((long)lastC - (long)first) / 8;
                if (n is > 0 and <= 8192)
                    for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
            }

            if (_reader.TryReadBytes(el, body) < body.Length) continue;
            if (BitConverter.ToSingle(body, Poe2.MapUiElement.DefaultShift) != 0f) continue;
            if (BitConverter.ToSingle(body, Poe2.MapUiElement.DefaultShift + 4) != -20f) continue;
            var zoom = BitConverter.ToSingle(body, Poe2.MapUiElement.Zoom);
            if (zoom is <= 0.05f or >= 8f) continue;
            results.Add(el);
        }
    }

    private bool TryClassifyMapPairFromEls(List<nint> els, out nint largeEl, out nint miniEl)
    {
        largeEl = miniEl = 0;
        if (els.Count < 2) return false;

        var bestArea = -1f;
        foreach (var el in els)
        {
            var (w, h) = ReadElementIntrinsicArea(el);
            var area = w * h;
            if (area > bestArea) { bestArea = area; largeEl = el; }
        }

        foreach (var el in els)
        {
            if (el == largeEl) continue;
            miniEl = el;
            break;
        }

        return largeEl != 0 && miniEl != 0;
    }

    /// <summary>Pick the UI tree whose classified large/mini pair is actually visible on-screen.</summary>
    private int ScoreDiscoveredPair(nint largeEl, nint miniEl, float uiScale, int windowWidth, int windowHeight)
    {
        var score = 0;
        if (TryReadMapElement(miniEl, out var miniLocal, out var miniHier, out _, out _, out var mzoom))
        {
            if (miniHier) score += 4;
            else if (miniLocal) score += 2;
            if (mzoom > 0.05f) score += 1;
        }

        if (windowWidth > 0 && windowHeight > 0)
        {
            if (TryReadMinimapFrameRect(miniEl, uiScale, windowWidth, windowHeight, out _, out _, out _, out _, out _))
                score += 3;
            else
            {
                var rect = ReadElementScreenRect(miniEl, uiScale, windowWidth, windowHeight);
                if (rect.HasArea) score += 1;
            }
        }

        if (TryReadMapElement(largeEl, out var largeLocal, out var largeHier, out _, out _, out _))
        {
            if (largeHier) score += 2;
            else if (largeLocal) score += 1;
        }

        return score;
    }

    private bool TryReadMapElement(
        nint el, out bool localVisible, out bool hierarchicallyVisible,
        out float shiftX, out float shiftY, out float zoom)
    {
        localVisible = hierarchicallyVisible = false;
        shiftX = shiftY = zoom = 0;
        if (!_reader.TryReadStruct<float>(el + Poe2.MapUiElement.DefaultShift + 4, out var dsy) || dsy != -20f) return false;
        _reader.TryReadStruct<float>(el + Poe2.MapUiElement.Shift, out shiftX);
        _reader.TryReadStruct<float>(el + Poe2.MapUiElement.Shift + 4, out shiftY);
        _reader.TryReadStruct<float>(el + Poe2.MapUiElement.Zoom, out zoom);
        localVisible = IsLocallyVisible(el);
        hierarchicallyVisible = IsVisible(el);
        return true;
    }

    private bool IsLocallyVisible(nint element)
    {
        if (!_reader.TryReadStruct<uint>(element + Poe2.UiElement.Flags, out var flags)) return false;
        return (flags & (1u << Poe2.UiElement.FlagVisibleBit)) != 0;
    }

    private static MapUi MapViewportToMapUi(MapViewport vp, nint element)
    {
        return new MapUi(
            vp.Visible, vp.ShiftX, vp.ShiftY, 0f, MapViewportLogic.MapDefaultShiftY, vp.Zoom, element,
            (vp.ScreenLeft + vp.ScreenRight) * 0.5f, (vp.ScreenTop + vp.ScreenBottom) * 0.5f,
            vp.ScreenWidth, vp.ScreenHeight, vp.ScreenLeft, vp.ScreenTop, 1f, 0, vp.HasScreenRect);
    }

    public MapViews ReadMapsViewport(nint inGameState, nint areaInstance, int windowWidth, int windowHeight)
    {
        var state = ReadMapState(inGameState, areaInstance, windowWidth, windowHeight);
        return new MapViews(
            MapViewportToMapUi(state.Large, _classifiedLargeEl),
            MapViewportToMapUi(state.Mini, _classifiedMiniEl));
    }

    /// <summary>
    /// Tick-rate pan/zoom + minimap clip lock between full <see cref="ReadMaps"/> calls.
    /// </summary>
    public MapViews RefreshMapPanZoom(MapViews current, nint areaInstance, int windowWidth, int windowHeight)
    {
        _ = areaInstance;
        var miniEl = current.MiniMap.Element != 0 ? current.MiniMap.Element : _classifiedMiniEl;
        var largeEl = current.LargeMap.Element != 0 ? current.LargeMap.Element : _classifiedLargeEl;
        if (miniEl == 0 && largeEl == 0 && _classifiedMiniEl == 0 && _classifiedLargeEl == 0)
            return current;
        return ApplyMapPanFromElements(current, miniEl, largeEl, windowWidth, windowHeight);
    }

    /// <summary>
    /// Per-draw pan/zoom + player lock (render thread). Keeps overlay terrain aligned with the game
    /// map when app tick cadence is lower than overlay present rate.
    /// </summary>
    public bool TryReadMapPanLock(
        nint localPlayer,
        nint miniElement,
        nint largeElement,
        MapViews baseline,
        bool forMinimap,
        int windowWidth,
        int windowHeight,
        out MapPanLockSample sample)
    {
        sample = default;
        if (localPlayer == 0 || (miniElement == 0 && largeElement == 0 && _classifiedMiniEl == 0 && _classifiedLargeEl == 0))
            return false;

        var player = PlayerGrid(localPlayer);
        if (player is not { } pg)
            return false;

        var maps = ApplyMapPanFromElements(baseline, miniElement, largeElement, windowWidth, windowHeight);
        var map = forMinimap ? maps.MiniMap : maps.LargeMap;
        if (!forMinimap && !map.IsVisible)
            map = maps.LargeMap;

        sample = new MapPanLockSample(pg, map.ShiftX, map.ShiftY, map.Zoom > 0f ? map.Zoom : 1f);
        return true;
    }

    private MapViews ApplyMapPanFromElements(MapViews current, nint miniEl, nint largeEl, int windowWidth, int windowHeight)
    {
        var large = current.LargeMap;
        var mini = current.MiniMap;

        // Tab-open signal lives on the BFS corner toggler, not the MapParent minimap render ptr
        // (that element stays locally visible during normal corner-minimap play).
        var togglerEl = _classifiedMiniEl != 0 ? _classifiedMiniEl : miniEl;
        var projLargeEl = largeEl != 0 ? largeEl : _classifiedLargeEl;
        var projMiniEl = _classifiedMiniEl != 0 ? _classifiedMiniEl : miniEl;

        if (togglerEl != 0
            && TryReadMapElement(togglerEl, out var cornerLocal, out _, out var csx, out var csy, out var czoom))
        {
            var tabOpen = MapViewportLogic.IsTabMapOpen(cornerLocal);
            if (!tabOpen && projLargeEl != 0
                && TryReadMapElement(projLargeEl, out _, out _, out var fsx, out var fsy, out var fzoom))
            {
                large = large with { IsVisible = false, ShiftX = fsx, ShiftY = fsy, Zoom = fzoom };
            }
            else
                large = large with { IsVisible = tabOpen, ShiftX = csx, ShiftY = csy, Zoom = czoom };
        }

        if (large.IsVisible)
            mini = mini with { IsVisible = false };
        else if (projMiniEl != 0
                 && TryReadActiveMapProjection(projMiniEl, projLargeEl, out var msx, out var msy, out var mzoom))
        {
            mini = mini with { ShiftX = msx, ShiftY = msy, Zoom = mzoom };
            if (windowWidth > 0 && windowHeight > 0)
                mini = RefreshMinimapScreenRect(mini, projMiniEl, windowWidth, windowHeight);
            mini = mini with { IsVisible = mzoom > 0.05f && mini.HasScreenRect };
        }

        return new MapViews(large, mini);
    }

    private MapUi RefreshMinimapScreenRect(MapUi mini, nint togglerEl, int windowWidth, int windowHeight)
    {
        if (togglerEl == 0)
            return mini;

        var uiScale = windowHeight / 1600f;
        if (TryReadMinimapFrameRect(togglerEl, uiScale, windowWidth, windowHeight, out _, out var cl, out var ct, out var cr, out var cb)
            && MapViewportLogic.IsTopRightMinimapRect(cl, ct, cr, cb, windowWidth, windowHeight))
        {
            return mini with
            {
                PositionX = cl,
                PositionY = ct,
                Width = cr - cl,
                Height = cb - ct,
                CenterX = (cl + cr) * 0.5f,
                CenterY = (ct + cb) * 0.5f,
                HasScreenRect = true,
            };
        }

        var rect = ReadElementScreenRect(togglerEl, uiScale, windowWidth, windowHeight);
        (cl, ct, cr, cb) = MapViewportLogic.ResolveMinimapClipRect(
            rect.left, rect.top, rect.right, rect.bottom, windowWidth, windowHeight, uiScale);
        if (!MapViewportLogic.IsPlausibleMinimapRect(cl, ct, cr, cb, windowWidth, windowHeight))
            return mini;

        return mini with
        {
            PositionX = cl,
            PositionY = ct,
            Width = cr - cl,
            Height = cb - ct,
            CenterX = (cl + cr) * 0.5f,
            CenterY = (ct + cb) * 0.5f,
            HasScreenRect = true,
        };
    }
}
