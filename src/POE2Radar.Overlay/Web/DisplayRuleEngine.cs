using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Web;

/// <summary>Merges global <see cref="DisplayRules"/> with per-zone-type overrides and the
/// <see cref="RadarSettings.ImportantOnly"/> trash filter into one resolve path for draw, nav, and labels.</summary>
public sealed class DisplayRuleEngine
{
    private readonly DisplayRules _global;
    private readonly ZoneEntityOverrides _zoneOverrides;
    private readonly RadarStyles _styles;
    private static readonly DisplayRule HiddenTrash = new() { Hide = true, Name = "(hidden)" };

    public DisplayRuleEngine(DisplayRules global, ZoneEntityOverrides zoneOverrides, RadarStyles styles)
    {
        _global = global;
        _zoneOverrides = zoneOverrides;
        _styles = styles;
    }

    public int Generation => _global.Generation + _zoneOverrides.Generation;

    /// <summary>Global rule only (state hides + semantic rules), without zone overrides or ImportantOnly.</summary>
    public DisplayRule? ResolveGlobal(Poe2Live.EntityDot e)
    {
        var state = _global.ResolveStateHide(e);
        if (state != null) return state;
        if (EndgameMechanicCatalog.TryMatch(e, out var mechanic))
            return EndgameMechanicCatalog.ToDisplayRule(mechanic!);
        return _global.ResolveContent(e);
    }

    /// <summary>Full merged resolve: state hides → catalog mechanics → zone override patch → global rules → ImportantOnly trash.</summary>
    public DisplayRule? Resolve(Poe2Live.EntityDot e, string areaCode, bool importantOnly)
    {
        var state = _global.ResolveStateHide(e);
        if (state != null) return state;

        var token = EntityDisplayHelper.TypeToken(e.Metadata);
        var zoneOv = _zoneOverrides.GetOverride(areaCode, token);

        // Catalog mechanics beat the Map-marker POI catch-all regardless of display_rules.json order.
        DisplayRule? global = null;
        if (EndgameMechanicCatalog.TryMatch(e, out var mechanic))
            global = EndgameMechanicCatalog.ToDisplayRule(mechanic!);
        global ??= _global.ResolveContent(e);

        if (zoneOv != null)
        {
            if (global != null)
                return Patch(global, zoneOv);
            if (zoneOv.Hide == true)
                return HiddenTrash;
            return new DisplayRule
            {
                Name = token,
                Enabled = true,
                Hide = false,
                Navigable = zoneOv.Navigable ?? false,
                Shape = "Circle",
                Color = "#FFFFFF",
                Opacity = 0.95f,
                Size = 6f,
            };
        }

        if (global != null)
        {
            if (importantOnly && !global.Hide && IsTrash(e))
                return HiddenTrash;
            return global;
        }

        if (importantOnly && IsTrash(e))
            return HiddenTrash;
        return null;
    }

    private bool IsTrash(Poe2Live.EntityDot e)
        => EntityImportanceHelper.IsTrash(EntityImportanceHelper.Classify(e, _styles));

    private static DisplayRule Patch(DisplayRule global, ZoneEntityOverride zoneOv)
    {
        var r = CloneRule(global);
        if (zoneOv.Hide.HasValue) r.Hide = zoneOv.Hide.Value;
        if (zoneOv.Navigable.HasValue) r.Navigable = zoneOv.Navigable.Value;
        return r;
    }

    private static DisplayRule CloneRule(DisplayRule r) => new()
    {
        Enabled = r.Enabled,
        Name = r.Name,
        Categories = new(r.Categories),
        Match = new(r.Match),
        Rarity = r.Rarity,
        Reaction = r.Reaction,
        Life = r.Life,
        Chest = r.Chest,
        Poi = r.Poi,
        Encounter = r.Encounter,
        Hide = r.Hide,
        Shape = r.Shape,
        Color = r.Color,
        Opacity = r.Opacity,
        Size = r.Size,
        Sprite = r.Sprite?.Clone(),
        Label = r.Label,
        Navigable = r.Navigable,
    };
}
