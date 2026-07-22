namespace POE2Radar.Overlay.Pickup;

/// <summary>Fail-closed policy for automatic pickup: collect non-gear and never click equipment.</summary>
internal static class PickupItemPolicy
{
    private static readonly string[] GearRoots =
    [
        "Metadata/Items/Weapons/",
        "Metadata/Items/Armours/",
        "Metadata/Items/Rings/",
        "Metadata/Items/Amulets/",
        "Metadata/Items/Belts/",
        "Metadata/Items/Flasks/",
        "Metadata/Items/Charms/",
        "Metadata/Items/Equipment/",
    ];

    internal static bool ShouldPickup(string? itemMetadata)
    {
        if (string.IsNullOrWhiteSpace(itemMetadata) ||
            !itemMetadata.StartsWith("Metadata/Items/", StringComparison.Ordinal))
            return false;

        foreach (var root in GearRoots)
            if (itemMetadata.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }
}
