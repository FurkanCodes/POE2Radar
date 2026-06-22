using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Core.Game;

/// <summary>
/// Reads the in-game "Runeshape Combinations" reward panel for rune-crafting league mechanics.
/// Read-only; validated live 2026-06-14 (Research --runeforge).
/// </summary>
public sealed class Poe2Runeforge
{
    private readonly MemoryReader _reader;

    private nint _panel;
    private nint _viewport;
    private DateTime _nextResolveUtc = DateTime.MinValue;
    private const int ResolveThrottleMs = 120;

    public Poe2Runeforge(MemoryReader reader) => _reader = reader;

    public readonly record struct RuneReward(int Count, string Name, float X, float Y, float W, float H);

    public bool PanelOpen { get; private set; }

    public List<RuneReward> ReadRewards(nint inGameState, float winW, float winH)
    {
        PanelOpen = false;
        var gameUi = Ptr(inGameState + Poe2.InGameState.UiRoot);
        if (gameUi == 0) { _panel = 0; return new List<RuneReward>(); }

        var titleEl = FindTitleElement(gameUi);
        var titleVisible = titleEl != 0;

        var now = DateTime.UtcNow;
        if (_panel == 0 || now >= _nextResolveUtc)
        {
            _nextResolveUtc = now.AddMilliseconds(ResolveThrottleMs);
            _viewport = 0;
            _panel = Walk(gameUi, 0);
            if (_panel == 0) _panel = FindPanelByTitle(gameUi, titleEl);
        }

        var result = _panel != 0 ? ReadRowsFromPanel(_panel, winW, winH) : new List<RuneReward>();

        // Stale cached panel or drifted fingerprints: re-resolve once when the title is visible but rows read empty.
        if (result.Count == 0 && titleVisible)
        {
            _panel = 0;
            _viewport = 0;
            _panel = Walk(gameUi, 0);
            if (_panel == 0) _panel = FindPanelByTitle(gameUi, titleEl);
            if (_panel != 0) result = ReadRowsFromPanel(_panel, winW, winH);
            if (result.Count == 0) result = ScanRewardTexts(gameUi, titleEl, winW, winH);
        }

        if (result.Count > 0) PanelOpen = true;
        return result;
    }

    private List<RuneReward> ReadRowsFromPanel(nint panel, float winW, float winH)
    {
        var result = new List<RuneReward>();
        if (!Children(panel, out var first, out var n)) { _panel = 0; return result; }

        var scroll = ReadScroll(_viewport);
        for (long i = 0; i < n; i++)
        {
            var row = Ptr(first + (nint)(i * 8));
            if (row == 0 || !Visible(row)) continue;
            var raw = ReadRowRewardText(row);
            if (string.IsNullOrEmpty(raw)) continue;
            ParseNameCount(raw, out var count, out var name);
            if (!TryRewardRect(row, scroll, winW, winH, out var pos, out var size)) continue;
            result.Add(new RuneReward(count, name, pos.X, pos.Y, size.X, size.Y));
        }
        return result;
    }

    /// <summary>When the structured panel walk fails, scan visible UI text near the panel title.</summary>
    private List<RuneReward> ScanRewardTexts(nint uiRoot, nint titleEl, float winW, float winH)
    {
        var result = new List<RuneReward>();
        if (titleEl == 0) titleEl = FindTitleElement(uiRoot);
        if (titleEl == 0) return result;
        if (!TryUiRect(titleEl, winW, winH, out var titleX, out var titleY, out _, out _)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<nint>(); queue.Enqueue(uiRoot);
        var visited = new HashSet<nint>();
        while (queue.Count > 0 && visited.Count < 20000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (Children(el, out var f, out var nn))
                for (long k = 0; k < nn; k++) queue.Enqueue(Ptr(f + (nint)(k * 8)));

            if (!Visible(el)) continue;
            var raw = ReadElementText(el);
            if (!LooksLikeRewardRow(raw)) continue;
            ParseNameCount(raw, out var count, out var name);
            if (!seen.Add($"{count}|{name}")) continue;
            if (!TryUiRect(el, winW, winH, out var x, out var y, out var w, out var h)) continue;
            // Keep rows in the panel column below the title (screen-space band around the title element).
            if (MathF.Abs(x + w * 0.5f - (titleX + 40f)) > 420f) continue;
            if (y < titleY - 24f || y > titleY + 820f) continue;
            result.Add(new RuneReward(count, name, x, y, w, h));
        }

        result.Sort((a, b) => a.Y.CompareTo(b.Y));
        return result;
    }

    private bool TryRewardRect(nint row, NumVec2 scroll, float winW, float winH, out NumVec2 pos, out NumVec2 size)
    {
        if (TryScreenRect(row, scroll, winW, winH, out pos, out size)) return true;
        if (TryUiRect(row, winW, winH, out var x, out var y, out var w, out var h))
        {
            pos = new NumVec2(x, y);
            size = new NumVec2(w, h);
            return true;
        }
        // Text may live on a child label while the row band is what we want for placement.
        if (Children(row, out var first, out var n))
        {
            for (long i = 0; i < n; i++)
            {
                var child = Ptr(first + (nint)(i * 8));
                if (child == 0) continue;
                var raw = ReadElementText(child);
                if (!LooksLikeRewardRow(raw)) continue;
                if (TryUiRect(child, winW, winH, out x, out y, out w, out h))
                {
                    pos = new NumVec2(x, y);
                    size = new NumVec2(w, h);
                    return true;
                }
            }
        }
        pos = default; size = default;
        return false;
    }

    private string ReadRowRewardText(nint row)
    {
        if (Children(row, out var first, out var n))
        {
            for (long i = 0; i < n; i++)
            {
                var child = Ptr(first + (nint)(i * 8));
                if (child == 0) continue;
                var raw = ReadElementText(child);
                if (LooksLikeRewardRow(raw)) return raw;
            }
        }
        var onRow = ReadElementText(row);
        return LooksLikeRewardRow(onRow) ? onRow : "";
    }

    private string ReadElementText(nint el)
    {
        var t = ReadStdWString(el + Poe2.UiElement.Text);
        return string.IsNullOrEmpty(t) ? ReadStdWString(el + Poe2.Runeforge.NameWString) : t;
    }

    private static bool LooksLikeRewardRow(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var t = raw.Trim();
        if (t.Contains("Runeshape Combinations", StringComparison.OrdinalIgnoreCase)) return false;
        var i = 0;
        while (i < t.Length && char.IsDigit(t[i])) i++;
        return i > 0 && i < t.Length && (t[i] == 'x' || t[i] == 'X');
    }

    private nint Walk(nint parent, int step)
    {
        var fps = Poe2.Runeforge.PanelFlagFingerprints;
        const uint visibleMask = 1u << Poe2.UiElement.FlagVisibleBit;
        if (step == fps.Length) return IsRecipesContainer(parent) ? parent : 0;
        if (!Children(parent, out var first, out var n)) return 0;
        var target = fps[step] & ~visibleMask;
        for (var pass = 0; pass < 2; pass++)
        {
            var wantVisible = pass == 0;
            for (long i = 0; i < n; i++)
            {
                var child = Ptr(first + (nint)(i * 8));
                if (child == 0) continue;
                if (!_reader.TryReadStruct<uint>(child + Poe2.UiElement.Flags, out var flags)) continue;
                if ((flags & ~visibleMask) != target) continue;
                var visible = (flags & visibleMask) != 0;
                if (visible != wantVisible) continue;
                if (step == Poe2.Runeforge.GateStep && !visible) continue;
                var deeper = Walk(child, step + 1);
                if (deeper != 0)
                {
                    if (step == Poe2.Runeforge.ViewportStep) _viewport = child;
                    return deeper;
                }
            }
        }
        return 0;
    }

    private bool IsRecipesContainer(nint addr)
    {
        if (!Children(addr, out var first, out var n)) return false;
        for (long i = 0; i < n; i++)
        {
            var row = Ptr(first + (nint)(i * 8));
            if (row != 0 && !string.IsNullOrEmpty(ReadRowRewardText(row))) return true;
        }
        return false;
    }

    private nint FindTitleElement(nint uiRoot)
    {
        var queue = new Queue<nint>(); queue.Enqueue(uiRoot);
        var visited = new HashSet<nint>();
        while (queue.Count > 0 && visited.Count < 15000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (Children(el, out var f, out var nn))
                for (long k = 0; k < nn; k++) queue.Enqueue(Ptr(f + (nint)(k * 8)));
            var t = ReadStdWString(el + Poe2.UiElement.Text);
            if (t.Contains("Runeshape Combinations", StringComparison.OrdinalIgnoreCase)) return el;
        }
        return 0;
    }

    /// <summary>Fallback when flag fingerprints drift: locate the panel by its title text.</summary>
    private nint FindPanelByTitle(nint uiRoot, nint titleEl)
    {
        if (titleEl == 0) titleEl = FindTitleElement(uiRoot);
        if (titleEl == 0) return 0;

        var cur = titleEl;
        for (var up = 0; up < 10; up++)
        {
            var grid = FindRecipesGrid(cur);
            if (grid != 0) return grid;
            var parent = Ptr(cur + Poe2.UiElement.Parent);
            if (parent == 0) break;
            cur = parent;
        }
        return 0;
    }

    private nint FindRecipesGrid(nint parent)
    {
        if (!Children(parent, out var first, out var n)) return 0;
        nint best = 0; var bestRows = 0;
        for (long i = 0; i < n; i++)
        {
            var c = Ptr(first + (nint)(i * 8));
            if (!IsRecipesContainer(c)) continue;
            if (!Children(c, out _, out var rows)) continue;
            if (rows > bestRows) { best = c; bestRows = (int)rows; }
        }
        return best;
    }

    private bool TryScreenRect(nint row, NumVec2 scroll, float winW, float winH, out NumVec2 pos, out NumVec2 size)
    {
        pos = default; size = default;
        if (!_reader.TryReadStruct<byte>(row + Poe2.UiElement.UiScaleIndex, out var idx)) return false;
        _reader.TryReadStruct<float>(row + Poe2.UiElement.LocalScaleMul, out var mul);
        _reader.TryReadStruct<float>(row + Poe2.UiElement.SizeW, out var uw);
        _reader.TryReadStruct<float>(row + Poe2.UiElement.SizeH, out var uh);
        var (sw, sh) = ScaleValue(idx, mul, winW, winH);
        if (sw <= 0f || sh <= 0f) return false;
        var p = UnscaledPos(row, 0, scroll, winW, winH);
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return false;
        pos = new NumVec2(p.X * sw, p.Y * sh);
        size = new NumVec2(uw * sw, uh * sh);
        return size.X > 1f && size.Y > 1f;
    }

    /// <summary>Loot-tag style rect (no viewport scroll) — matches Poe2Live.TryUiElementRect.</summary>
    private bool TryUiRect(nint el, float winW, float winH, out float x, out float y, out float w, out float h)
    {
        x = y = w = h = 0f;
        if (el == 0 || !Visible(el)) return false;
        if (!_reader.TryReadStruct<byte>(el + Poe2.UiElement.UiScaleIndex, out var idx)) return false;
        _reader.TryReadStruct<float>(el + Poe2.UiElement.LocalScaleMul, out var mul);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeW, out var uw);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.SizeH, out var uh);
        var (sw, sh) = ScaleValue(idx, mul, winW, winH);
        if (sw <= 0f || sh <= 0f) return false;
        var (px, py) = UiUnscaledPos(el, 0, winW, winH);
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

    private (float x, float y) UiUnscaledPos(nint el, int depth, float winW, float winH)
    {
        _reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos, out var lx);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos + 4, out var ly);
        var parent = Ptr(el + Poe2.UiElement.Parent);
        if (parent == 0 || depth >= 64) return (lx, ly);

        var (ppx, ppy) = UiUnscaledPos(parent, depth + 1, winW, winH);
        if (_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)
            && (flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            _reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier, out var mx);
            _reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier + 4, out var my);
            ppx += mx; ppy += my;
        }
        return (ppx + lx, ppy + ly);
    }

    private NumVec2 UnscaledPos(nint el, int depth, NumVec2 scroll, float winW, float winH)
    {
        _reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos, out var lx);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.RelativePos + 4, out var ly);
        var local = new NumVec2(lx, ly);
        var parent = Ptr(el + Poe2.UiElement.Parent);
        if (parent == 0 || depth >= 64) return local;

        var parentPos = UnscaledPos(parent, depth + 1, scroll, winW, winH);

        if (_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)
            && (flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            _reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier, out var mx);
            _reader.TryReadStruct<float>(el + Poe2.UiElement.UiPositionModifier + 4, out var my);
            parentPos += new NumVec2(mx, my);
        }
        if (parent == _viewport) parentPos += scroll;

        _reader.TryReadStruct<byte>(el + Poe2.UiElement.UiScaleIndex, out var elIdx);
        _reader.TryReadStruct<float>(el + Poe2.UiElement.LocalScaleMul, out var elMul);
        _reader.TryReadStruct<byte>(parent + Poe2.UiElement.UiScaleIndex, out var pIdx);
        _reader.TryReadStruct<float>(parent + Poe2.UiElement.LocalScaleMul, out var pMul);
        if (pIdx == elIdx && pMul == elMul) return parentPos + local;

        var (psw, psh) = ScaleValue(pIdx, pMul, winW, winH);
        var (msw, msh) = ScaleValue(elIdx, elMul, winW, winH);
        if (msw == 0f || msh == 0f) return parentPos + local;
        return new NumVec2(parentPos.X * psw / msw + local.X, parentPos.Y * psh / msh + local.Y);
    }

    private NumVec2 ReadScroll(nint viewport)
    {
        if (viewport == 0) return NumVec2.Zero;
        _reader.TryReadStruct<float>(viewport + Poe2.Runeforge.ScrollOffset, out var x);
        _reader.TryReadStruct<float>(viewport + Poe2.Runeforge.ScrollOffset + 4, out var y);
        return new NumVec2(x, y);
    }

    private static void ParseNameCount(string raw, out int count, out string name)
    {
        count = 1; name = raw?.Trim() ?? "";
        if (name.Length == 0) return;
        var i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i > 0 && i < name.Length && (name[i] == 'x' || name[i] == 'X')
            && int.TryParse(name.AsSpan(0, i), out var c) && c > 0)
        { count = c; name = name[(i + 1)..].TrimStart(); }
    }

    private bool Visible(nint el)
        => _reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var f)
           && (f & (1u << Poe2.UiElement.FlagVisibleBit)) != 0;

    private bool Children(nint el, out nint first, out long n)
    {
        first = Ptr(el + Poe2.UiElement.Children); n = 0;
        if (first == 0) return false;
        if (!_reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 4000;
    }

    private nint Child(nint el, int index)
        => Children(el, out var first, out var n) && index >= 0 && index < n
            ? Ptr(first + (nint)(index * 8)) : 0;

    private string ReadStdWString(nint addr)
    {
        if (!_reader.TryReadStruct<long>(addr + 0x10, out var len) || len <= 0 || len > 1024) return "";
        if (len < 8) return _reader.ReadStringUtf16(addr, (int)len);
        var ptr = Ptr(addr);
        return ptr == 0 ? "" : _reader.ReadStringUtf16(ptr, (int)len);
    }

    private nint Ptr(nint addr)
    {
        if (!_reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
