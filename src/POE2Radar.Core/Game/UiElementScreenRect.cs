namespace POE2Radar.Core.Game;

/// <summary>Loot-tag style UI element screen rect — matches <see cref="Poe2Live.TryUiElementRect"/>.</summary>
internal static class UiElementScreenRect
{
    public static bool TryGet(MemoryReader reader, nint el, float winW, float winH, out float x, out float y, out float w, out float h)
    {
        x = y = w = h = 0f;
        if (el == 0) return false;
        if (!reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)) return false;
        if ((flags & (1u << Poe2.UiElement.FlagVisibleBit)) == 0) return false;
        if (!reader.TryReadStruct<byte>(el + Poe2.UiElement.UiScaleIndex, out var idx)) return false;
        reader.TryReadStruct<float>(el + Poe2.UiElement.LocalScaleMul, out var mul);
        reader.TryReadStruct<float>(el + Poe2.UiElement.SizeW, out var uw);
        reader.TryReadStruct<float>(el + Poe2.UiElement.SizeH, out var uh);
        var (sw, sh) = ScaleValue(idx, mul, winW, winH);
        if (sw <= 0f || sh <= 0f) return false;
        var (px, py) = UnscaledPos(reader, el, 0);
        if (!float.IsFinite(px) || !float.IsFinite(py)) return false;
        x = px * sw; y = py * sh; w = uw * sw; h = uh * sh;
        return w > 1f && h > 1f;
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

    private static (float x, float y) UnscaledPos(MemoryReader reader, nint el, int depth)
    {
        reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos, out var lx);
        reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos + 4, out var ly);
        var parent = Ptr(reader, el + Poe2.UiElement.Parent);
        if (parent == 0 || depth >= 64) return (lx, ly);

        var (ppx, ppy) = UnscaledPos(reader, parent, depth + 1);
        if (reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)
            && (flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier, out var mx);
            reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier + 4, out var my);
            ppx += mx; ppy += my;
        }
        return (ppx + lx, ppy + ly);
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
