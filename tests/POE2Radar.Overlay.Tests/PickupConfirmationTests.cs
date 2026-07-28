using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupConfirmationTests
{
    [Fact]
    public void ConfirmationSet_IncludesGroundItemsRejectedBySelectionPolicy()
    {
        var address = (nint)0x1234;
        var item = new Poe2Live.EntityDot(
            7,
            address,
            default,
            default,
            0f,
            Poe2Live.EntityCategory.Other,
            "Metadata/MiscellaneousObjects/WorldItem",
            0,
            0,
            false,
            0,
            Poe2Live.Rarity.NonMonster,
            false,
            ItemMetadata: null);

        var confirmation = RadarApp.BuildPickupConfirmationSet([item]);

        Assert.Contains(address, confirmation);
    }
}
