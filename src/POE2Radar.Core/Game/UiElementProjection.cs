using System.Runtime.InteropServices;

namespace POE2Radar.Core.Game;

/// <summary>
/// Reconstructs a UiElement's screen-space rectangle from the live UI tree. This mirrors GameHelper's
/// UiElementBase projection model: every element contributes its own relative position scaled by its own
/// scale pair, parent position modifiers are applied in the parent's scale, and widescreen UI cull is
/// added once at the root.
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

    internal static Point ScalePair(
        byte scaleIndex,
        float localScaleMultiplier,
        float windowWidth,
        float windowHeight,
        float? horizontalCull = null)
    {
        var mul = float.IsFinite(localScaleMultiplier) && localScaleMultiplier > 0f
            ? MathF.Max(0.0001f, localScaleMultiplier)
            : 1f;

        Point pair;
        if (horizontalCull is { } cull)
        {
            // Sekhema's current GameHelper layout uses the live game-cull-aware scale indices.
            // Keep this model opt-in; every established overlay consumer relies on the mapping below.
            var widthScale = (windowWidth - cull * 2f) / (float)Poe2.UiElement.BaseResW;
            var heightScale = windowHeight / (float)Poe2.UiElement.BaseResH;
            pair = scaleIndex switch
            {
                1 => new Point(widthScale, widthScale),
                2 => new Point(heightScale, heightScale),
                3 => new Point(widthScale, heightScale),
                _ => new Point(1f, 1f),
            };
        }
        else
        {
            var sx = windowWidth / (float)Poe2.UiElement.BaseResW;
            var sy = windowHeight / (float)Poe2.UiElement.BaseResH;
            pair = scaleIndex switch
            {
                0 => new Point(sx, sx),
                1 => new Point(sy, sy),
                2 => new Point(MathF.Min(sx, sy), MathF.Min(sx, sy)),
                _ => new Point(sx, sy),
            };
        }

        return new Point(pair.X * mul, pair.Y * mul);
    }

    /// <summary>
    /// Aspect-ratio fallback for PoE's horizontal UI cull. Live projection should prefer the
    /// client value because ultrawide layouts can deliberately use a different cull.
    /// </summary>
    internal static float HorizontalCull(float windowWidth, float windowHeight)
    {
        if (!IsFinite(windowWidth) || !IsFinite(windowHeight) || windowWidth <= 0f || windowHeight <= 0f)
            return 0f;
        var fittedWidth = windowHeight * (float)(Poe2.UiElement.BaseResW / Poe2.UiElement.BaseResH);
        return MathF.Max(0f, (windowWidth - fittedWidth) * 0.5f);
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

    /// <summary>
    /// Reads the projection fields in one cross-process call. The fields are sparse across the
    /// UiElement base object, so this transfers more bytes than <see cref="TryRead"/>, but avoids
    /// eleven kernel transitions per element on geometry-heavy overlay paths.
    /// </summary>
    internal static bool TryReadBatch(MemoryReader reader, nint address, out Element element)
    {
        element = default;
        if (address == 0) return false;

        const int start = Poe2.UiElement.Self;
        const int end = Poe2.UiElement.SizeH + sizeof(float);
        Span<byte> bytes = stackalloc byte[end - start];
        if (reader.TryReadBytes(address + start, bytes) != bytes.Length) return false;

        element = new Element(
            Read<nint>(bytes, Poe2.UiElement.Self - start),
            Read<nint>(bytes, Poe2.UiElement.Parent - start),
            Read<uint>(bytes, Poe2.UiElement.Flags - start),
            Read<float>(bytes, Poe2.UiElement.RelativePos - start),
            Read<float>(bytes, Poe2.UiElement.RelativePos + 4 - start),
            Read<float>(bytes, Poe2.UiElement.UiPositionModifier - start),
            Read<float>(bytes, Poe2.UiElement.UiPositionModifier + 4 - start),
            Read<float>(bytes, Poe2.UiElement.LocalScaleMul - start),
            Read<byte>(bytes, Poe2.UiElement.UiScaleIndex - start),
            Read<float>(bytes, Poe2.UiElement.SizeW - start),
            Read<float>(bytes, Poe2.UiElement.SizeH - start));
        return true;
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes, int offset) where T : unmanaged
        => MemoryMarshal.Read<T>(bytes[offset..]);

    internal static bool TryGetRect(
        nint address,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache,
        IDictionary<nint, Point>? parentOffsetCache,
        out Rect rect,
        float? horizontalCull = null)
    {
        rect = default;
        if (windowWidth <= 0f || windowHeight <= 0f) return false;
        if (!ReadCached(address, read, elementCache, out var leaf)) return false;

        var topLeft = horizontalCull is { } cull
            ? GetLeafTopLeft(
                leaf,
                read,
                windowWidth,
                windowHeight,
                elementCache,
                parentOffsetCache,
                cull)
            : GetEstablishedLeafTopLeft(
                leaf,
                read,
                windowWidth,
                windowHeight,
                elementCache,
                parentOffsetCache);
        var scale = ScalePair(
            leaf.ScaleIndex,
            leaf.LocalScaleMultiplier,
            windowWidth,
            windowHeight,
            horizontalCull);
        rect = new Rect(topLeft.X, topLeft.Y, leaf.SizeW * scale.X, leaf.SizeH * scale.Y);
        return IsFinite(rect.X) && IsFinite(rect.Y) && rect.W > 1f && rect.H > 1f;
    }

    internal static Point GetFinalTopLeft(
        Element leaf,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache = null,
        float? horizontalCull = null)
    {
        if (horizontalCull is null)
            return GetEstablishedFinalTopLeft(leaf, read, windowWidth, windowHeight, elementCache);

        var cull = horizontalCull.Value;
        var pos = new Point(cull, 0f);
        var cur = leaf;
        var guard = 0;

        while (true)
        {
            var scale = ScalePair(
                cur.ScaleIndex,
                cur.LocalScaleMultiplier,
                windowWidth,
                windowHeight,
                cull);
            pos = new Point(
                pos.X + cur.RelativeX * scale.X,
                pos.Y + cur.RelativeY * scale.Y);

            if (cur.Parent == 0 || cur.Parent == cur.Self || ++guard > 64)
                break;

            if (!ReadCached(cur.Parent, read, elementCache, out var parent))
                break;

            if ((cur.Flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
            {
                var parentScale = ScalePair(
                    parent.ScaleIndex,
                    parent.LocalScaleMultiplier,
                    windowWidth,
                    windowHeight,
                    cull);
                pos = new Point(
                    pos.X + parent.PositionModifierX * parentScale.X,
                    pos.Y + parent.PositionModifierY * parentScale.Y);
            }

            cur = parent;
        }

        return pos;
    }

    private static Point GetEstablishedFinalTopLeft(
        Element leaf,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache)
    {
        var pos = new Point(0f, 0f);
        var current = leaf;
        nint last = 0;
        var guard = 0;

        while (true)
        {
            pos = AddEstablishedContribution(pos, current, windowWidth, windowHeight);
            if (current.Parent == 0 || current.Parent == last || ++guard > 64)
                break;

            last = current.Self;
            if (!ReadCached(current.Parent, read, elementCache, out current))
                break;
        }

        return pos;
    }

    private static Point GetEstablishedLeafTopLeft(
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
        else if (parentOffsetCache is not null &&
                 parentOffsetCache.TryGetValue(leaf.Parent, out var cached))
        {
            parentOffset = cached;
        }
        else if (ReadCached(leaf.Parent, read, elementCache, out var parent))
        {
            parentOffset = GetEstablishedFinalTopLeft(
                parent,
                read,
                windowWidth,
                windowHeight,
                elementCache);
            if (parentOffsetCache is not null)
                parentOffsetCache[leaf.Parent] = parentOffset;
        }
        else
        {
            parentOffset = new Point(0f, 0f);
        }

        return AddEstablishedContribution(
            parentOffset,
            leaf,
            windowWidth,
            windowHeight);
    }

    private static Point AddEstablishedContribution(
        Point position,
        Element element,
        float windowWidth,
        float windowHeight)
    {
        var scale = ScalePair(
            element.ScaleIndex,
            element.LocalScaleMultiplier,
            windowWidth,
            windowHeight);
        var x = position.X + element.RelativeX * scale.X;
        var y = position.Y + element.RelativeY * scale.Y;
        if ((element.Flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            x += element.PositionModifierX * scale.X;
            y += element.PositionModifierY * scale.Y;
        }

        return new Point(x, y);
    }

    private static Point GetLeafTopLeft(
        Element leaf,
        TryReadElement read,
        float windowWidth,
        float windowHeight,
        IDictionary<nint, Element>? elementCache,
        IDictionary<nint, Point>? parentOffsetCache,
        float horizontalCull)
    {
        Point parentOffset;
        if (leaf.Parent == 0)
        {
            parentOffset = new Point(horizontalCull, 0f);
        }
        else if (parentOffsetCache is not null && parentOffsetCache.TryGetValue(leaf.Parent, out var cached))
        {
            parentOffset = cached;
        }
        else if (ReadCached(leaf.Parent, read, elementCache, out var parent))
        {
            parentOffset = GetFinalTopLeft(
                parent,
                read,
                windowWidth,
                windowHeight,
                elementCache,
                horizontalCull);
            if (parentOffsetCache is not null)
                parentOffsetCache[leaf.Parent] = parentOffset;
        }
        else
        {
            parentOffset = new Point(horizontalCull, 0f);
        }

        var leafScale = ScalePair(
            leaf.ScaleIndex,
            leaf.LocalScaleMultiplier,
            windowWidth,
            windowHeight,
            horizontalCull);
        var x = parentOffset.X + leaf.RelativeX * leafScale.X;
        var y = parentOffset.Y + leaf.RelativeY * leafScale.Y;
        if ((leaf.Flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0
            && leaf.Parent != 0
            && ReadCached(leaf.Parent, read, elementCache, out var leafParent))
        {
            var parentScale = ScalePair(
                leafParent.ScaleIndex,
                leafParent.LocalScaleMultiplier,
                windowWidth,
                windowHeight,
                horizontalCull);
            x += leafParent.PositionModifierX * parentScale.X;
            y += leafParent.PositionModifierY * parentScale.Y;
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
