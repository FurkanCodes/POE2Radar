namespace POE2Radar.Core.Game;

/// <summary>Loot-tag style UI element screen rect — delegates to <see cref="UiProjector"/>.</summary>
public static class UiElementScreenRect
{
    public static bool TryGet(MemoryReader reader, nint el, float winW, float winH, out float x, out float y, out float w, out float h)
        => UiProjector.TryRect(reader, el, winW, winH, out x, out y, out w, out h);
}
