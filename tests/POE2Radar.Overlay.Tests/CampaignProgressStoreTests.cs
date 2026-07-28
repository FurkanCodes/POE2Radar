using POE2Radar.Overlay.Campaign;
using System.Text;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class CampaignProgressStoreTests
{
    [Fact]
    public void Progress_IsCharacterIsolatedAndPersistsNoRawIdentity()
    {
        var directory = Directory.CreateTempSubdirectory("poe2radar-campaign-");
        try
        {
            var file = Path.Combine(directory.FullName, "progress.json");
            var first = CampaignProgressStore.HashIdentity("Standard", "RawCharacterOne");
            var second = CampaignProgressStore.HashIdentity("Standard", "RawCharacterTwo");
            var store = new CampaignProgressStore(file);

            store.SetComplete(first, "act1.test", true);
            store.SetDismissed(first, true);

            Assert.True(store.IsComplete(first, "act1.test"));
            Assert.False(store.IsComplete(second, "act1.test"));
            var reloaded = new CampaignProgressStore(file);
            Assert.True(reloaded.IsComplete(first, "act1.test"));
            Assert.True(reloaded.Snapshot(first).Dismissed);
            var serialized = File.ReadAllText(file);
            Assert.DoesNotContain("RawCharacterOne", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("Standard", serialized, StringComparison.Ordinal);
            Assert.Contains(first, serialized, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void CorruptProgress_RecoversAndCanSaveAtomically()
    {
        var directory = Directory.CreateTempSubdirectory("poe2radar-campaign-");
        try
        {
            var file = Path.Combine(directory.FullName, "progress.json");
            File.WriteAllText(file, "{not-json");
            var store = new CampaignProgressStore(file);
            var profile = CampaignProgressStore.HashIdentity("League", "Character");

            store.SetComplete(profile, "act1.recovered", true);

            Assert.True(new CampaignProgressStore(file).IsComplete(profile, "act1.recovered"));
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void PortableCode_RoundTripsWithoutCharacterIdentity()
    {
        var code = CampaignProgressCodec.Encode(
            "guide-v1",
            ["act1.first", "act1.second", "act1.first"]);

        Assert.True(CampaignProgressCodec.TryDecode(code, out var completed));
        Assert.Equal(["act1.first", "act1.second"], completed);
        Assert.DoesNotContain("Character", code, StringComparison.Ordinal);
        Assert.False(CampaignProgressCodec.TryDecode("not-a-progress-code", out _));
    }

    [Fact]
    public void WebsiteProgressCode_MapsStorageKeysToSourceRows()
    {
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                """{"poe2-act1-v05":["1","66"],"poe2-interludes-v06":["7"]}"""));

        Assert.True(CampaignProgressCodec.TryDecodeWebsite(
            "PoE2v05_" + payload,
            out var rows));
        Assert.Contains(rows, x => x.Chapter == "act1" && x.Row == 1);
        Assert.Contains(rows, x => x.Chapter == "act1" && x.Row == 66);
        Assert.Contains(rows, x => x.Chapter == "interludes" && x.Row == 7);
    }
}
