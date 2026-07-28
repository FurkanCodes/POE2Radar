using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pickup;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupItemPolicyTests
{
    private readonly PickupPolicy _policy = new();
    private readonly PickupPolicySettings _settings = new();

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
    {
        var decision = Evaluate(metadata);

        Assert.False(decision.Eligible);
        Assert.Equal(PickupPolicyReason.EquipmentDisabled, decision.Reason);
    }

    [Theory]
    [InlineData("Metadata/Items/Currency/CurrencyUpgradeToRare")]
    [InlineData("Metadata/Items/Maps/Waystone16")]
    [InlineData("Metadata/Items/MapFragments/ExpeditionLogbook")]
    [InlineData("Metadata/Items/Gems/SkillGemUncut20")]
    [InlineData("Metadata/Items/Jewels/JewelInt1")]
    [InlineData("Metadata/Items/Tablets/TabletRitual")]
    [InlineData("Metadata/Items/Runes/RuneFire1")]
    public void ShouldPickup_AllowsNonGearLoot(string metadata)
    {
        var decision = Evaluate(metadata);

        Assert.True(decision.Eligible);
        Assert.Equal(PickupPolicyReason.Allowed, decision.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Metadata/MiscellaneousObjects/WorldItem")]
    public void ShouldPickup_FailsClosedWhenInnerItemIdentityIsUnavailable(string? metadata)
    {
        var decision = Evaluate(metadata);

        Assert.False(decision.Eligible);
        Assert.Equal(PickupPolicyReason.IdentityUnavailable, decision.Reason);
    }

    [Fact]
    public void AllowPatterns_WhenConfigured_RequireAtLeastOneNameOrMetadataMatch()
    {
        _settings.AllowPatterns = "Divine Orb, Metadata/Items/Maps/";

        Assert.True(Evaluate("Metadata/Items/Currency/CurrencyDivine", "Divine Orb").Eligible);
        Assert.True(Evaluate("Metadata/Items/Maps/Waystone16", "Waystone (Tier 16)").Eligible);
        Assert.Equal(
            PickupPolicyReason.NotAllowListed,
            Evaluate("Metadata/Items/Currency/CurrencyUpgradeToRare", "Exalted Orb").Reason);
    }

    [Fact]
    public void DenyPatterns_WinOverAllowAndPriorityPatterns()
    {
        _settings.AllowPatterns = "Waystone";
        _settings.DenyPatterns = "Corrupted";
        _settings.PriorityPatterns = "Waystone";

        var decision = Evaluate("Metadata/Items/Maps/Waystone16Corrupted", "Corrupted Waystone");

        Assert.False(decision.Eligible);
        Assert.Equal(PickupPolicyReason.DenyListed, decision.Reason);
    }

    [Fact]
    public void Equipment_RequiresExplicitOptInEvenWhenAllowPatternMatches()
    {
        _settings.AllowPatterns = "Astral Plate";
        var metadata = "Metadata/Items/Armours/BodyArmours/BodyStr1";

        Assert.Equal(PickupPolicyReason.EquipmentDisabled, Evaluate(metadata, "Astral Plate").Reason);

        _settings.AllowEquipment = true;

        Assert.True(Evaluate(metadata, "Astral Plate").Eligible);
    }

    [Fact]
    public void PriorityPatterns_UseTheirConfiguredOrder()
    {
        _settings.PriorityPatterns = "Divine Orb, Waystone, Currency";

        var divine = Evaluate("Metadata/Items/Currency/CurrencyDivine", "Divine Orb");
        var waystone = Evaluate("Metadata/Items/Maps/Waystone16", "Waystone (Tier 16)");
        var ordinary = Evaluate("Metadata/Items/Currency/CurrencyUpgradeToRare", "Exalted Orb");

        Assert.True(divine.Priority > waystone.Priority);
        Assert.True(waystone.Priority > ordinary.Priority);
    }

    private PickupPolicyDecision Evaluate(string? metadata, string? name = null)
        => _policy.Evaluate(new PickupPolicyCandidate(metadata, name), _settings);
}
