using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Web;

/// <summary>Merges global <see cref="DisplayRules"/> with per-zone-type overrides and the
/// <see cref="RadarSettings.ImportantOnly"/> trash filter into one resolve path for draw, nav, and labels.</summary>
public sealed class DisplayRuleEngine
{
    private readonly DisplayRules _global;
    private readonly ZoneEntityOverrides _zoneOverrides;
    private readonly Func<RadarStyles> _getStyles;
    private static readonly DisplayRule HiddenTrash = new() { Hide = true, Name = "(hidden)" };

    /// <param name="getStyles">Live accessor — dashboard POST replaces <c>_settings.Styles</c> wholesale.</param>
    public DisplayRuleEngine(DisplayRules global, ZoneEntityOverrides zoneOverrides, Func<RadarStyles> getStyles)
    {
        _global = global;
        _zoneOverrides = zoneOverrides;
        _getStyles = getStyles;
    }

    private RadarStyles Styles => _getStyles();

    public int Generation => _global.Generation + _zoneOverrides.Generation;

    /// <summary>Global rule only (state hides + semantic rules), without zone overrides or ImportantOnly.</summary>
    public DisplayRule? ResolveGlobal(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot>? peers = null)
    {
        var state = ApplyOpenedChestExemption(e, _global.ResolveStateHide(e));
        if (state != null) return state;
        return FinalizeEssence(e, _global.ResolveContent(e), peers);
    }

    /// <summary>Full merged resolve: state hides → display_rules (first match) → zone override → ImportantOnly trash.</summary>
    public DisplayRule? Resolve(
        Poe2Live.EntityDot e,
        string areaCode,
        bool importantOnly,
        IReadOnlyList<Poe2Live.EntityDot>? peers = null)
    {
        var state = ApplyOpenedChestExemption(e, _global.ResolveStateHide(e));
        if (state != null) return state;

        var token = EntityDisplayHelper.TypeToken(e.Metadata);
        var zoneOv = _zoneOverrides.GetOverride(areaCode, token);

        DisplayRule? global = FinalizeEssence(e, _global.ResolveContent(e), peers);

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
            if (importantOnly && !global.Hide && IsTrash(e, peers))
                return HiddenTrash;
            return global;
        }

        if (importantOnly && IsTrash(e, peers))
            return HiddenTrash;
        return null;
    }

    private bool IsTrash(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot>? peers)
        => EntityImportanceHelper.IsTrash(EntityImportanceHelper.Classify(e, Styles, FinalizeEssence(e, _global.ResolveContent(e), peers)));

    private static DisplayRule Patch(DisplayRule global, ZoneEntityOverride zoneOv)
    {
        var r = CloneRule(global);
        if (zoneOv.Hide.HasValue) r.Hide = zoneOv.Hide.Value;
        if (zoneOv.Navigable.HasValue) r.Navigable = zoneOv.Navigable.Value;
        return r;
    }

    private DisplayRule? FinalizeEssence(
        Poe2Live.EntityDot e,
        DisplayRule? rule,
        IReadOnlyList<Poe2Live.EntityDot>? peers)
    {
        var peerList = peers ?? Array.Empty<Poe2Live.EntityDot>();
        rule = PromoteEssenceCluster(e, rule, peerList);
        if (rule is { Name: var n }
            && string.Equals(n, "Essence", StringComparison.OrdinalIgnoreCase)
            && EssenceEncounterHelper.ShouldHidePoiDuplicate(e, peerList))
            return HiddenTrash;
        return rule;
    }

    private DisplayRule? PromoteEssenceCluster(
        Poe2Live.EntityDot e,
        DisplayRule? rule,
        IReadOnlyList<Poe2Live.EntityDot> peers)
    {
        if (rule is null || !EssenceEncounterHelper.ShouldPromoteToEssence(rule)) return rule;
        if (!EssenceEncounterHelper.IsEssenceClusterMember(e, peers)) return rule;
        return EssenceDisplayRule();
    }

    private DisplayRule EssenceDisplayRule()
    {
        foreach (var r in _global.All)
            if (string.Equals(r.Name, "Essence", StringComparison.OrdinalIgnoreCase))
                return r;
        foreach (var def in EndgameMechanicCatalog.All)
            if (string.Equals(def.Name, "Essence", StringComparison.OrdinalIgnoreCase))
                return EndgameMechanicCatalog.ToDisplayRule(def);
        return new DisplayRule { Name = "Essence", Label = "Essence", Enabled = true, Navigable = false };
    }

    private static DisplayRule? ApplyOpenedChestExemption(Poe2Live.EntityDot e, DisplayRule? state)
    {
        if (state is { Hide: true, Chest: "Opened" }
            && StrongboxHidePolicy.ShouldKeepOpenedVisible(e.Metadata, e.Category))
            return null;
        return state;
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
