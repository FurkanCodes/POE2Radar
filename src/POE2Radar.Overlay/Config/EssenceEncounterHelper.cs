using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Essence encounter detection — frozen mobs, monolith POIs, and cluster dedupe.</summary>
public static class EssenceEncounterHelper
{
    /// <summary>Grid radius to treat Map-marker POI + imprisoned rare as one Essence encounter.</summary>
    public const float ClusterRadiusGrid = 18f;

    private static readonly string[] ObjectTokens =
    [
        "essencestartcalm",
        "essencestart",
        "/essence/",
        "essencemonolith",
        "essenceencounter",
        "/essencemoddaemons/",
    ];

    /// <summary>POI / object paths that are never Essence anchors (waypoints, league mechanics, NPCs).</summary>
    private static readonly string[] NonEssencePoiTokens =
    [
        "Waypoint", "TownPortal", "Portal", "Stash", "Quest", "/npc/",
        "Expedition2", "LeagueRitual", "RitualAltar", "Breach", "Abyss", "Delirium",
        "StrongBox", "Shrine", "SummoningCircle", "AzmeriSpirit", "RogueExile",
        "Checkpoint", "Bridge", "Transition", "Multiplex",
    ];

    public static bool MetadataMatches(string metadata)
        => EndgameMechanicCatalog.TryMatchMetadata(metadata, Poe2Live.EntityCategory.Other, out var d)
           && string.Equals(d!.Name, "Essence", StringComparison.OrdinalIgnoreCase);

    public static bool HasEssenceObjectMetadata(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        foreach (var t in ObjectTokens)
            if (metadata.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsEssenceAnchor(Poe2Live.EntityDot e)
    {
        if (MetadataMatches(e.Metadata) || HasEssenceObjectMetadata(e.Metadata)) return true;
        return IsGenericMapMarkerPoi(e);
    }

    public static bool IsEssenceClusterMember(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot> peers)
    {
        if (IsEssenceAnchor(e)) return true;

        foreach (var p in peers)
        {
            if (p.Id == e.Id) continue;
            if (!WithinCluster(e, p)) continue;
            if (IsEssenceAnchor(p)) return true;
        }

        if (IsImprisonedRare(e) && HasCoLocatedGenericMapMarkerPoi(e, peers)) return true;
        if (IsGenericMapMarkerPoi(e) && HasCoLocatedImprisonedRare(e, peers)) return true;

        return false;
    }

    public static bool ShouldPromoteToEssence(DisplayRule? rule)
    {
        if (rule is not { Name: { Length: > 0 } n }) return false;
        return string.Equals(n, "Map marker", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "Point of Interest", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "Monster · Rare", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "Rare", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hide duplicate Map-marker POI when a co-located rare is the real Essence target.</summary>
    public static bool ShouldHidePoiDuplicate(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot> peers)
    {
        if (!IsGenericMapMarkerPoi(e)) return false;
        return HasCoLocatedImprisonedRare(e, peers);
    }

    private static bool IsGenericMapMarkerPoi(Poe2Live.EntityDot e)
    {
        if (!e.Poi) return false;
        if (e.Category is not (Poe2Live.EntityCategory.Object or Poe2Live.EntityCategory.Other)) return false;
        if (EndgameMechanicCatalog.MatchesNonEssenceMechanic(e)) return false;
        var md = e.Metadata;
        foreach (var t in NonEssencePoiTokens)
            if (md.Contains(t, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IsImprisonedRare(Poe2Live.EntityDot e)
        => e.Category == Poe2Live.EntityCategory.Monster && e.Rarity == Poe2Live.Rarity.Rare;

    private static bool HasCoLocatedGenericMapMarkerPoi(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot> peers)
    {
        foreach (var p in peers)
        {
            if (p.Id == e.Id) continue;
            if (!WithinCluster(e, p)) continue;
            if (IsGenericMapMarkerPoi(p)) return true;
        }
        return false;
    }

    private static bool HasCoLocatedImprisonedRare(Poe2Live.EntityDot e, IReadOnlyList<Poe2Live.EntityDot> peers)
    {
        foreach (var p in peers)
        {
            if (p.Id == e.Id) continue;
            if (!WithinCluster(e, p)) continue;
            if (IsImprisonedRare(p)) return true;
        }
        return false;
    }

    private static bool WithinCluster(Poe2Live.EntityDot a, Poe2Live.EntityDot b)
        => System.Numerics.Vector2.Distance(a.Grid, b.Grid) <= ClusterRadiusGrid;
}
