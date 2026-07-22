using POE2Radar.Overlay.Pickup;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupItemPolicyTests
{
    [Theory]
    [InlineData("Metadata/Items/Weapons/OneHandWeapons/Swords/Sword1")]
    [InlineData("Metadata/Items/Armours/BodyArmours/BodyStr1")]
    [InlineData("Metadata/Items/Armours/Shields/ShieldStr1")]
    [InlineData("Metadata/Items/Rings/Ring1")]
    [InlineData("Metadata/Items/Amulets/Amulet1")]
    [InlineData("Metadata/Items/Belts/Belt1")]
    [InlineData("Metadata/Items/Flasks/LifeFlask1")]
    [InlineData("Metadata/Items/Charms/Charm1")]
    public void ShouldPickup_RejectsEquippableGear(string metadata)
        => Assert.False(PickupItemPolicy.ShouldPickup(metadata));

    [Theory]
    [InlineData("Metadata/Items/Currency/CurrencyUpgradeToRare")]
    [InlineData("Metadata/Items/Maps/Waystone16")]
    [InlineData("Metadata/Items/MapFragments/ExpeditionLogbook")]
    [InlineData("Metadata/Items/Gems/SkillGemUncut20")]
    [InlineData("Metadata/Items/Jewels/JewelInt1")]
    [InlineData("Metadata/Items/Tablets/TabletRitual")]
    [InlineData("Metadata/Items/Runes/RuneFire1")]
    public void ShouldPickup_AllowsNonGearLoot(string metadata)
        => Assert.True(PickupItemPolicy.ShouldPickup(metadata));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Metadata/MiscellaneousObjects/WorldItem")]
    public void ShouldPickup_FailsClosedWhenInnerItemIdentityIsUnavailable(string? metadata)
        => Assert.False(PickupItemPolicy.ShouldPickup(metadata));
}
