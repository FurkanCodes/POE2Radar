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
    /// Live-validated (Research --map-probe): Tab open = local visible bit on the corner widget.
    /// </summary>
    public static bool IsTabMapOpen(bool cornerLocalVisible) => cornerLocalVisible;

    /// <summary>
    /// The Tab map and corner toggler are the two live MapUiElements. GH2 MapParent field names are
    /// not trustworthy in PoE2 — assign roles by intrinsic UiElement size (larger = Tab map).
    /// </summary>
    public static void ClassifyByIntrinsicSize(
        float largeW, float largeH, float miniW, float miniH,
        out bool firstIsLarge)
        => firstIsLarge = largeW * largeH >= miniW * miniH;

    /// <summary>Map grid→screen anchor for the fullscreen Tab map overlay.</summary>
    public static (float X, float Y) MapProjectionCenter(
        int windowWidth, int windowHeight,
        float shiftX, float shiftY,
        float offsetX, float offsetY)
        => (
            windowWidth * 0.5f + shiftX + offsetX,
            windowHeight * 0.5f + shiftY + MapDefaultShiftY + offsetY);

    /// <summary>Per-element snapshot for corner-toggler selection (unit-tested).</summary>
    public readonly record struct MiniElementRead(
        bool LocalVisible, float ShiftX, float ShiftY, float Zoom,
        float ScreenLeft, float ScreenTop, float ScreenRight, float ScreenBottom);

    /// <summary>Pick the corner toggler: the locally-visible map element with the smallest screen area.</summary>
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
}
