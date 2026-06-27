namespace POE2Radar.Core.Game;

/// <summary>Single UI rect/projection entry point for panel features.</summary>
public static class UiProjector
{
    /// <summary>Screen rect for a visible UiElement using window size from <paramref name="ui"/>.</summary>
    public static bool TryRect(MemoryReader reader, nint element, UiContextSnapshot ui,
        out float x, out float y, out float w, out float h,
        string? requireFirstLine = null, bool requireVisible = true)
    {
        if (!ui.Valid || ui.WindowWidth <= 0 || ui.WindowHeight <= 0)
            return TryRect(reader, element, 1920f, 1080f, out x, out y, out w, out h, requireFirstLine, requireVisible);
        return TryRect(reader, element, ui.WindowWidth, ui.WindowHeight, out x, out y, out w, out h, requireFirstLine, requireVisible);
    }

    /// <summary>Screen rect with explicit window dimensions.</summary>
    public static bool TryRect(MemoryReader reader, nint element, float windowWidth, float windowHeight,
        out float x, out float y, out float w, out float h,
        string? requireFirstLine = null, bool requireVisible = true)
    {
        x = y = w = h = 0f;
        if (element == 0) return false;
        if (!reader.TryReadStruct<uint>(element + Poe2.UiElement.Flags, out var flags)) return false;
        if (requireVisible && (flags & (1u << Poe2.UiElement.FlagVisibleBit)) == 0) return false;
        if (requireFirstLine is { Length: > 0 })
        {
            if (!TryFirstLine(reader, element, out var line))
                return false;
            if (!string.Equals(line.Trim(), requireFirstLine, StringComparison.Ordinal))
                return false;
        }

        if (!reader.TryReadStruct<byte>(element + Poe2.UiElement.UiScaleIndex, out var idx)) return false;
        reader.TryReadStruct<float>(element + Poe2.UiElement.LocalScaleMul, out var mul);
        reader.TryReadStruct<System.Numerics.Vector2>(element + Poe2.UiElement.SizeW, out var sz);
        var (sw, sh) = ScaleValue(idx, mul, windowWidth, windowHeight);
        if (sw <= 0f || sh <= 0f) return false;
        var (px, py) = UnscaledPos(reader, element, 0, windowWidth, windowHeight);
        if (!float.IsFinite(px) || !float.IsFinite(py)) return false;
        x = px * sw; y = py * sh; w = sz.X * sw; h = sz.Y * sh;
        return w > 1f && h > 1f;
    }

    /// <summary>First visible text line on a UiElement (debug/probes only).</summary>
    public static bool TryText(MemoryReader reader, nint element, out string text)
    {
        text = "";
        if (!TryFirstLine(reader, element, out text)) return false;
        text = text.Trim();
        return text.Length > 0;
    }

    private static bool TryFirstLine(MemoryReader reader, nint element, out string text)
    {
        text = ReadStdWString(reader, element + Poe2.UiElement.Text);
        if (text.Length == 0) return false;
        var nl = text.IndexOf('\n');
        if (nl >= 0) text = text[..nl];
        return true;
    }

    private static string ReadStdWString(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var ptr) || ptr == 0) return "";
        if (!reader.TryReadStruct<long>(addr + 0x10, out var size) || size <= 0 || size > 512) return "";
        return reader.ReadStringUtf16(ptr, (int)Math.Min(size, 256));
    }

    private static (float w, float h) ScaleValue(byte idx, float mul, float winW, float winH)
    {
        if (mul == 0f) mul = 1f;
        var v1 = winW / (float)Poe2.UiElement.BaseResW;
        var v2 = winH / (float)Poe2.UiElement.BaseResH;
        float w = mul, h = mul;
        switch (idx)
        {
            case 1: w *= v1; h *= v1; break;
            case 2: w *= v2; h *= v2; break;
            case 3: w *= v1; h *= v2; break;
        }
        return (w, h);
    }

    private static (float x, float y) UnscaledPos(MemoryReader reader, nint el, int depth, float winW, float winH)
    {
        _ = winW; _ = winH;
        reader.TryReadStruct<System.Numerics.Vector2>(el + Poe2.UiElement.RelativePos, out var rel);
        var parent = Ptr(reader, el + Poe2.UiElement.Parent);
        if (parent == 0 || depth >= 64) return (rel.X, rel.Y);

        var (ppx, ppy) = UnscaledPos(reader, parent, depth + 1, winW, winH);
        if (reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)
            && (flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            reader.TryReadStruct<System.Numerics.Vector2>(el + Poe2.UiElement.UiPositionModifier, out var mod);
            ppx += mod.X; ppy += mod.Y;
        }
        return (ppx + rel.X, ppy + rel.Y);
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
