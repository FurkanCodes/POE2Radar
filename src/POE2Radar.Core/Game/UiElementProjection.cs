namespace POE2Radar.Core.Game;

/// <summary>
/// Reconstructs a UiElement's screen-space rectangle from the live UI tree. This mirrors GameHelper's
/// UiElementBase projection model: every element contributes its own relative position scaled by its own
/// scale pair, and parent offsets are accumulated up to the UI root.
/// </summary>
internal static class UiElementProjection
{
    internal readonly record struct Element(
        nint Self,
        nint Parent,
        uint Flags,
        float RelativeX,
        float RelativeY,
        float PositionModifierX,
        float PositionModifierY,
        float LocalScaleMultiplier,
        byte ScaleIndex,
        float SizeW,
        float SizeH);

    internal readonly record struct Point(float X, float Y);

    internal readonly record struct Rect(float X, float Y, float W, float H)
    {
        public Point Center => new(X + W * 0.5f, Y + H * 0.5f);
    }

    internal delegate bool TryReadElement(nint address, out Element element);

    internal static Point ScalePair(byte scaleIndex, float localScaleMultiplier, float windowWidth, float windowHeight)
    {
        var sx = windowWidth / (float)Poe2.UiElement.BaseResW;
        var sy = windowHeight / (float)Poe2.UiElement.BaseResH;
        var pair = scaleIndex switch
        {
            0 => new Point(sx, sx),
            1 => new Point(sy, sy),
            2 => new Point(MathF.Min(sx, sy), MathF.Min(sx, sy)),
            _ => new Point(sx, sy),
        };

        var mul = float.IsFinite(localScaleMultiplier) && localScaleMultiplier > 0f
            ? MathF.Max(0.0001f, localScaleMultiplier)
            : 1f;
        return new Point(pair.X * mul, pair.Y * mul);
    }

    internal static bool TryRead(MemoryReader reader, nint address, out Element element)
    {
        element = default;
        if (address == 0) return false;
        if (!reader.TryReadStruct<nint>(address + Poe2.UiElement.Self, out var self)) return false;
        if (!reader.TryReadStruct<nint>(address + Poe2.UiElement.Parent, out var parent)) return false;
        if (!reader.TryReadStruct<uint>(address + Poe2.UiElement.Flags, out var flags)) return false;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.RelativePos, out var x)) return false;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.RelativePos + 4, out var y)) return false;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.SizeW, out var w)) return false;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.SizeH, out var h)) return false;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.LocalScaleMul, out var mul)) mul = 1f;
        if (!reader.TryReadStruct<byte>(address + Poe2.UiElement.UiScaleIndex, out var scaleIndex)) scaleIndex = 2;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.UiPositionModifier, out var modX)) modX = 0f;
        if (!reader.TryReadStruct<float>(address + Poe2.UiElement.UiPositionModifier + 4, out var modY)) modY = 0f;

        element = new Element(self, parent, flags, x, y, modX, modY, mul, scaleIndex, w, h);
        return true;
    }

    internal static bool TryGetRect(
        nint address,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache,
        IDictionary<nint, Point>? parentOffsetCache,
        out Rect rect)
    {
        rect = default;
        if (windowWidth <= 0f || windowHeight <= 0f) return false;
        if (!ReadCached(address, read, elementCache, out var leaf)) return false;

        var topLeft = GetLeafTopLeft(leaf, read, windowWidth, windowHeight, elementCache, parentOffsetCache);
        var scale = ScalePair(leaf.ScaleIndex, leaf.LocalScaleMultiplier, windowWidth, windowHeight);
        rect = new Rect(topLeft.X, topLeft.Y, leaf.SizeW * scale.X, leaf.SizeH * scale.Y);
        return IsFinite(rect.X) && IsFinite(rect.Y) && rect.W > 1f && rect.H > 1f;
    }

    internal static Point GetFinalTopLeft(
        Element leaf,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache = null)
    {
        var pos = new Point(0f, 0f);
        var cur = leaf;
        nint last = 0;
        var guard = 0;

        while (true)
        {
            pos = AddContribution(pos, cur, windowWidth, windowHeight);
            if (cur.Parent == 0 || cur.Parent == last || ++guard > 64)
                break;

            last = cur.Self;
            if (!ReadCached(cur.Parent, read, elementCache, out cur))
                break;
        }

        return pos;
    }

    private static Point GetLeafTopLeft(
        Element leaf,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache,
        IDictionary<nint, Point>? parentOffsetCache)
    {
        Point parentOffset;
        if (leaf.Parent == 0)
        {
            parentOffset = new Point(0f, 0f);
        }
        else if (parentOffsetCache is not null && parentOffsetCache.TryGetValue(leaf.Parent, out var cached))
        {
            parentOffset = cached;
        }
        else if (ReadCached(leaf.Parent, read, elementCache, out var parent))
        {
            parentOffset = GetFinalTopLeft(parent, read, windowWidth, windowHeight, elementCache);
            parentOffsetCache?.Add(leaf.Parent, parentOffset);
        }
        else
        {
            parentOffset = new Point(0f, 0f);
        }

        return AddContribution(parentOffset, leaf, windowWidth, windowHeight);
    }

    private static Point AddContribution(Point pos, Element element, float windowWidth, float windowHeight)
    {
        var scale = ScalePair(element.ScaleIndex, element.LocalScaleMultiplier, windowWidth, windowHeight);
        var x = pos.X + element.RelativeX * scale.X;
        var y = pos.Y + element.RelativeY * scale.Y;
        if ((element.Flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            x += element.PositionModifierX * scale.X;
            y += element.PositionModifierY * scale.Y;
        }

        return new Point(x, y);
    }

    private static bool ReadCached(nint address, TryReadElement read, IDictionary<nint, Element>? cache, out Element element)
    {
        if (cache is not null && cache.TryGetValue(address, out element))
            return true;
        if (!read(address, out element))
            return false;
        cache?.Add(address, element);
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
