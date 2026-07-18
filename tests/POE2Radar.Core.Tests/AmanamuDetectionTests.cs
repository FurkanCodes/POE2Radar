using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AmanamuDetectionTests
{
    [Theory]
    [InlineData("MonsterAbyssLightlessFaction1")]
    [InlineData("Metadata/Monsters/MonsterMods/LeagueAbyss/LightlessWells")]
    [InlineData("Metadata/Monsters/LeagueAbyss/MonsterAbyssLightlessRare")]
    public void IdentityMatcher_AcceptsKnownAbyssIdentifiers(string value)
        => Assert.True(Poe2Live.IsAmanamuIdentity(value));

    [Theory]
    [InlineData("")]
    [InlineData("MonsterAbyssFaction1")]
    [InlineData("Metadata/Monsters/LeagueAbyss/Generic")]
    public void IdentityMatcher_RejectsUnrelatedIdentifiers(string value)
        => Assert.False(Poe2Live.IsAmanamuIdentity(value));

    [Theory]
    [InlineData("abyss_lightless_well")]
    [InlineData("abyss_lightless_well_immune")]
    [InlineData("ABYSS_LIGHTLESS_WELL_OTHER")]
    public void BuffMatcher_AcceptsLightlessWellFamily(string value)
        => Assert.True(Poe2Live.IsAmanamuBuff(value));

    [Fact]
    public void CloudMatcher_OnlyAcceptsImmunityBuff()
    {
        Assert.True(Poe2Live.IsAmanamuInsideCloudBuff("abyss_lightless_well_immune"));
        Assert.False(Poe2Live.IsAmanamuInsideCloudBuff("abyss_lightless_well"));
    }

    [Fact]
    public void BuffOffsets_MatchGameHelperLayout()
    {
        Assert.Equal(0x160, Poe2.Buffs.StatusEffects);
        Assert.Equal(0x08, Poe2.StatusEffect.BuffDefinition);
        Assert.Equal(0x00, Poe2.StatusEffect.BuffDefinitionName);
        Assert.Equal(0x08, Poe2.StatusEffect.PointerStride);
    }
}
