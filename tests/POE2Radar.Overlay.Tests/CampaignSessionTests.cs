using System.Text;
using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Campaign;
using POE2Radar.Overlay.Config;
using Xunit;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Tests;

public sealed class CampaignSessionTests
{
    [Fact]
    public void StableAreaEntry_RequiresTwoStableTicks()
    {
        using var fixture = Fixture.Create(
            targetKind: "AreaTransition",
            completion: """{ "kind": "StableAreaEntry", "expectedAreaCode": "G1_2", "stableTicks": 2 }""",
            targetExtra: """, "destinationAreaCode": "G1_2" """);

        var first = fixture.Update("G1_2");
        var second = fixture.Update("G1_2");

        Assert.NotNull(first.Current);
        Assert.Null(second.Current);
    }

    [Fact]
    public void BossDeath_RequiresAliveObservationAndNeverCompletesOnDisappearance()
    {
        using var fixture = Fixture.Create(
            targetKind: "Boss",
            completion: """{ "kind": "BossDefeated" }""",
            targetExtra: """, "metadataGlobs": ["Metadata/monster/boss"] """);
        var alive = CampaignTargetResolverTests.Entity(
            42, "Metadata/monster/boss", 2, 2, hpCur: 100, hpMax: 100);
        var dead = CampaignTargetResolverTests.Entity(
            42, "Metadata/monster/boss", 2, 2, hpCur: 0, hpMax: 100);

        Assert.NotNull(fixture.Update("G1_1", alive).Current);
        Assert.NotNull(fixture.Update("G1_1").Current);
        Assert.Null(fixture.Update("G1_1", dead).Current);
    }

    [Fact]
    public void ObjectChange_RequiresIncompleteThenOpenedForSameEntity()
    {
        using var fixture = Fixture.Create(
            targetKind: "QuestObject",
            completion: """{ "kind": "ObjectChanged" }""",
            targetExtra: """, "metadataGlobs": ["Metadata/quest/object"] """);
        var alreadyOpen = CampaignTargetResolverTests.Entity(
            9, "Metadata/quest/object", 2, 2, opened: true);
        var incomplete = CampaignTargetResolverTests.Entity(
            9, "Metadata/quest/object", 2, 2);

        Assert.NotNull(fixture.Update("G1_1", alreadyOpen).Current);
        Assert.NotNull(fixture.Update("G1_1", incomplete).Current);
        Assert.Null(fixture.Update("G1_1", alreadyOpen).Current);
    }

    [Fact]
    public void Interludes_SwitchToTheBranchThePlayerActuallyEntered()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "guideVersion": "test",
          "sourceRepository": "test",
          "sourceCommit": "abc",
          "sourceLicense": "MIT",
          "sourceRowCount": 2,
          "sourceChapters": [{ "id": "interludes", "rowCount": 2, "implemented": true }],
          "objectives": [
            {
              "id": "interlude.53", "chapter": "interludes", "branch": "interlude-5.3",
              "order": 1, "areaName": "Ashen Forest", "text": "Interlude 5.3",
              "source": [{ "chapter": "interludes", "row": 1 }],
              "target": { "kind": "Npc", "allowedAreaCodes": ["P3_1"], "validated": false }
            },
            {
              "id": "interlude.52", "chapter": "interludes", "branch": "interlude-5.2",
              "order": 2, "areaName": "Khari Crossing", "text": "Interlude 5.2",
              "source": [{ "chapter": "interludes", "row": 2 }],
              "target": { "kind": "Npc", "allowedAreaCodes": ["P2_1"], "validated": false }
            }
          ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var catalog = CampaignCatalog.Load(stream);
        var directory = Directory.CreateTempSubdirectory("poe2radar-campaign-interlude-");
        try
        {
            var session = new CampaignSession(
                catalog,
                new CampaignProgressStore(Path.Combine(directory.FullName, "progress.json")));
            var settings = new CampaignSettings { Enabled = true, AutoActivate = true };
            var frame = new CampaignFrame(
                "P2_1", 55, NumVec2.Zero, "League", "Character", [],
                Array.Empty<Poe2Live.Landmark>(),
                Array.Empty<Poe2Live.ServerMinimapIcon>());

            var view = session.Update(frame, settings);

            Assert.Equal("interlude.52", view.Current?.Id);
            Assert.Equal("INTERLUDE 5.2", view.ChapterLabel);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void RequiredMode_SkipsOptionalObjectivesButFullClearIncludesThem()
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
              "id": "act1.optional", "chapter": "act1", "order": 1, "areaName": "Clearfell",
              "text": "Optional detour", "optional": true,
              "source": [{ "chapter": "act1", "row": 1 }]
            },
            {
              "id": "act1.required", "chapter": "act1", "order": 2, "areaName": "Clearfell",
              "text": "Required route",
              "source": [{ "chapter": "act1", "row": 2 }]
            }
          ]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var catalog = CampaignCatalog.Load(stream);
        var directory = Directory.CreateTempSubdirectory("poe2radar-campaign-mode-");
        try
        {
            var session = new CampaignSession(
                catalog,
                new CampaignProgressStore(Path.Combine(directory.FullName, "progress.json")));
            var frame = new CampaignFrame(
                "G1_2", 3, NumVec2.Zero, "League", "Character", [],
                Array.Empty<Poe2Live.Landmark>(),
                Array.Empty<Poe2Live.ServerMinimapIcon>());

            var required = session.Update(
                frame,
                new CampaignSettings
                {
                    Enabled = true,
                    AutoActivate = true,
                    GuideMode = CampaignGuideMode.Required,
                });
            var full = session.Update(
                frame,
                new CampaignSettings
                {
                    Enabled = true,
                    AutoActivate = true,
                    GuideMode = CampaignGuideMode.FullClear,
                });

            Assert.Equal("act1.required", required.Current?.Id);
            Assert.Single(required.ZoneObjectives);
            Assert.Equal(1, required.RequiredTotal);
            Assert.Equal(2, required.FullTotal);
            Assert.Equal("act1.optional", full.Current?.Id);
            Assert.Equal(2, full.ZoneObjectives.Length);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void ImportProgress_FiltersUnknownObjectiveIds()
    {
        using var fixture = Fixture.Create(
            targetKind: "NonSpatial",
            completion: """{ "kind": "Manual" }""",
            targetExtra: "");
        fixture.Update("G1_1");
        var code = CampaignProgressCodec.Encode("test", ["act1.test", "removed.objective"]);

        Assert.True(fixture.Session.ImportCurrentCharacter(code));
        Assert.Null(fixture.Update("G1_1").Current);
        Assert.True(CampaignProgressCodec.TryDecode(
            fixture.Session.ExportCurrentCharacter(),
            out var exported));
        Assert.DoesNotContain("removed.objective", exported);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly CampaignSession _session;
        private readonly CampaignSettings _settings = new()
        {
            Enabled = true,
            AutoActivate = true,
            SafeAutoCheck = true,
        };

        private Fixture(DirectoryInfo directory, CampaignSession session)
        {
            _directory = directory;
            _session = session;
        }

        public CampaignSession Session => _session;

        public static Fixture Create(string targetKind, string completion, string targetExtra)
        {
            var json = $$"""
            {
              "schemaVersion": 1,
              "guideVersion": "test",
              "sourceRepository": "test",
              "sourceCommit": "abc",
              "sourceLicense": "MIT",
              "sourceRowCount": 1,
              "sourceChapters": [{ "id": "act1", "rowCount": 1, "implemented": true }],
              "objectives": [{
                "id": "act1.test", "chapter": "act1", "order": 1, "areaName": "Test", "text": "Test",
                "source": [{ "chapter": "act1", "row": 1 }],
                "target": {
                  "kind": "{{targetKind}}", "label": "Target", "allowedAreaCodes": ["G1_1"],
                  "validated": true {{targetExtra}}
                },
                "completion": {{completion}}
              }]
            }
            """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var catalog = CampaignCatalog.Load(stream);
            var directory = Directory.CreateTempSubdirectory("poe2radar-campaign-session-");
            var session = new CampaignSession(
                catalog,
                new CampaignProgressStore(Path.Combine(directory.FullName, "progress.json")));
            return new Fixture(directory, session);
        }

        public CampaignView Update(string area, params Poe2Live.EntityDot[] entities)
            => _session.Update(
                new CampaignFrame(
                    area, 1, NumVec2.Zero, "League", "Character", entities,
                    Array.Empty<Poe2Live.Landmark>(),
                    Array.Empty<Poe2Live.ServerMinimapIcon>()),
                _settings);

        public void Dispose() => _directory.Delete(true);
    }
}
