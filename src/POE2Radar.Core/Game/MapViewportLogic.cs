namespace POE2Radar.Core.Game;

/// <summary>
/// Screen-rect math and map-element classification for <see cref="Poe2Live.ReadMap"/>.
/// </summary>
public static class MapViewportLogic
{
    /// <summary>MapUiElement.DefaultShift Y (live-validated 0,-20). Applied to window-centered large map only.</summary>
    public const float MapDefaultShiftY = -20f;

    /// <summary>Clamp an unscaled UI position + size into window pixels.</summary>
    public static (float Left, float Top, float Right, float Bottom) ClampScreenRect(
        float x, float y, float w, float h, float uiScale, int windowWidth, int windowHeight)
    {
        if (w <= 0f || h <= 0f) { w = 250f; h = 250f; }

        var left = x * uiScale;
        var top = y * uiScale;
        var right = left + w * uiScale;
        var bottom = top + h * uiScale;

        left = Math.Clamp(left, 0f, windowWidth);
        top = Math.Clamp(top, 0f, windowHeight);
        right = Math.Clamp(right, 0f, windowWidth);
        bottom = Math.Clamp(bottom, 0f, windowHeight);
        return (left, top, right, bottom);
    }

    public static bool HasArea(float left, float top, float right, float bottom)
        => right > left + 1f && bottom > top + 1f;

    /// <summary>
    /// Resolve the mutually-exclusive large-map/minimap state from hierarchical visibility. Local
    /// visibility is not sufficient: current PoE2 keeps both MapUiElements locally visible while the
    /// fullscreen element is hidden by an ancestor. Research --map-probe validates the hierarchy.
    /// </summary>
    public static (bool LargeVisible, bool MiniVisible) ResolveMapVisibility(
        bool largeHierarchicallyVisible,
        bool miniHierarchicallyVisible,
        bool minimapFrameVisible)
        => (
            largeHierarchicallyVisible,
            !largeHierarchicallyVisible && (miniHierarchicallyVisible || minimapFrameVisible));

    /// <summary>
    /// Current standalone layout exposes one shared map-content MapUiElement and a separate corner
    /// minimap frame. Tab-open makes the content visible and hides the frame; Tab-closed does the
    /// inverse. If both appear visible during a transition, prefer the corner frame so the two draw
    /// layers remain mutually exclusive.
    /// </summary>
    public static (bool LargeVisible, bool MiniVisible) ResolveSharedMapVisibility(
        bool contentVisible,
        bool minimapFrameVisible)
        => (
            contentVisible && !minimapFrameVisible,
            minimapFrameVisible);

    /// <summary>
    /// The Tab map and corner minimap are the two live MapUiElements. GH2 MapParent field names are
    /// not trustworthy in PoE2 — assign roles by intrinsic UiElement size (larger = Tab map).
    /// </summary>
    public static void ClassifyByIntrinsicSize(
        float largeW, float largeH, float miniW, float miniH,
        out bool firstIsLarge)
        => firstIsLarge = largeW * largeH >= miniW * miniH;

    /// <summary>Size-only sanity check for a corner-minimap clip rect.</summary>
    public static bool IsPlausibleMinimapRect(
        float left, float top, float right, float bottom, int windowWidth, int windowHeight)
    {
        if (!HasArea(left, top, right, bottom)) return false;
        var w = right - left;
        var h = bottom - top;
        var maxSide = Math.Max(windowWidth, windowHeight) * 0.45f;
        return w >= 80f && h >= 80f && w <= maxSide && h <= maxSide;
    }

    /// <summary>
    /// PoE2's corner minimap sits top-right below the top HUD band. The corner MapUiElement often
    /// reports size 0×0 with a parent-chain rect in the middle of the screen (live --map-probe).
    /// </summary>
    public static bool IsTopRightMinimapRect(
        float left, float top, float right, float bottom, int windowWidth, int windowHeight)
    {
        if (!IsPlausibleMinimapRect(left, top, right, bottom, windowWidth, windowHeight)) return false;
        return left > windowWidth * 0.55f && top < windowHeight * 0.42f;
    }

    /// <summary>Resolve the clip rect used for minimap overlay drawing.</summary>
    public static (float Left, float Top, float Right, float Bottom) ResolveMinimapClipRect(
        float left, float top, float right, float bottom, int windowWidth, int windowHeight, float uiScale)
    {
        if (IsTopRightMinimapRect(left, top, right, bottom, windowWidth, windowHeight))
            return (left, top, right, bottom);
        return TopRightMinimapRect(windowWidth, windowHeight, uiScale);
    }

    /// <summary>
    /// Live-validated (Research --map-scan-frames): the corner MapUiElement is 0×0 mid-screen; the
    /// visible minimap frame is a square sibling under its parent (e.g. 402×402 unscaled → ~362px).
    /// </summary>
    public static bool TrySelectMinimapFrameRect(
        IReadOnlyList<MinimapFrameCandidate> candidates,
        int windowWidth, int windowHeight,
        out float left, out float top, out float right, out float bottom)
    {
        left = top = right = bottom = 0;
        var bestArea = 0f;
        var found = false;
        foreach (var c in candidates)
        {
            if (c.Width <= 0f || c.Height <= 0f) continue;
            var aspect = c.Width / c.Height;
            if (aspect is < 0.85f or > 1.15f) continue;
            if (!c.Visible) continue;
            if (!IsTopRightMinimapRect(c.Left, c.Top, c.Right, c.Bottom, windowWidth, windowHeight)) continue;
            var area = (c.Right - c.Left) * (c.Bottom - c.Top);
            if (found && area <= bestArea) continue;
            found = true;
            bestArea = area;
            left = c.Left;
            top = c.Top;
            right = c.Right;
            bottom = c.Bottom;
        }
        return found;
    }

    public readonly record struct MinimapFrameCandidate(
        float Width, float Height,
        float Left, float Top, float Right, float Bottom,
        bool Visible);

    /// <summary>
    /// Map grid→screen anchor. Large map: window center + pan shift + DefaultShift. Minimap: frame clip
    /// center only — pan shift belongs to the fullscreen Tab map; applying it here (e.g. 14,173 fallback)
    /// misaligns the overlay completely. DefaultShift is also omitted: the frame rect already includes
    /// layout (live --map-probe: -20px on clip center shifted content ~20px too high).
    /// </summary>
    public static (float X, float Y) MapProjectionCenter(
        int windowWidth, int windowHeight,
        float shiftX, float shiftY,
        float offsetX, float offsetY,
        bool minimapClip,
        float clipLeft, float clipTop, float clipRight, float clipBottom,
        bool hasLargeMapAnchor = false,
        float largeMapAnchorX = 0f,
        float largeMapAnchorY = 0f)
    {
        if (minimapClip)
        {
            return (
                (clipLeft + clipRight) * 0.5f + offsetX,
                (clipTop + clipBottom) * 0.5f + offsetY);
        }

        var anchorX = hasLargeMapAnchor && float.IsFinite(largeMapAnchorX)
            ? largeMapAnchorX
            : windowWidth * 0.5f;
        var anchorY = hasLargeMapAnchor && float.IsFinite(largeMapAnchorY)
            ? largeMapAnchorY
            : windowHeight * 0.5f;
        return (
            anchorX + shiftX + offsetX,
            anchorY + shiftY + MapDefaultShiftY + offsetY);
    }

    /// <summary>Per-element snapshot for corner-minimap selection (unit-tested).</summary>
    public readonly record struct MiniElementRead(
        bool LocalVisible, float ShiftX, float ShiftY, float Zoom,
        float ScreenLeft, float ScreenTop, float ScreenRight, float ScreenBottom);

    /// <summary>Pick the corner minimap: the locally-visible map element with the smallest screen area.</summary>
    public static bool TrySelectLocalVisibleMini(
        IReadOnlyList<MiniElementRead> elements,
        out MiniElementRead selected)
    {
        selected = default;
        var bestArea = float.MaxValue;
        var found = false;
        foreach (var el in elements)
        {
            if (!el.LocalVisible || el.Zoom is <= 0.05f or >= 8f) continue;
            var area = HasArea(el.ScreenLeft, el.ScreenTop, el.ScreenRight, el.ScreenBottom)
                ? (el.ScreenRight - el.ScreenLeft) * (el.ScreenBottom - el.ScreenTop)
                : float.MaxValue;
            if (found && area >= bestArea) continue;
            found = true;
            bestArea = area;
            selected = el;
        }
        return found;
    }

    /// <summary>Default minimap side length — ~19% of client height matches live PoE2 at 1440p (≈274px).</summary>
    public static float DefaultMinimapSide(int windowHeight, float uiScale)
        => Math.Clamp(windowHeight * 0.19f, 200f, 400f);

    /// <summary>Top-right minimap when memory layout rect is missing or wrong (PoE2 live default).</summary>
    public static (float Left, float Top, float Right, float Bottom) TopRightMinimapRect(
        int windowWidth, int windowHeight, float uiScale)
    {
        var size = DefaultMinimapSide(windowHeight, uiScale);
        const float margin = 8f;
        var top = Math.Clamp(130f * uiScale, 72f, 220f);
        return (
            windowWidth - size - margin,
            top,
            windowWidth - margin,
            top + size);
    }

    /// <summary>GameHelper Radar large-map scale from the height-adjusted base diagonal.</summary>
    public static float LargeMapOverlayScale(float windowHeight, float zoom, float largeMapScaleMultiplier)
    {
        var h = MathF.Max(1f, windowHeight);
        var z = zoom > 0f ? zoom : 1f;
        return Pathfinding.GameHelperRadarProjection.LargeMapScale(
            h,
            z,
            MathF.Max(0.01f, largeMapScaleMultiplier));
    }

    /// <summary>GameHelper Radar minimap scale from the height-adjusted base diagonal.</summary>
    public static float MinimapOverlayScale(float referenceSide, float zoom, float scaleMul)
    {
        var side = MathF.Max(1f, referenceSide);
        var z = zoom > 0f ? zoom : 1f;
        return Pathfinding.GameHelperRadarProjection.MiniMapScale(
            side,
            z,
            MathF.Max(0.01f, scaleMul));
    }

    /// <summary>Alias kept for callers that still mention a generic fallback.</summary>
    public static (float Left, float Top, float Right, float Bottom) FallbackMinimapRect(
        int windowWidth, int windowHeight, float uiScale)
        => TopRightMinimapRect(windowWidth, windowHeight, uiScale);
}
