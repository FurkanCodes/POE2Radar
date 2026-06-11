using POE2Radar.Core.Game;

namespace POE2Radar.Overlay.Config;

/// <summary>Opened-chest hide exemptions for high-value strongbox types (Unique / Landmark stay on map).</summary>
public static class StrongboxHidePolicy
{
    private static readonly string[] KeepOpenedVisibleNames =
        ["Strongbox · Unique", "Strongbox · Landmark"];

    public static bool ShouldKeepOpenedVisible(string metadata, Poe2Live.EntityCategory category)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        foreach (var def in EndgameMechanicCatalog.All)
        {
            foreach (var name in KeepOpenedVisibleNames)
                if (string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase)
                    && EndgameMechanicCatalog.Matches(metadata, category, def))
                    return true;
        }
        return false;
    }
}
