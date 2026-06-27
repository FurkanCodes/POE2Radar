namespace POE2Radar.Core.Game;

/// <summary>Single UI rect/projection entry point for panel features.</summary>
public static class UiProjector
{
    /// <summary>Screen rect for a visible UiElement using window size from <paramref name="ui"/>.</summary>
    public static bool TryRect(MemoryReader reader, nint element, UiContextSnapshot ui,
        out float x, out float y, out float w, out float h)
    {
        x = y = w = h = 0f;
        if (!ui.Valid || element == 0 || ui.WindowWidth <= 0 || ui.WindowHeight <= 0)
            return false;
        return UiElementScreenRect.TryGet(reader, element, ui.WindowWidth, ui.WindowHeight, out x, out y, out w, out h);
    }

    /// <summary>Screen rect with explicit window dimensions (atlas/probes).</summary>
    public static bool TryRect(MemoryReader reader, nint element, float windowWidth, float windowHeight,
        out float x, out float y, out float w, out float h)
        => UiElementScreenRect.TryGet(reader, element, windowWidth, windowHeight, out x, out y, out w, out h);
}
