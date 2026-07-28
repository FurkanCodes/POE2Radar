using System.Text;
using POE2Radar.Core.Campaign;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class CampaignCatalogTests
{
    [Fact]
    public void EmbeddedCatalog_CoversEverySourceRowAcrossAllActsAndInterludes()
    {
        var catalog = CampaignCatalog.Shared;

        Assert.Equal("90739c2", catalog.SourceCommit);
        Assert.Equal(333, catalog.SourceRowCount);
        Assert.Equal(333, catalog.SourceChapters.Sum(x => x.RowCount));
        Assert.All(catalog.SourceChapters, chapter => Assert.True(chapter.Implemented));
        Assert.True(catalog.ForChapter("act1").Count > 66);
        Assert.True(catalog.ForChapter("act2").Count >= 78);
        Assert.True(catalog.ForChapter("act3").Count >= 86);
        Assert.True(catalog.ForChapter("act4").Count >= 67);
        Assert.True(catalog.ForChapter("interludes").Count >= 36);
        Assert.Equal(
            Enumerable.Range(1, 66),
            catalog.ForChapter("act1")
                .SelectMany(x => x.Source)
                .Select(x => x.Row)
                .Distinct()
                .Order());
        Assert.Equal(
            333,
            catalog.Objectives
                .SelectMany(x => x.Source)
                .Select(x => (x.Chapter, x.Row))
                .Distinct()
                .Count());
        Assert.Equal(
            "interlude-5.3",
            catalog.ForChapter("interludes").OrderBy(x => x.Order).First().Branch);
        Assert.True(catalog.IsCampaignArea("G4_3_1"));
        Assert.True(catalog.IsCampaignArea("P3_1"));
        Assert.False(catalog.IsCampaignArea("G4_WorldMap"));
        Assert.False(catalog.IsCampaignArea("G4_12"));
        Assert.Equal(
            catalog.Objectives.Count,
            catalog.Objectives.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(55, catalog.Objectives.Count(x => !string.IsNullOrWhiteSpace(x.Note)));
        Assert.All(
            catalog.SourceChapters,
            chapter =>
            {
                var objectives = catalog.ForChapter(chapter.Id);
                Assert.Contains(objectives, x => x.Optional);
                Assert.Contains(objectives, x => x.Rewards.Length > 0);
            });
    }

    [Fact]
    public void EmbeddedCatalog_UsesContiguousZoneVisitsInsteadOfMergingRepeatedTowns()
    {
        var catalog = CampaignCatalog.Shared;

        foreach (var chapter in catalog.SourceChapters)
        {
            var sections = catalog.SectionsForChapter(chapter.Id);
            Assert.NotEmpty(sections);
            Assert.Equal(
                catalog.ForChapter(chapter.Id).Count,
                sections.Sum(x => x.Objectives.Length));
            Assert.All(
                sections,
                section => Assert.All(
                    section.Objectives,
                    objective => Assert.Same(section, catalog.SectionContaining(objective.Id))));
        }

        var actOneClearfellVisits = catalog.SectionsForChapter("act1")
            .Count(x => x.AreaName.Contains("Clearfell", StringComparison.OrdinalIgnoreCase));
        Assert.True(actOneClearfellVisits > 1);
    }

    [Fact]
    public void Load_RejectsDuplicateStableIds()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "guideVersion": "test",
          "sourceRepository": "test",
          "sourceCommit": "abc",
          "sourceLicense": "MIT",
          "sourceRowCount": 1,
          "sourceChapters": [{ "id": "act1", "rowCount": 1, "implemented": true }],
          "objectives": [
            { "id": "same", "chapter": "act1", "order": 1, "text": "a", "source": [{ "chapter": "act1", "row": 1 }] },
            { "id": "same", "chapter": "act1", "order": 2, "text": "b", "source": [{ "chapter": "act1", "row": 1 }] }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var error = Assert.Throws<InvalidDataException>(() => CampaignCatalog.Load(stream));

        Assert.Contains("duplicate objective id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsUnknownAreaCodesAndMissingCoverage()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "guideVersion": "test",
          "sourceRepository": "test",
          "sourceCommit": "abc",
          "sourceLicense": "MIT",
          "sourceRowCount": 2,
          "sourceChapters": [{ "id": "act1", "rowCount": 2, "implemented": true }],
          "objectives": [
            {
              "id": "one", "chapter": "act1", "order": 1, "text": "a",
              "source": [{ "chapter": "act1", "row": 1 }],
              "target": { "kind": "Npc", "allowedAreaCodes": ["NOT_A_REAL_AREA"] }
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var error = Assert.Throws<InvalidDataException>(() => CampaignCatalog.Load(stream));

        Assert.Contains("unknown area code", error.Message, StringComparison.Ordinal);
        Assert.Contains("source coverage count mismatch", error.Message, StringComparison.Ordinal);
    }
}
