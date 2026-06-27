namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    private string _league = "";
    private nint _leagueFor = -1;

    /// <summary>Current league name (ServerData @ AreaInstance+ServerDataPtr -> std::wstring +0x21E0). Cached per area.</summary>
    public string LeagueName(nint areaInstance)
    {
        if (areaInstance == _leagueFor) return _league;
        _leagueFor = areaInstance;
        var serverData = Ptr(areaInstance + Poe2.AreaInstance.ServerDataPtr);
        _league = serverData == 0 ? "" : ReadStdWString(serverData + Poe2.ServerData.League);
        return _league;
    }

    /// <summary>Runeshape monolith station state read off the device entity (StateMachine → RuneStation).</summary>
    public readonly record struct MonolithState(
        bool Resolved, int HoleCount, int AnchorIdx, int AnchorPos, bool IsUnique, bool Collected);

    public MonolithState ReadMonolith(nint device)
    {
        var (_, collected) = ReadIcon(device);
        var fail = new MonolithState(false, 0, -1, -1, false, collected);

        var sm = ResolveComponent(device, "StateMachine");
        if (sm == 0) return fail;
        var first = Ptr(sm + Poe2.StateMachine.ListenerVec);
        if (first == 0 || !_reader.TryReadStruct<nint>(sm + Poe2.StateMachine.ListenerVec + 8, out var last)) return fail;
        var n = ((long)last - first) / 8;
        if (n is <= 0 or > 256) return fail;

        nint station = 0;
        for (long i = 0; i < n; i++)
        {
            var node = Ptr(first + (nint)(i * 8));
            if (node == 0) continue;
            var sub = Ptr(node);
            if (sub == 0) continue;
            var cand = sub - Poe2.RuneStation.ListenerSub;
            if (Ptr(cand + Poe2.RuneStation.Owner) == device) { station = cand; break; }
        }
        if (station == 0) return fail;

        if (!_reader.TryReadStruct<int>(station + Poe2.RuneStation.HoleCount, out var holes) || holes is <= 0 or > 16)
            return fail;
        _reader.TryReadStruct<int>(station + Poe2.RuneStation.AnchorPos, out var pos);

        var rowPtr = Ptr(station + Poe2.RuneStation.AnchorRef);
        if (rowPtr == 0) return new MonolithState(true, holes, -1, -1, true, collected);

        var holder = Ptr(station + Poe2.RuneStation.AnchorHolder);
        var p1 = Ptr(holder + 0x28);
        if (p1 == 0 || !_reader.TryReadStruct<long>(p1, out var tableBase) || tableBase == 0)
            return new MonolithState(true, holes, -1, pos, false, collected);
        var delta = (long)rowPtr - tableBase;
        var idx = (delta >= 0 && delta % Poe2.RuneStation.RuneStride == 0)
            ? (int)(delta / Poe2.RuneStation.RuneStride) : -1;
        if (idx < 0 || idx >= Poe2.RuneStation.RuneCount) idx = -1;
        return new MonolithState(true, holes, idx, pos, false, collected);
    }

    private (Rarity, string?, bool, string?) ReadItemIdentity(nint entity)
    {
        if (_itemIdent.TryGetValue(entity, out var cached)) return cached;
        if (_itemReadBudget <= 0) return (Rarity.NonMonster, null, true, null);

        var wi = ResolveComponent(entity, "WorldItem");
        var item = wi == 0 ? 0 : Ptr(wi + Poe2.WorldItemComponent.ItemEntity);
        if (item == 0) { var v = (Rarity.NonMonster, (string?)null, true, (string?)null); _itemIdent[entity] = v; return v; }
        _itemReadBudget--;

        var result0 = ReadIdentityFromItem(item);
        _itemIdent[entity] = result0;
        return result0;
    }

    private (Rarity, string?, bool, string?) ReadIdentityFromItem(nint item)
    {
        var rarity = Rarity.NonMonster;
        var identified = true;
        var modsComp = ResolveComponent(item, "Mods");
        if (modsComp != 0)
        {
            if (_reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Rarity, out var r) && r is >= 0 and <= 3)
                rarity = (Rarity)r;
            if (_reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Identified, out var idf))
                identified = idf != 0;
        }

        string? art = null;
        var renderItem = ResolveComponent(item, "RenderItem");
        if (renderItem != 0)
        {
            var pathPtr = Ptr(renderItem + Poe2.RenderItemComponent.ResourcePath);
            if (pathPtr != 0)
            {
                var full = _reader.ReadStringUtf16(pathPtr, 128);
                art = ArtBasename(full);
            }
        }

        string? name = null;
        var baseComp = ResolveComponent(item, "Base");
        if (baseComp != 0)
        {
            var nameRow = Ptr(baseComp + Poe2.BaseComponent.NameRow);
            var namePtr = nameRow == 0 ? 0 : Ptr(nameRow + Poe2.BaseComponent.RowDisplayName);
            if (namePtr != 0)
            {
                var s = _reader.ReadStringUtf16(namePtr, 64);
                if (!string.IsNullOrWhiteSpace(s)) name = s.Trim();
            }
        }

        return (rarity, art, identified, name);
    }

    private static string? ArtBasename(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var slash = path.LastIndexOf('/');
        var start = slash >= 0 ? slash + 1 : 0;
        var dot = path.LastIndexOf('.');
        var end = dot > start ? dot : path.Length;
        if (end <= start) return null;
        var name = path[start..end];
        return name.Length >= 2 ? name : null;
    }

    public bool TryUiElementRect(nint el, float winW, float winH, out float x, out float y, out float w, out float h,
        string? requireFirstLine = null)
    {
        x = y = w = h = 0f;
        if (el == 0) return false;
        if (!_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)) return false;
        if ((flags & (1u << Poe2.UiElement.FlagVisibleBit)) == 0) return false;
        if (requireFirstLine is { Length: > 0 })
        {
            var t = ReadStdWString(el + Poe2.UiElement.Text);
            var nl = t.IndexOf('\n');
            if (!string.Equals((nl >= 0 ? t[..nl] : t).Trim(), requireFirstLine, StringComparison.Ordinal)) return false;
        }
        if (!_reader.TryReadStruct<byte>(el + Poe2.UiElement.UiScaleIndex, out var idx)) return false;
        _reader.TryReadStruct<float>(el + Poe2.UiElement.LocalScaleMul, out var mul);
        _reader.TryReadStruct<System.Numerics.Vector2>(el + Poe2.UiElement.SizeW, out var sz);
        var (sw, sh) = UiScaleValue(idx, mul, winW, winH);
        if (sw <= 0f || sh <= 0f) return false;
        var (px, py) = UiUnscaledPos(el, 0, winW, winH);
        if (!float.IsFinite(px) || !float.IsFinite(py)) return false;
        x = px * sw; y = py * sh; w = sz.X * sw; h = sz.Y * sh;
        return w > 1f && h > 1f;
    }

    private static (float w, float h) UiScaleValue(byte idx, float mul, float winW, float winH)
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
        _reader.TryReadStruct<System.Numerics.Vector2>(el + Poe2.UiElement.RelativePos, out var rel);
        var parent = Ptr(el + Poe2.UiElement.Parent);
        if (parent == 0 || depth >= 64) return (rel.X, rel.Y);

        var (ppx, ppy) = UiUnscaledPos(parent, depth + 1, winW, winH);

        if (_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags)
            && (flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            _reader.TryReadStruct<System.Numerics.Vector2>(el + Poe2.UiElement.UiPositionModifier, out var mod);
            ppx += mod.X; ppy += mod.Y;
        }
        return (ppx + rel.X, ppy + rel.Y);
    }

    public readonly record struct RitualReward(
        Rarity Rarity, string? Art, string? Name, bool Identified, float X, float Y, float W, float H);

    public List<RitualReward> ReadRitualRewards(nint inGameState, float winW, float winH)
    {
        var result = new List<RitualReward>();
        var uiRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        if (uiRoot == 0) return result;
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;

        nint sigEl = 0;
        var queue = new Queue<nint>(); queue.Enqueue(uiRoot);
        var visited = new HashSet<nint>();
        while (queue.Count > 0 && visited.Count < 20000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            var visible = _reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) && (flags & visBit) != 0;
            if (!visible && el != uiRoot) continue;
            if (ChildSpan(el, out var f, out var nn))
                for (long k = 0; k < nn; k++) queue.Enqueue(Ptr(f + (nint)(k * 8)));
            if (sigEl == 0)
            {
                var t = ReadStdWString(el + Poe2.UiElement.Text);
                if (t.Length >= 6 && (t.Contains("Rituals Remaining", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("tribute to the king", StringComparison.OrdinalIgnoreCase)))
                    sigEl = el;
            }
        }
        if (sigEl == 0) return result;

        var cur = sigEl;
        nint grid = 0;
        for (var up = 0; up < 8 && grid == 0; up++)
        {
            grid = FindRewardGrid(cur);
            var parent = Ptr(cur + Poe2.UiElement.Parent);
            if (parent == 0) break;
            cur = parent;
        }
        if (grid == 0 || !ChildSpan(grid, out var gf, out var gn)) return result;

        for (long i = 0; i < gn; i++)
        {
            var tile = Ptr(gf + (nint)(i * 8));
            var item = TileItem(tile);
            if (item == 0) continue;
            var (rarity, art, identified, name) = ReadIdentityFromItem(item);
            if (!TryUiElementRect(tile, winW, winH, out var x, out var y, out var w, out var h)) continue;
            result.Add(new RitualReward(rarity, art, name, identified, x, y, w, h));
        }
        return result;
    }

    private nint FindRewardGrid(nint parent)
    {
        if (!ChildSpan(parent, out var first, out var n)) return 0;
        nint best = 0; var bestItems = 0;
        for (long i = 0; i < n; i++)
        {
            var c = Ptr(first + (nint)(i * 8));
            if (!ChildSpan(c, out var cf, out var cn) || cn is < 1 or > 16) continue;
            var items = 0;
            for (long k = 0; k < cn; k++) if (TileItem(Ptr(cf + (nint)(k * 8))) != 0) items++;
            if (items >= 2 && items > bestItems && items * 2 >= cn) { best = c; bestItems = items; }
        }
        return best;
    }

    private nint TileItem(nint tile)
    {
        if (tile == 0) return 0;
        var item = Ptr(tile + Poe2.Ritual.TileSlotItem);
        return item != 0 && ResolveComponent(item, "RenderItem") != 0 ? item : 0;
    }

    private bool ChildSpan(nint el, out nint first, out long n)
    {
        first = Ptr(el + Poe2.UiElement.Children); n = 0;
        if (first == 0) return false;
        if (!_reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 4000;
    }

    public List<(nint El, string Text)> ScanLootLabels(nint inGameState, int maxNodes = 20000)
    {
        var result = new List<(nint, string)>();
        var uiRoot = PreferredUiScanRoot(inGameState);
        if (uiRoot == 0) return result;
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;

        var queue = new Queue<nint>(); queue.Enqueue(uiRoot);
        var visited = new HashSet<nint>();
        while (queue.Count > 0 && visited.Count < maxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            var visible = _reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) && (flags & visBit) != 0;
            if (!visible && el != uiRoot) continue;

            var first = Ptr(el + Poe2.UiElement.Children);
            if (first != 0 && _reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last))
            {
                var n = ((long)last - (long)first) / 8;
                if (n is > 0 and <= 8192)
                    for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
            }

            var text = ReadStdWString(el + Poe2.UiElement.Text);
            if (text.Length < 2) continue;
            var nl = text.IndexOf('\n');
            var firstLine = (nl >= 0 ? text[..nl] : text).Trim();
            if (firstLine.Length >= 2) result.Add((el, firstLine));
        }
        return result;
    }
}
