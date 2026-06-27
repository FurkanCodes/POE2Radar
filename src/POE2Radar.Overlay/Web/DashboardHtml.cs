namespace POE2Radar.Overlay.Web;

using System.Net;
using POE2Radar.Overlay.Settings;

/// <summary>
/// Self-contained web dashboard served at <c>GET /</c> by <see cref="ApiServer"/>. One inlined
/// HTML/CSS/JS document — no external assets beyond Google Fonts. The Console tab reads/writes
/// radar/visual settings via <c>/api/settings</c> (the only writes it makes — flags + calibration,
/// never flask/automation); the Filters tab manages watched/hidden lists via <c>/api/watched</c> /
/// <c>/api/hidden</c>; the Dashboard tab polls the same-origin read endpoints (<c>/state</c>,
/// <c>/entities</c>, <c>/landmarks</c>, <c>/api/nav</c>).
/// </summary>
internal static class DashboardHtml
{
    public static string Page => ApplySettingHints(PageTemplate);

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private static string ApplySettingHints(string html) => html
        .Replace("{{H.ShowMonsters}}", H(SettingHints.Radar.ShowMonsters))
        .Replace("{{H.ShowTerrain}}", H(SettingHints.Radar.ShowTerrain))
        .Replace("{{H.ShowPlayerBlip}}", H(SettingHints.Radar.ShowPlayerBlip))
        .Replace("{{H.AlwaysShowOverlay}}", H(SettingHints.Radar.AlwaysShowOverlay))
        .Replace("{{H.HideJunk}}", H(SettingHints.Radar.HideJunk))
        .Replace("{{H.CuratedLandmarks}}", H(SettingHints.Radar.CuratedLandmarks))
        .Replace("{{H.LandmarkClusterGap}}", H(SettingHints.Radar.LandmarkClusterGap))
        .Replace("{{H.LowImpactMode}}", H(SettingHints.Performance.LowImpactMode))
        .Replace("{{H.FpsCap}}", H(SettingHints.Performance.FpsCap))
        .Replace("{{H.LiveRefreshHz}}", H(SettingHints.Performance.LiveRefreshHz))
        .Replace("{{H.WorldRefreshHz}}", H(SettingHints.Performance.WorldRefreshHz))
        .Replace("{{H.InactiveRefreshHz}}", H(SettingHints.Performance.InactiveRefreshHz))
        .Replace("{{H.HpBarRefreshHz}}", H(SettingHints.Performance.HpBarRefreshHz))
        .Replace("{{H.MaxLiveHpBars}}", H(SettingHints.Performance.MaxLiveHpBars))
        .Replace("{{H.SmoothOverlayMotion}}", H(SettingHints.Performance.SmoothOverlayMotion))
        .Replace("{{H.OverlaySmoothingMs}}", H(SettingHints.Performance.OverlaySmoothingMs))
        .Replace("{{H.ChipSmoothingMs}}", H(SettingHints.Performance.ChipSmoothingMs))
        .Replace("{{H.PixelSnapLabels}}", H(SettingHints.Performance.PixelSnapLabels))
        .Replace("{{H.OverlayVSync}}", H(SettingHints.Performance.OverlayVSync))
        .Replace("{{H.FpsResourceOverlay}}", H(SettingHints.Performance.FpsResourceOverlay))
        .Replace("{{H.ExtendedPerfStats}}", H(SettingHints.Performance.ExtendedPerfStats))
        .Replace("{{H.MetricsRefreshHz}}", H(SettingHints.Performance.MetricsRefreshHz))
        .Replace("{{H.GpuMetricsSeconds}}", H(SettingHints.Performance.GpuMetricsSeconds))
        .Replace("{{H.ShowPathWorld}}", H(SettingHints.Radar.ShowPathWorld))
        .Replace("{{H.ShowGroundWaypoints}}", H(SettingHints.Radar.ShowGroundWaypoints))
        .Replace("{{H.ShowPathMap}}", H(SettingHints.Radar.ShowPathMap))
        .Replace("{{H.ShowPathMinimap}}", H(SettingHints.Radar.ShowPathMinimap))
        .Replace("{{H.AutoPathNearest}}", H(SettingHints.Entities.AutoPathNearest))
        .Replace("{{H.DetectionRadius}}", H(SettingHints.Entities.DetectionRadius))
        .Replace("{{H.ShowAllMonsters}}", H(SettingHints.Entities.ShowAllMonsters))
        .Replace("{{H.NavMenuCorner}}", H(SettingHints.Performance.NavMenuCorner))
        .Replace("{{H.GlobalIconScale}}", H(SettingHints.DisplayRules.GlobalIconScale))
        .Replace("{{H.LargeMapScale}}", H(SettingHints.Radar.LargeMapScale))
        .Replace("{{H.MinimapScale}}", H(SettingHints.Radar.MinimapScale))
        .Replace("{{H.OffsetX}}", H(SettingHints.Radar.OffsetX))
        .Replace("{{H.OffsetY}}", H(SettingHints.Radar.OffsetY))
        .Replace("{{H.TerrainInterior}}", H(SettingHints.Radar.TerrainInterior))
        .Replace("{{H.TerrainEdge}}", H(SettingHints.Radar.TerrainEdge))
        .Replace("{{H.TerrainEdgeDetail}}", H(SettingHints.Radar.TerrainEdgeDetail))
        .Replace("{{H.TerrainEdgeThickness}}", H(SettingHints.Radar.TerrainEdgeThickness))
        .Replace("{{H.AtlasShowAllNodes}}", H(SettingHints.Atlas.ShowAllNodes))
        .Replace("{{H.AtlasShowNames}}", H(SettingHints.Atlas.ShowNames))
        .Replace("{{H.AtlasRevealFog}}", H(SettingHints.Atlas.RevealFog))
        .Replace("{{H.AtlasOffScreenArrows}}", H(SettingHints.Atlas.OffScreenArrows))
        .Replace("{{H.AtlasShowRoute}}", H(SettingHints.Atlas.ShowRoute))
        .Replace("{{H.AtlasRouteFromCurrent}}", H(SettingHints.Atlas.RouteFromCurrent))
        .Replace("{{H.AtlasSearchQuery}}", H(SettingHints.Atlas.SearchQuery))
        .Replace("{{H.AtlasHideCompleted}}", H(SettingHints.Atlas.HideCompleted))
        .Replace("{{H.AtlasHideNotAccessible}}", H(SettingHints.Atlas.HideNotAccessible))
        .Replace("{{H.AtlasHideAvailable}}", H(SettingHints.Atlas.HideAvailable))
        .Replace("{{H.AtlasBiomeBorders}}", H(SettingHints.Atlas.BiomeBorders))
        .Replace("{{H.AtlasContentBadges}}", H(SettingHints.Atlas.ContentBadges))
        .Replace("{{H.AtlasContentCount}}", H(SettingHints.Atlas.ContentCount))
        .Replace("{{H.AtlasRouteChevrons}}", H(SettingHints.Atlas.RouteChevrons))
        .Replace("{{H.AtlasIconScale}}", H(SettingHints.Atlas.IconScale))
        .Replace("{{H.AtlasLabelScale}}", H(SettingHints.Atlas.LabelScale))
        .Replace("{{H.AtlasRouteThickness}}", H(SettingHints.Atlas.RouteThickness))
        .Replace("{{H.AtlasChevronSpacing}}", H(SettingHints.Atlas.ChevronSpacing))
        .Replace("{{H.AtlasLanguage}}", H(SettingHints.Atlas.Language))
        .Replace("{{H.FlaskTriggerPool}}", H(SettingHints.Flask.TriggerPool))
        .Replace("{{H.FlaskLifeThreshold}}", H(SettingHints.Flask.LifeThreshold))
        .Replace("{{H.FlaskEsThreshold}}", H(SettingHints.Flask.EsThreshold))
        .Replace("{{H.FlaskManaThreshold}}", H(SettingHints.Flask.ManaThreshold))
        .Replace("{{H.FlaskLifeKey}}", H(SettingHints.Flask.LifeKey))
        .Replace("{{H.FlaskManaKey}}", H(SettingHints.Flask.ManaKey))
        .Replace("{{H.FlaskLifeCooldown}}", H(SettingHints.Flask.LifeCooldown))
        .Replace("{{H.FlaskManaCooldown}}", H(SettingHints.Flask.ManaCooldown))
        .Replace("{{H.GamepadHotkeys}}", H(SettingHints.Hotkeys.GamepadEnabled))
        .Replace("{{H.PadSlot}}", H(SettingHints.Hotkeys.PadSlot))
        .Replace("{{H.HkHideEntity}}", H(SettingHints.Hotkeys.HideEntity))
        .Replace("{{H.HkTrackEntity}}", H(SettingHints.Hotkeys.TrackEntity))
        .Replace("{{H.HkAutoPath}}", H(SettingHints.Hotkeys.AutoPathToggle))
        .Replace("{{H.HkAddNearest}}", H(SettingHints.Hotkeys.AddNearestPath))
        .Replace("{{H.HkClearPaths}}", H(SettingHints.Hotkeys.ClearPaths))
        .Replace("{{H.HkAutoFlask}}", H(SettingHints.Hotkeys.AutoFlaskToggle))
        .Replace("{{H.HkAtlasPick}}", H(SettingHints.Hotkeys.AtlasPick))
        .Replace("{{H.HkToggleSettings}}", H(SettingHints.Hotkeys.ToggleSettings))
        .Replace("{{H.HkOpenDashboard}}", H(SettingHints.Hotkeys.OpenDashboard))
        .Replace("{{H.HkQuit}}", H(SettingHints.Hotkeys.Quit))
        .Replace("{{H.HpNormal}}", H(SettingHints.HpBars.Normal))
        .Replace("{{H.HpMagic}}", H(SettingHints.HpBars.Magic))
        .Replace("{{H.HpRare}}", H(SettingHints.HpBars.Rare))
        .Replace("{{H.HpUnique}}", H(SettingHints.HpBars.Unique))
        .Replace("{{H.HpUseTextures}}", H(SettingHints.HpBars.UseTextures))
        .Replace("{{H.HpHeight}}", H(SettingHints.HpBars.BarHeight))
        .Replace("{{H.HpOffsetX}}", H(SettingHints.HpBars.OffsetX))
        .Replace("{{H.HpOffsetY}}", H(SettingHints.HpBars.OffsetY))
        .Replace("{{H.HpColOn}}", H(SettingHints.HpBars.ColumnOn))
        .Replace("{{H.HpColWidth}}", H(SettingHints.HpBars.ColumnWidth))
        .Replace("{{H.HpColBorder}}", H(SettingHints.HpBars.ColumnBorder))
        .Replace("{{H.HpColThick}}", H(SettingHints.HpBars.ColumnThick))
        .Replace("{{H.LiveColName}}", H(SettingHints.Dashboard.LiveColName))
        .Replace("{{H.LiveColCategory}}", H(SettingHints.Dashboard.LiveColCategory))
        .Replace("{{H.LiveColRarity}}", H(SettingHints.Dashboard.LiveColRarity))
        .Replace("{{H.LiveColDist}}", H(SettingHints.Dashboard.LiveColDist))
        .Replace("{{H.LiveColHp}}", H(SettingHints.Dashboard.LiveColHp))
        .Replace("{{H.LiveColRule}}", H(SettingHints.Dashboard.LiveColRule))
        .Replace("{{H.DbColCategory}}", H(SettingHints.Dashboard.DbColCategory))
        .Replace("{{H.DbColPath}}", H(SettingHints.Dashboard.DbColPath))
        .Replace("{{H.RulesPaused}}", H(SettingHints.Dashboard.RulesPaused))
        .Replace("{{H.RulesHide}}", H(SettingHints.Dashboard.RulesHide))
        .Replace("{{H.RulesPath}}", H(SettingHints.Dashboard.RulesPath))
        .Replace("{{H.LootEnabled}}", H(SettingHints.Loot.Enabled))
        .Replace("{{H.LootLeague}}", H(SettingHints.Loot.LeagueOverride))
        .Replace("{{H.LootHighlightMin}}", H(SettingHints.Loot.HighlightMin))
        .Replace("{{H.LootUniqueFloor}}", H(SettingHints.Loot.UniqueFloor))
        .Replace("{{H.LootCurrencyFloor}}", H(SettingHints.Loot.CurrencyFloor))
        .Replace("{{H.LootOtherFloor}}", H(SettingHints.Loot.OtherFloor))
        .Replace("{{H.LootMinQty}}", H(SettingHints.Loot.MinListingQty))
        .Replace("{{H.LootAnchorTags}}", H(SettingHints.Loot.AnchorToTags))
        .Replace("{{H.MonolithEnabled}}", H(SettingHints.Monoliths.Enabled))
        .Replace("{{H.MonolithHighlightMin}}", H(SettingHints.Monoliths.HighlightMin))
        .Replace("{{H.MonolithMinReward}}", H(SettingHints.Monoliths.MinReward))
        .Replace("{{H.MonolithMinValue}}", H(SettingHints.Monoliths.MinValue))
        .Replace("{{H.MonolithHideCollected}}", H(SettingHints.Monoliths.HideCollected))
        .Replace("{{H.MonolithShowMapLabel}}", H(SettingHints.Monoliths.ShowMapLabel))
        .Replace("{{H.MonolithShowPanel}}", H(SettingHints.Monoliths.ShowPanel))
        .Replace("{{H.RitualEnabled}}", H(SettingHints.RitualHelper.Enabled))
        .Replace("{{H.RitualShowPrices}}", H(SettingHints.RitualHelper.ShowPrices))
        .Replace("{{H.RitualMinDisplay}}", H(SettingHints.RitualHelper.MinDisplayExalted))
        .Replace("{{H.RitualPriceSource}}", H(SettingHints.RitualHelper.PriceSource))
        .Replace("{{H.RitualLeague}}", H(SettingHints.RitualHelper.League))
        .Replace("{{H.RitualRefresh}}", H(SettingHints.RitualHelper.RefreshIntervalMin))
        .Replace("{{H.RitualReadHz}}", H(SettingHints.RitualHelper.ReadHz))
        .Replace("{{H.RitualDiagnose}}", H(SettingHints.RitualHelper.DiagnosePricing))
        .Replace("{{H.RitualAlert}}", H(SettingHints.RitualHelper.PlayValueAlert))
        .Replace("{{H.RitualAlertDiv}}", H(SettingHints.RitualHelper.AlertMinDivine));

    private const string PageTemplate = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>POE2Radar Console</title>
<style>
  :root{
    --bg:#0b0e14; --bg2:#0f131a; --panel:#141820; --panel2:#1a2030;
    --line:#2a3344; --line-soft:#222a38;
    --ink:#e8ecf4; --ink-dim:#9aa8be; --ink-faint:#6b7a92;
    --gold:#6eb6ff; --gold-bright:#8ecfff; --gold-deep:#3d8fd9;
    --blood:#e5534b; --blood-bright:#ff6b6b;
    --rare:#ffd866; --magic:#7aa2ff; --unique:#ff8c42; --normal:#b8c0cc;
    --good:#3dd68c; --poi:#56d4e8;
    --accent:#3b9eff; --accent-dim:#2563a8;
    --shadow:0 8px 32px rgba(0,0,0,.45);
    --radius:10px; --radius-sm:6px;
  }
  *{box-sizing:border-box}
  html,body{height:100%}
  body{
    margin:0;
    background:radial-gradient(120% 80% at 80% -20%, rgba(59,158,255,.08), transparent 50%), var(--bg);
    color:var(--ink);
    font-family:system-ui,-apple-system,"Segoe UI",Roboto,Ubuntu,sans-serif;
    font-size:13px; line-height:1.5;
    -webkit-font-smoothing:antialiased;
    overflow:hidden;
  }

  .shell{display:grid; grid-template-rows:auto 1fr; height:100vh}

  header{
    display:flex; align-items:center; gap:16px; padding:12px 24px;
    border-bottom:1px solid var(--line);
    background:rgba(14,18,26,.92); backdrop-filter:blur(12px);
  }
  .mark{display:flex; align-items:center; gap:10px}
  .mark h1{
    font-size:17px; font-weight:700; margin:0; letter-spacing:-.02em;
    color:var(--ink);
  }
  .mark h1 span{color:var(--accent); font-weight:800}
  .hgap{flex:1}
  .conn{
    display:flex; align-items:center; gap:8px; font-size:12px; font-weight:500;
    color:var(--ink-dim); padding:6px 12px; border-radius:20px;
    background:var(--panel2); border:1px solid var(--line);
  }
  .dot{width:8px; height:8px; border-radius:50%; background:var(--blood); flex-shrink:0}
  .conn.live .dot{background:var(--good); box-shadow:0 0 0 3px rgba(61,214,140,.25)}
  .conn.live{color:var(--good)}
  .area-chip{
    font-size:13px; color:var(--ink-dim);
    border:1px solid var(--line); padding:6px 14px; border-radius:20px;
    background:var(--panel2); max-width:320px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;
  }
  .area-chip b{color:var(--ink); font-weight:600}

  .body{display:grid; grid-template-columns:272px 1fr; gap:0; min-height:0; transition:grid-template-columns .2s ease}
  body.sidebar-off .body{grid-template-columns:1fr}
  body.sidebar-off aside{display:none}
  aside{
    border-right:1px solid var(--line); padding:20px 18px;
    overflow-y:auto; background:var(--bg2);
  }
  main{display:grid; grid-template-rows:auto 1fr; min-height:0; min-width:0; background:var(--bg)}

  .vital{margin-bottom:14px}
  .vital .vlabel{
    display:flex; justify-content:space-between; font-size:11px; font-weight:600;
    color:var(--ink-dim); margin-bottom:6px;
  }
  .vital .vlabel .num{color:var(--ink); font-variant-numeric:tabular-nums}
  .bar{height:6px; background:var(--panel2); border-radius:99px; overflow:hidden}
  .bar > i{display:block; height:100%; transition:width .35s ease; border-radius:99px}
  .bar.hp > i{background:linear-gradient(90deg,#c23d3d,#ff6b6b)}
  .bar.es > i{background:linear-gradient(90deg,#1a8a7a,#3dd6c3)}
  .bar.mana > i{background:linear-gradient(90deg,#3d5fc7,var(--magic))}

  .sect{
    font-size:10px; font-weight:700; letter-spacing:.08em; text-transform:uppercase;
    color:var(--ink-faint); margin:20px 0 10px;
  }

  .kv{
    display:flex; justify-content:space-between; align-items:center;
    padding:7px 0; font-size:12px; border-bottom:1px solid var(--line-soft);
  }
  .kv:last-child{border-bottom:none}
  .kv span:first-child{color:var(--ink-faint)}
  .kv span:last-child{color:var(--ink); font-weight:500; font-variant-numeric:tabular-nums}

  .tally{display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:6px}
  .tally .t{
    border:1px solid var(--line); background:var(--panel); padding:10px 12px; border-radius:var(--radius-sm);
  }
  .tally .t .n{font-size:22px; font-weight:700; color:var(--ink); line-height:1; font-variant-numeric:tabular-nums}
  .tally .t .l{font-size:10px; color:var(--ink-faint); margin-top:4px}

  .znotes{
    margin-top:10px; padding:12px; border:1px solid var(--line); border-radius:var(--radius-sm);
    background:var(--panel); white-space:pre-wrap; font-size:12px; line-height:1.55;
    color:var(--ink-dim); max-height:200px; overflow:auto;
  }
  .znotes .zt{font-size:12px; font-weight:600; color:var(--accent); margin-bottom:6px; white-space:normal}

  .tabs{
    display:flex; flex-wrap:wrap; gap:6px; padding:14px 20px;
    border-bottom:1px solid var(--line); background:var(--bg2);
  }
  .tab{
    font-size:13px; font-weight:500; color:var(--ink-dim);
    background:transparent; border:1px solid transparent;
    padding:8px 14px; cursor:pointer; border-radius:var(--radius-sm);
    transition:background .15s,color .15s,border-color .15s;
  }
  .tab:hover{color:var(--ink); background:var(--panel)}
  .tab.on{
    color:var(--ink); background:var(--panel); border-color:var(--line);
    box-shadow:0 1px 0 var(--accent) inset;
  }

  .view{overflow:auto; padding:20px 24px; min-height:0}
  .view[hidden]{display:none}
  /* ── atlas tab ── */
  .arow{display:grid; grid-template-columns:minmax(200px,2fr) minmax(120px,1.4fr) 120px; gap:10px; align-items:center;
        padding:5px 10px; border-bottom:1px solid var(--line); font-size:13px}
  .arow.ahead{font-weight:600; color:var(--ink-faint); font-size:10px; text-transform:uppercase; letter-spacing:.06em; border-bottom:1px solid var(--line); position:sticky; top:0; background:var(--panel)}
  .arow.val{background:rgba(59,158,255,.06)}
  .arow .acode{font-family:ui-monospace,Consolas,monospace; color:var(--ink)}
  .arow.val .acode{color:var(--accent)}
  .arow .aname{color:var(--ink-dim)}
  .arow .aid{display:inline-block; min-width:22px; color:var(--ink-dim); font-family:ui-monospace,Consolas,monospace}
  .rin{color:#6ee787; font-weight:600} .rno{color:var(--ink-dim); opacity:.5}
  .arow.nrow{grid-template-columns:60px minmax(90px,1fr) minmax(200px,2fr) 130px; cursor:pointer}
  .arow.nrow:hover{background:rgba(255,255,255,.04)}
  .arow.nrow.sel{background:rgba(60,220,255,.16); outline:1px solid var(--edge,#3cdcff)}
  .amono{font-family:ui-monospace,Consolas,monospace; color:var(--ink-dim); font-size:12px}
  .ntag{font-size:10px; font-weight:600; padding:0 6px; border-radius:8px; border:1px solid var(--line); margin-right:3px}
  .ntag.tc{color:#ff9f43;border-color:#a35a00} .ntag.tv{color:var(--ink-dim)} .ntag.tu{color:#6ee787;border-color:#2f6b3f}
  .ntag.tk{color:#73a6ff;border-color:#2a4a80} .ntag.ts{color:#c98bff;border-color:#5a3a80}
  .akind{font-size:11px; font-weight:600; padding:1px 8px; border-radius:10px; border:1px solid var(--line); color:var(--ink-dim)}
  .akind.k-boss{color:#ff7300; border-color:#ff7300} .akind.k-unique{color:#ff9f43; border-color:#a35a00}
  .akind.k-tower{color:#73a6ff; border-color:#2a4a80} .akind.k-merchant{color:#c98bff; border-color:#5a3a80}

  /* controls */
  .controls{display:flex; flex-wrap:wrap; gap:8px; align-items:center; margin-bottom:16px}
  .chip{
    font-size:12px; font-weight:500; color:var(--ink-dim);
    border:1px solid var(--line); background:var(--panel); padding:6px 12px; border-radius:20px; cursor:pointer;
    transition:background .15s,border-color .15s,color .15s;
  }
  .chip:hover{border-color:var(--accent-dim); color:var(--ink)}
  .chip.on{background:var(--accent); border-color:var(--accent); color:#fff}
  input[type=search]{
    font-family:inherit; font-size:13px; color:var(--ink); background:var(--panel2);
    border:1px solid var(--line); border-radius:var(--radius-sm); padding:8px 12px; min-width:200px; flex:1;
  }
  input[type=search]:focus{outline:none; border-color:var(--accent); box-shadow:0 0 0 3px rgba(59,158,255,.15)}
  input[type=search]::placeholder{color:var(--ink-faint)}

  table{width:100%; border-collapse:collapse; font-size:12px}
  thead th{
    text-align:left; font-weight:600; font-size:10px; letter-spacing:.06em; text-transform:uppercase;
    color:var(--ink-faint); padding:10px 12px; border-bottom:1px solid var(--line); position:sticky; top:-20px;
    background:var(--bg);
  }
  tbody td{padding:8px 12px; border-bottom:1px solid var(--line-soft); white-space:nowrap}
  tbody tr:hover{background:rgba(59,158,255,.04)}
  .meta{color:var(--ink-faint); font-size:11px; max-width:380px; overflow:hidden; text-overflow:ellipsis}
  .rar-Normal{color:var(--normal)} .rar-Magic{color:var(--magic)} .rar-Rare{color:var(--rare)} .rar-Unique{color:var(--unique)}
  .pill{font-size:10px; font-weight:600; padding:2px 8px; border-radius:20px; border:1px solid currentColor}
  .friendly{color:var(--good)} .hostile{color:var(--blood-bright)}
  .num-r{text-align:right; color:var(--ink-dim); font-variant-numeric:tabular-nums}
  .hpbar{width:60px; height:5px; background:var(--panel2); border-radius:99px; overflow:hidden; display:inline-block; vertical-align:middle}
  .hpbar > i{display:block; height:100%; background:linear-gradient(90deg,#c23d3d,#ff6b6b); border-radius:99px}

  .scrollbox{max-height:520px; overflow:auto; border:1px solid var(--line); border-radius:var(--radius-sm); background:var(--panel)}
  .db-cat{font-size:10px; font-weight:600; color:var(--ink-dim); border:1px solid var(--line); padding:2px 8px; border-radius:20px}
  .db-path{font-family:ui-monospace,Consolas,monospace; font-size:11px; color:var(--ink-faint); max-width:520px; overflow:hidden; text-overflow:ellipsis}
  .filter-btns{display:flex; flex-wrap:wrap; gap:6px; margin:10px 0}
  .filter-btn{font-size:11px; font-weight:500; padding:5px 12px; border:1px solid var(--line); background:var(--panel); color:var(--ink-dim); cursor:pointer; border-radius:20px; transition:all .15s}
  .filter-btn.active{background:var(--accent); border-color:var(--accent); color:#fff}
  .filter-btn:hover:not(.active){border-color:var(--accent-dim); color:var(--ink)}
  tbody tr.watched{background:rgba(59,158,255,.08)}

  .lm{display:flex; align-items:center; gap:14px; padding:12px 14px; border:1px solid var(--line); border-radius:var(--radius-sm); margin-bottom:8px; background:var(--panel)}
  .lm:hover{border-color:var(--accent-dim)}
  .lm .name{font-size:14px; font-weight:600; color:var(--ink)}
  .lm .path{font-size:11px; color:var(--ink-faint); overflow:hidden; text-overflow:ellipsis; white-space:nowrap}
  .lm .dist{margin-left:auto; color:var(--ink); font-size:14px; font-weight:600; flex:none; font-variant-numeric:tabular-nums}
  .lm .dist small{color:var(--ink-faint); font-size:10px; display:block; text-align:right}

  .empty{color:var(--ink-faint); text-align:center; padding:48px 0; font-size:14px}
  ::-webkit-scrollbar{width:8px;height:8px}
  ::-webkit-scrollbar-thumb{background:var(--line); border-radius:4px}
  ::-webkit-scrollbar-track{background:transparent}

  .panel-grid{display:grid; grid-template-columns:repeat(auto-fill,minmax(320px,1fr)); gap:16px; align-items:start}
  .card{border:1px solid var(--line); border-radius:var(--radius); background:var(--panel); padding:18px 20px}
  .card h3{font-size:13px; font-weight:700; color:var(--ink); margin:0 0 12px; letter-spacing:-.01em}
  .card h3 .tag{color:var(--ink-faint); font-size:11px; font-weight:500}
  .row{display:flex; align-items:center; justify-content:space-between; gap:16px; padding:10px 0; border-bottom:1px solid var(--line-soft)}
  .row:last-child{border-bottom:none}
  .row .rl{font-size:13px; color:var(--ink); min-width:0}
  .row .rl small{display:block; color:var(--ink-faint); font-size:11px; margin-top:3px; line-height:1.45}
  .sw{position:relative; width:40px; height:22px; flex:none; cursor:pointer; display:inline-block}
  .sw input{opacity:0; width:0; height:0; position:absolute}
  .sw .track{position:absolute; inset:0; background:var(--panel2); border:1px solid var(--line); border-radius:11px; transition:.2s}
  .sw .knob{position:absolute; top:2px; left:2px; width:16px; height:16px; border-radius:50%; background:var(--ink-faint); transition:.2s}
  .sw input:checked ~ .track{background:var(--accent); border-color:var(--accent)}
  .sw input:checked ~ .knob{transform:translateX(18px); background:#fff}
  .numin{font-family:inherit; font-size:12px; color:var(--ink); background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius-sm); padding:6px 10px; width:96px; text-align:right}
  .numin:focus{outline:none; border-color:var(--accent)}
  .ro{color:var(--accent); font-size:14px; font-weight:600; font-variant-numeric:tabular-nums}
  .hint-row{color:var(--ink-faint)!important; font-size:11px!important}
  .saved{font-size:11px; font-weight:600; color:var(--good); opacity:0; transition:opacity .3s}
  .saved.show{opacity:1}

  /* ── icon / mechanic style editors ── */
  .stylerow{display:flex; align-items:center; gap:9px; padding:9px 0; border-bottom:1px dotted var(--line-soft); flex-wrap:wrap}
  .stylerow:last-child{border-bottom:none}
  .stylerow .nm{flex:1 1 110px; min-width:90px; font-size:12px; color:var(--ink)}
  .stylerow .sw{width:38px; height:20px}
  .stylerow .sw .knob{width:13px; height:13px}
  .stylerow .sw input:checked ~ .knob{transform:translateX(18px)}
  input[type=color]{width:30px; height:24px; padding:0; border:1px solid var(--line); background:var(--panel2); border-radius:var(--radius-sm); cursor:pointer; flex:none}
  input[type=range].op{width:78px; accent-color:var(--accent); flex:none}
  .opv{font-size:10px; color:var(--ink-faint); width:30px; text-align:right}
  .numin.sz{width:56px}
  .sprctl{display:flex; align-items:center; gap:4px; flex-wrap:wrap; color:var(--ink-faint); font-size:10px}
  .sprctl .numin.sprin{width:46px; padding:4px 5px; font-size:10px}
  .mechrow{border:1px solid var(--line); border-radius:var(--radius-sm); background:var(--panel2); padding:10px 12px; margin-bottom:8px}
  .mechrow .top{display:flex; align-items:center; gap:9px; margin-bottom:8px}
  .mechrow .top input.mname{flex:1; font-family:inherit; font-size:12px; color:var(--ink); background:var(--bg); border:1px solid var(--line); border-radius:var(--radius-sm); padding:6px 10px}
  .mechrow .matchin{width:100%; font-family:inherit; font-size:11px; color:var(--ink-dim); background:var(--bg); border:1px solid var(--line); border-radius:var(--radius-sm); padding:6px 10px; margin-bottom:8px}
  .mechrow .ctl{display:flex; align-items:center; gap:9px; flex-wrap:wrap}
  .mcats{display:flex; align-items:center; gap:6px; flex-wrap:wrap; margin-bottom:8px}
  .mcats-lbl{font-size:10px; letter-spacing:.06em; text-transform:uppercase; color:var(--ink-faint); margin-right:2px}
  .mcats-hint{font-size:10px; font-style:italic; color:var(--ink-faint)}
  .catchip{display:inline-flex; align-items:center; font-size:11px; color:var(--ink-dim); background:var(--bg); border:1px solid var(--line); border-radius:20px; padding:3px 10px; cursor:pointer; user-select:none}
  .catchip:hover{border-color:var(--accent-dim)}
  .catchip.on{color:#fff; background:var(--accent); border-color:var(--accent); font-weight:600}
  .catchip input{display:none}
  /* Display-rule rows: collapsed one-line header, expand to the full editor. */
  .drrow{padding:10px 12px; border:1px solid var(--line); border-radius:var(--radius-sm); background:var(--panel2); margin-bottom:8px}
  .drhead{display:flex; align-items:center; gap:9px; cursor:pointer}
  .drhead .sw{flex:none}
  .drcaret{color:var(--ink-faint); width:10px; font-size:10px; flex:none}
  .drswatch{width:15px; height:15px; flex:none; display:inline-flex}
  .drswatch svg{width:15px; height:15px; display:block}
  .drnm{font-weight:600; color:var(--ink); white-space:nowrap; flex:none; max-width:200px; overflow:hidden; text-overflow:ellipsis}
  .drsum{flex:1 1 auto; min-width:0; color:var(--ink-faint); font-size:11px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis}
  .drbadges{display:inline-flex; gap:4px; flex:none}
  .drbadge{font-size:9px; text-transform:uppercase; letter-spacing:.05em; color:var(--ink-dim); border:1px solid var(--line); border-radius:8px; padding:1px 6px; white-space:nowrap}
  .drbadge.hide{color:var(--blood-bright); border-color:var(--blood)}
  .drbadge.paused{color:var(--ink-faint);border-color:var(--line)}
  .drrow.off .drnm,.drrow.off .drsum,.drrow.off .drswatch{opacity:.45}
  .dr-status{font-size:11px;font-weight:600;color:var(--good);flex:none;min-width:46px}
  .dr-status.paused{color:var(--ink-faint)}
  .dr-section{font-size:11px;font-weight:700;color:var(--ink-faint);text-transform:uppercase;letter-spacing:.06em;margin:14px 0 8px}
  .dr-section:first-child{margin-top:0}
  .rules-compare{font-size:12px;color:var(--ink-dim);line-height:1.55;margin:0;padding:12px 14px;border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--panel2);list-style:none}
  .rules-compare.vis-order{list-style:decimal;padding-left:28px}
  .rules-compare li{margin:6px 0}
  .rules-compare b{color:var(--ink)}
  .drbody{margin-top:10px; padding-top:10px; border-top:1px dotted var(--line-soft)}
  .drbody .top{align-items:center; margin-bottom:8px}
  .drord{display:inline-flex; gap:2px; flex:none}
  .drhead .delbtn{flex:none}
  .ordbtn{font-size:10px; line-height:1; color:var(--ink-dim); background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius-sm); padding:4px 7px; cursor:pointer}
  .ordbtn:hover{color:var(--accent); border-color:var(--accent-dim)}
  .drconds{display:flex; align-items:center; gap:10px; flex-wrap:wrap; margin-bottom:8px}
  .drsel{display:inline-flex; align-items:center; gap:5px; font-size:10px; letter-spacing:.05em; text-transform:uppercase; color:var(--ink-faint)}
  .drsel select{font-family:inherit; font-size:11px; text-transform:none; letter-spacing:0; color:var(--ink); background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius-sm); padding:4px 8px}
  .drsel select:hover{border-color:var(--accent-dim)}
  .drflag{display:inline-flex; align-items:center; gap:5px; font-size:11px; color:var(--ink-dim); cursor:pointer; user-select:none; white-space:nowrap}
  .dr-hideflag{color:var(--blood-bright)}
  .drrow.hideon{opacity:.72}
  .drrow.hideon .iconpick,.drrow.hideon .dr-color,.drrow.hideon .dr-op,.drrow.hideon .dr-size,.drrow.hideon .dr-label,.drrow.hideon .opv{opacity:.4; pointer-events:none}
  /* consolidated HP-bar card: per-rarity grid + shared geometry footer */
  .hpgrid{display:grid; grid-template-columns:30px 64px 1fr 30px 1fr; gap:9px 11px; align-items:center; padding:4px 0 2px}
  .hpgrid input[type=checkbox]{margin:0; justify-self:center}
  .hpgrid .hph{font-size:10px; letter-spacing:.06em; text-transform:uppercase; color:var(--ink-faint); text-align:right}
  .hpgrid .hph:first-child{text-align:left}
  .hpgrid .hpr{font-size:12px; color:var(--ink)}
  .hpgrid .numin{width:100%; min-width:0; padding:5px 8px}
  .hpgrid input[type=color]{width:100%}
  .hpshared{display:flex; gap:16px; flex-wrap:wrap; margin-top:10px; padding-top:11px; border-top:1px dotted var(--line-soft)}
  .hpshared label{display:flex; align-items:center; gap:7px; font-size:11px; color:var(--ink-dim)}
  .hpshared .numin{width:62px}
  .delbtn{font-family:inherit; font-size:11px; color:var(--blood-bright); background:transparent; border:1px solid var(--line); border-radius:2px; padding:4px 9px; cursor:pointer; flex:none}
  .trow-ctl{display:flex; align-items:center; gap:9px; flex:none}

  /* ── SVG icon picker (replaces the plain shape <select>): a button showing the chosen icon's
       silhouette + name, opening a shared popup grid of icon previews. ── */
  .iconpick{display:inline-flex; align-items:center; gap:6px; min-width:104px; background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius-sm); padding:4px 8px; cursor:pointer; flex:none}
  .iconpick:hover{border-color:var(--accent-dim)}
  .iconpick .ipreview{width:15px; height:15px; flex:none; display:inline-flex; align-items:center; justify-content:center; color:var(--ink)}
  .iconpick .ipreview svg{width:15px; height:15px; display:block}
  .spr-icon{display:inline-block;flex:none;background-image:url(/api/sprite-sheet);background-repeat:no-repeat;image-rendering:pixelated;vertical-align:middle}
  .spr-preview{margin-left:6px}
  .iconpick .ipname{font-size:11px; color:var(--ink); white-space:nowrap; overflow:hidden; text-overflow:ellipsis}
  .iconpick .ipcar{margin-left:auto; color:var(--ink-faint); font-size:8px}
  #iconPop{position:fixed; z-index:1000; display:none; background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius); box-shadow:var(--shadow); padding:10px; max-height:300px; overflow:auto}
  #iconPop.open{display:block}
  /* Add-rule picker modal: browse live entities + terrain tiles. */
  #pickPop{position:fixed; inset:0; z-index:1100; display:none; background:rgba(0,0,0,.62); padding:6vh 4vw}
  #pickPop.open{display:flex; justify-content:center; align-items:flex-start}
  .pickbox{display:flex; flex-direction:column; width:min(760px,100%); max-height:88vh; background:var(--panel); border:1px solid var(--line); border-radius:var(--radius); box-shadow:var(--shadow); overflow:hidden}
  .pickhead{display:flex; align-items:center; gap:10px; padding:12px 14px; border-bottom:1px solid var(--line)}
  .pickhead #pickSearch{flex:1; font-family:inherit; font-size:13px; color:var(--ink); background:var(--panel2); border:1px solid var(--line); border-radius:var(--radius-sm); padding:8px 12px}
  .pickkinds{display:inline-flex; gap:3px}
  .pickclose{font-size:13px; color:var(--ink-dim); background:transparent; border:1px solid var(--line); border-radius:3px; padding:6px 10px; cursor:pointer}
  .pickclose:hover{color:var(--blood-bright); border-color:var(--blood)}
  .picklist{overflow:auto; padding:4px 0}
  .pickrow{display:flex; align-items:center; gap:10px; padding:7px 14px; cursor:pointer; border-bottom:1px dotted var(--line-soft)}
  .pickrow:hover{background:var(--panel2)}
  .pickbadge{flex:none; font-size:9px; text-transform:uppercase; letter-spacing:.05em; color:var(--ink-dim); background:var(--panel2); border:1px solid var(--line); border-radius:20px; padding:2px 8px; min-width:58px; text-align:center}
  .pickbadge.tile{color:var(--poi); border-color:rgba(86,212,232,.5)}
  .pickbadge.entity{color:var(--accent); border-color:rgba(59,158,255,.5)}
  .picknm{flex:none; font-weight:600; color:var(--ink); max-width:230px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap}
  .picksub{flex:1; min-width:0; color:var(--ink-faint); font-size:11px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap}
  .pickrar{flex:none; font-size:10px; color:var(--rare)}
  .pickempty{padding:24px 14px; color:var(--ink-faint); font-style:italic; text-align:center}
  .pickfoot{padding:9px 14px; border-top:1px solid var(--line); color:var(--ink-faint); font-size:11px}
  /* Landmarks tab rows */
  .lmrow{display:flex; align-items:center; gap:10px; padding:6px 0; border-bottom:1px dotted var(--line-soft)}
  .lmbadge{flex:none; min-width:48px; text-align:center; font-size:9px; text-transform:uppercase; letter-spacing:.05em; color:var(--ink-dim); border:1px solid var(--line); border-radius:8px; padding:2px 6px}
  .lmbadge.user{color:var(--accent); border-color:rgba(59,158,255,.5)}
  .lmbadge.hidden{color:var(--blood-bright); border-color:var(--blood)}
  .lmarea{flex:none; min-width:64px; font-size:11px; color:var(--ink-dim); font-family:"Consolas",monospace}
  .lmlabel{flex:none; width:200px}
  .lmpath{flex:1; min-width:0; color:var(--ink-faint); font-size:11px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-family:"Consolas",monospace}
  .lmrow.sup .lmlabel,.lmrow.sup .lmpath{opacity:.5}
  .ipop-grid{display:grid; grid-template-columns:repeat(6,38px); gap:4px}
  .ipop-cell{display:flex; flex-direction:column; align-items:center; justify-content:center; gap:3px; width:38px; height:40px; border:1px solid transparent; border-radius:3px; cursor:pointer; color:var(--ink)}
  .ipop-cell:hover{border-color:var(--accent-dim); background:var(--bg)}
  .ipop-cell.sel{border-color:var(--accent); background:var(--bg)}
  .ipop-cell svg{width:20px; height:20px; display:block}
  .ipop-cell .cn{font-size:7px; line-height:1; color:var(--ink-faint); max-width:36px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap}
  .delbtn:hover{border-color:var(--blood-bright)}
  .addbtn{font-size:12px; font-weight:600; color:var(--accent); background:transparent; border:1px dashed var(--accent-dim); border-radius:var(--radius-sm); padding:10px 14px; cursor:pointer; width:100%; margin-top:6px; transition:background .15s}
  .addbtn:hover{background:rgba(59,158,255,.08)}

  .navrow{display:flex; align-items:center; gap:12px; padding:10px 12px; border:1px solid var(--line); border-radius:var(--radius-sm); margin-bottom:6px; background:var(--panel); cursor:pointer; transition:border-color .15s,background .15s}
  .navrow:hover{border-color:var(--accent-dim)}
  .navrow.sel{border-color:var(--accent); background:rgba(59,158,255,.08)}
  .navbtn{width:18px; height:18px; flex:none; border:1px solid var(--ink-faint); border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:11px; color:var(--bg); line-height:1}
  .navrow:not(.sel) .navbtn{color:var(--ink-faint); background:transparent}
  .navrow.sel .navbtn{background:var(--accent); border-color:var(--accent); color:#fff}
  .navname{flex:1; min-width:0; color:var(--ink); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-size:13px; font-weight:500}
  .navrow.sel .navname{color:var(--accent)}
  .navtag{font-size:10px; color:var(--ink-faint); border:1px solid var(--line); border-radius:20px; padding:2px 8px; flex:none}
  .navdist{color:var(--ink-dim); font-size:13px; min-width:48px; text-align:right; flex:none; font-variant-numeric:tabular-nums}

  .toast-wrap{position:fixed;top:12px;right:16px;z-index:2000;display:flex;flex-direction:column;gap:8px;pointer-events:none}
  .toast{padding:10px 14px;border-radius:var(--radius-sm);background:var(--panel2);border:1px solid var(--line);box-shadow:var(--shadow);font-size:13px;color:var(--ink);animation:toastIn .2s ease}
  .toast.ok{border-color:rgba(61,214,140,.5);color:var(--good)}
  @keyframes toastIn{from{opacity:0;transform:translateY(-6px)}to{opacity:1;transform:none}}
  .save-stamp{font-size:11px;color:var(--ink-faint);margin-left:8px}
  .save-stamp.recent{color:var(--good)}
  body.offline .aside-inner{opacity:.45;filter:grayscale(.25)}
  .stale-banner{font-size:11px;color:var(--blood-bright);padding:8px 10px;border:1px solid rgba(229,83,75,.35);border-radius:var(--radius-sm);background:rgba(229,83,75,.08);margin-bottom:12px}
  .stale-banner[hidden]{display:none}
  .char-chip{font-size:12px;color:var(--ink-dim);padding:6px 12px;border-radius:20px;border:1px solid var(--line);background:var(--panel2)}
  .char-chip b{color:var(--ink);font-weight:600}
  .header-quick{display:flex;gap:6px;align-items:center}
  .hq{font-size:11px;font-weight:600;padding:5px 10px;border-radius:20px;border:1px solid var(--line);background:var(--panel2);color:var(--ink-dim);cursor:pointer;white-space:nowrap}
  .hq.on{color:var(--good);border-color:rgba(61,214,140,.45)}
  .hq.off{color:var(--ink-faint)}
  .sidebar-toggle{font-size:12px;padding:6px 10px;border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--panel2);color:var(--ink-dim);cursor:pointer}
  .hint-toggle{font-size:12px;font-weight:600;color:var(--accent);background:none;border:none;cursor:pointer;padding:0;margin-bottom:8px}
  .hint-toggle:hover{text-decoration:underline}
  .hint-body{font-size:12px;color:var(--ink-dim);line-height:1.55;margin-bottom:10px}
  .hint-body[hidden]{display:none}
  .hint-oneline{font-size:12px;color:var(--ink-faint);margin-bottom:8px}
  .settings-layout{display:grid;grid-template-columns:160px 1fr;gap:16px;align-items:start}
  .settings-nav{display:flex;flex-direction:column;gap:4px;position:sticky;top:0}
  .settings-nav button{font-size:12px;font-weight:500;text-align:left;padding:8px 12px;border:1px solid transparent;border-radius:var(--radius-sm);background:transparent;color:var(--ink-dim);cursor:pointer}
  .settings-nav button:hover{color:var(--ink);background:var(--panel)}
  .settings-nav button.on{color:var(--ink);background:var(--panel);border-color:var(--line)}
  .settings-section[hidden]{display:none}
  .tally .t.clickable{cursor:pointer;transition:border-color .15s,background .15s}
  .tally .t.clickable:hover{border-color:var(--accent-dim);background:var(--panel2)}
  .drmatch{font-size:10px;color:var(--ink-faint);flex:none;min-width:52px;text-align:right;font-variant-numeric:tabular-nums}
  .dr-dup,.dr-drag{font-size:10px;padding:4px 7px}
  .drrow.dr-new{animation:drFlash 1.2s ease}
  @keyframes drFlash{0%,100%{box-shadow:none}50%{box-shadow:0 0 0 2px var(--accent)}}
  .drrow.drag-over{border-color:var(--accent);background:rgba(59,158,255,.1)}
  .zone-types-box{max-height:320px;margin-top:8px}
  .zt-tier{border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--panel2);margin-bottom:8px;padding:8px 10px}
  .zt-tier-h{display:flex;align-items:center;gap:8px;cursor:pointer;font-weight:600;font-size:12px;color:var(--ink);margin-bottom:4px}
  .zt-tier-h .zt-caret{color:var(--ink-faint);font-size:10px;width:10px}
  .zt-tier-grp{display:flex;align-items:center;gap:10px;font-size:11px;color:var(--ink-faint);margin:4px 0 6px;padding-left:18px}
  .zt-tier-grp label{display:inline-flex;align-items:center;gap:4px;cursor:pointer;user-select:none}
  .zt-row{display:flex;align-items:center;gap:8px;font-size:12px;padding:3px 0 3px 18px;color:var(--ink-dim)}
  .zt-row label{display:inline-flex;align-items:center;gap:3px;cursor:pointer;user-select:none;font-size:11px;color:var(--ink-faint)}
  .zt-count{font-size:11px;color:var(--ink-faint);min-width:28px}
  .zt-label{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
  .zt-zone{font-size:10px;color:var(--ink-faint)}
  .dr-search-row{margin:0 0 10px}
  .hkctl{display:flex;align-items:center;gap:6px;flex-wrap:wrap}
  .pad-menu{display:flex;flex-wrap:wrap;gap:4px;margin:4px 0;padding:6px;background:var(--bg-2);border:1px solid var(--line-soft);border-radius:6px}
  .hk-display{min-width:64px;padding:6px 12px;font-weight:600;color:var(--ink);background:var(--bg);border:1px solid var(--line);border-radius:var(--radius-sm);font-size:12px;text-align:center}
  .hkctl .chip.binding{color:var(--accent);border-color:var(--accent);animation:drFlash 1s ease infinite}
  .dr-preview-lg{width:36px;height:36px;display:flex;align-items:center;justify-content:center;margin-bottom:10px;color:var(--ink)}
  .dr-preview-lg svg{width:32px;height:32px}
  .live-actions{display:flex;gap:6px;flex-wrap:wrap}
  .live-actions button{font-size:11px;padding:4px 10px}
  .lm-dist{flex:none;font-size:12px;color:var(--ink-dim);min-width:44px;text-align:right;font-variant-numeric:tabular-nums}
  .lm-suggest-wrap{position:relative}
  @media(max-width:960px){
    .body{grid-template-columns:1fr}
    aside{border-right:none;border-bottom:1px solid var(--line);max-height:42vh}
    .settings-layout{grid-template-columns:1fr}
    .settings-nav{flex-direction:row;flex-wrap:wrap;position:static}
  }
</style>
</head>
<body>
<a id="updateBanner" href="#" target="_blank" rel="noopener" hidden
   style="display:none;align-items:center;gap:10px;padding:10px 20px;margin:0;background:linear-gradient(90deg,#3b9eff,#56d4e8);color:#fff;font-weight:600;font-size:13px;text-decoration:none">
  <span>&#x2B06; Update available</span><span id="updateMsg" style="font-weight:400;opacity:.9"></span><span style="margin-left:auto;text-decoration:underline;opacity:.95">Download &rarr;</span>
</a>
<div class="toast-wrap" id="toastWrap"></div>
<div class="shell">
  <header>
    <button type="button" class="sidebar-toggle" id="sidebarToggle" title="Toggle sidebar">☰</button>
    <div class="mark">
      <h1><span>POE2</span>Radar</h1>
    </div>
    <div class="hgap"></div>
    <div class="char-chip" id="charChip" hidden><b id="charName">—</b> · lvl <span id="charLvl">—</span></div>
    <div class="area-chip" id="areaChip">— <b>·</b></div>
    <div class="header-quick" id="headerQuick">
      <button type="button" class="hq" id="hqMap" title="Large map open">Map —</button>
      <button type="button" class="hq" id="hqFlask" title="Auto-flask status (F8 in-game)">Flask —</button>
      <button type="button" class="hq" id="hqJunk" title="Toggle map clutter filter">Clutter —</button>
    </div>
    <div class="conn" id="conn"><span class="dot"></span><span id="connTxt">Offline</span></div>
  </header>

  <div class="body">
    <aside>
      <div class="aside-inner">
      <div class="stale-banner" id="staleBanner" hidden>Disconnected — vitals may be stale. <span id="staleTime"></span></div>
      <div class="vital">
        <div class="vlabel"><span>Life</span><span class="num" id="hpNum">—</span></div>
        <div class="bar hp"><i id="hpBar" style="width:0"></i></div>
      </div>
      <div class="vital">
        <div class="vlabel"><span>Energy Shield</span><span class="num" id="esNum">—</span></div>
        <div class="bar es"><i id="esBar" style="width:0"></i></div>
      </div>
      <div class="vital">
        <div class="vlabel"><span>Mana</span><span class="num" id="mpNum">—</span></div>
        <div class="bar mana"><i id="mpBar" style="width:0"></i></div>
      </div>

      <div class="sect">Zone</div>
      <div class="kv"><span>Area</span><span id="kAreaName">—</span></div>
      <div class="kv"><span>Area code</span><span id="kArea">—</span></div>
      <div class="kv"><span>Act / Level</span><span id="kAlvl">—</span></div>
      <div class="kv"><span>Map open</span><span id="kMap">—</span></div>
      <div class="kv"><span>Auto-flask</span><span id="kFlask">—</span></div>
      <div id="zoneNotes" class="znotes" hidden></div>

      <div class="sect">Census</div>
      <div class="tally">
        <div class="t clickable" id="tallyEnt" title="Open Live tab"><div class="n" id="cEnt">0</div><div class="l">Entities</div></div>
        <div class="t clickable" id="tallyPoi" title="Open Live · POI"><div class="n" id="cPoi">0</div><div class="l">Points of Int.</div></div>
        <div class="t clickable" id="tallyMon" title="Open Live · Monsters"><div class="n" id="cMon">0</div><div class="l">Monsters</div></div>
        <div class="t clickable" id="tallyLm" title="Open Landmarks"><div class="n" id="cLm">0</div><div class="l">Landmarks</div></div>
      </div>
      <div style="height:24px"></div>
      </div>
    </aside>

    <main>
      <div class="tabs">
        <button class="tab on" data-tab="filters" title="1">Rules</button>
        <button class="tab" data-tab="live" title="2">Live</button>
        <button class="tab" data-tab="database" title="3">Database</button>
        <button class="tab" data-tab="landmarks" title="4">Landmarks</button>
        <button class="tab" data-tab="atlas" title="5">Atlas</button>
        <button class="tab" data-tab="settings" title="6">Settings</button>
      </div>

      <section class="view" data-view="filters">
        <div class="panel-grid">
          <div class="card" style="grid-column:1/-1">
            <h3>How visibility works</h3>
            <ol class="rules-compare vis-order">
              <li><b>Never-show patterns</b> — separate list at the bottom of this tab. Matched entities are removed everywhere (map, Live list, paths) before rules run.</li>
              <li><b>Radar rules</b> — each entity gets the first <b>active</b> rule that matches (top to bottom). <b>Paused</b> skips that rule so the next one can match. <b>Don&rsquo;t show on map</b> on a rule hides matches; lower rules never apply.</li>
            </ol>
          </div>
          <div class="card" style="grid-column:1/-1">
            <h3>Types in this zone <span class="tag" id="ztAreaTag">&middot; zone type overrides</span></h3>
            <p class="hint-oneline">Show on map &middot; Path. Overrides apply to this zone type only (not global rules).</p>
            <div class="controls" style="margin:8px 0 0">
              <input type="search" id="typeSearch" placeholder="Search types… (Shift+/)" style="flex:1">
            </div>
            <div id="zoneTypesHost" class="scrollbox zone-types-box"><div class="hint-row">Loading…</div></div>
          </div>
          <div class="card" style="grid-column:1/-1">
            <h3>Radar rules <span class="tag">&middot; first active match wins</span><span class="save-stamp" id="stampRules"></span></h3>
            <p class="hint-oneline">Reorder with ▲/▼ or drag. Match on name, type, rarity, and more — blank fields mean &ldquo;any&rdquo;.</p>
            <button type="button" class="hint-toggle" data-hint="hintRules">Rule fields ▾</button>
            <div class="hint-body" id="hintRules" hidden><b>Paused</b> (switch off) — rule is ignored; try the next rule below. <b>Don&rsquo;t show on map</b> — matched entities are hidden on the radar (lower rules won&rsquo;t run). Otherwise the rule sets icon, color, label, and path target.</div>
            <div class="controls" style="margin:0 0 10px">
              <button class="addbtn" id="drExport" style="width:auto;margin:0;padding:8px 14px">Export rules…</button>
              <button class="addbtn" id="drImport" style="width:auto;margin:0;padding:8px 14px">Import rules…</button>
            </div>
            <div class="dr-search-row">
              <input type="search" id="drSearch" placeholder="Search rules by name… (press /)" style="width:100%">
            </div>
            <div id="drList"></div>
            <div class="controls" style="margin:8px 0 0">
              <button class="addbtn" id="drPick" style="width:auto;margin:0;padding:9px 16px">+ Add from game data…</button>
              <button class="addbtn" id="drAdd" style="width:auto;margin:0;padding:9px 16px">+ Add blank rule</button>
            </div>
          </div>
          <div class="card" style="grid-column:1/-1">
            <h3>Never show <span class="tag">&middot; always hidden patterns</span></h3>
            <p class="hint-oneline">Metadata patterns removed before radar rules — map, Live tab, and paths. Use for permanent noise (FX, daemons, cracks).</p>
            <button type="button" class="hint-toggle" data-hint="hintHidden">Patterns &amp; wildcards ▾</button>
            <div class="hint-body" id="hintHidden" hidden>Substring or glob: <code>*</code> matches any run, <code>?</code> matches one character. F5 in-game adds the entity type under your cursor here.</div>
            <div id="hideList" class="controls" style="margin:8px 0 14px"></div>
            <div class="controls" style="margin:0">
              <input type="search" id="hidePattern" placeholder="e.g. AbyssCrack, *Daemon*">
              <button class="addbtn" id="hideAdd" style="width:auto;margin:0;padding:8px 16px">+ Add pattern</button>
            </div>
          </div>
        </div>
        <div style="margin-top:18px; height:14px"><span class="saved" id="savedMsgF">&#10003; saved</span></div>
      </section>

      <section class="view" data-view="live" hidden>
        <div class="panel-grid">
          <div class="card" style="grid-column:1/-1">
            <h3>Live entities <span class="tag">&middot; in current zone</span><span id="liveCount" class="tag">—</span></h3>
            <p class="hint-oneline">What&rsquo;s in the zone right now — filter, set path target, or create a radar rule.</p>
            <div class="controls" style="margin:8px 0 10px">
              <input type="search" id="liveSearch" placeholder="Search name or path… (press /)">
              <label class="chip"><input type="checkbox" id="liveAlive" checked> Alive only</label>
              <input class="numin" type="number" id="liveRadius" placeholder="radius" min="0" step="10" style="width:80px" title="Max grid distance (0 = all)">
              <span class="tag" id="liveNavCount">0 nav targets</span>
              <button class="chip" id="liveNavClear">Clear nav</button>
            </div>
            <div class="filter-btns" id="liveCatFilters"></div>
            <div class="scrollbox">
              <table><thead><tr><th title="{{H.LiveColName}}">Name</th><th title="{{H.LiveColCategory}}">Category</th><th title="{{H.LiveColRarity}}">Rarity</th><th title="{{H.LiveColDist}}">Dist</th><th title="{{H.LiveColHp}}">HP</th><th title="{{H.LiveColRule}}">Matching rule</th><th></th></tr></thead>
              <tbody id="liveBody"></tbody></table>
            </div>
          </div>
        </div>
      </section>

      <section class="view" data-view="database" hidden>
        <div class="panel-grid">
          <div class="card" style="grid-column:1/-1">
            <h3>Entity database <span class="tag">&middot; static GGPK paths</span></h3>
            <p class="hint-oneline">Full game entity catalog — use <b>+ Rule</b> to create a radar rule from a path.</p>
            <div class="controls" style="margin:8px 0 10px">
              <input type="search" id="dbSearch" placeholder="Search paths (Waypoint, Ritual, Chest…)">
              <label class="chip"><input type="checkbox" id="dbHideJunk" checked> Hide clutter paths</label>
              <label class="chip"><input type="checkbox" id="dbNoRule"> Without rule</label>
              <span class="tag" id="dbCount">&mdash;</span>
            </div>
            <div class="filter-btns" id="dbCatFilters"></div>
            <div class="scrollbox">
              <table><thead><tr><th title="{{H.DbColCategory}}">Category</th><th title="{{H.DbColPath}}">Path</th><th></th></tr></thead>
              <tbody id="dbBody"></tbody></table>
            </div>
          </div>
        </div>
      </section>

      <section class="view" data-view="landmarks" hidden>
        <div class="panel-grid">
          <div class="card" style="grid-column:1/-1">
            <h3>Landmarks <span class="tag">&middot; curated labels</span><span class="save-stamp" id="stampLm"></span></h3>
            <p class="hint-oneline">Rename wrong labels, add custom entries, import/export JSON.</p>
            <button type="button" class="hint-toggle" data-hint="hintLm">More about landmarks ▾</button>
            <div class="hint-body" id="hintLm" hidden>Built-in map features per area. For tile <i>drawing</i> (icon/color), use a Tile rule on Rules. Distance shown when a live landmark matches the pattern.</div>
            <div class="controls" style="margin:6px 0 12px">
              <input type="search" id="lmSearch" placeholder="filter by area / tile / label…">
              <button class="chip on" id="lmAreaOnly">This area only</button>
              <span style="flex:1"></span>
              <button class="addbtn" id="lmImport" style="width:auto;margin:0;padding:8px 14px">Import…</button>
              <button class="addbtn" id="lmExport" style="width:auto;margin:0;padding:8px 14px">Export</button>
            </div>
            <div id="lmList"></div>
            <div class="mechrow lm-suggest-wrap">
              <div class="top">
                <input class="mname" id="lmArea" placeholder="area (e.g. P2_3, or *)" style="max-width:150px">
                <input class="mname" id="lmPat" placeholder="tile path / pattern" list="lmTileList">
                <datalist id="lmTileList"></datalist>
                <input class="mname" id="lmLabel" placeholder="label">
                <button class="addbtn" id="lmAdd" style="width:auto;margin:0;padding:8px 16px">+ Add</button>
              </div>
            </div>
          </div>
        </div>
        <div style="margin-top:18px; height:14px"><span class="saved" id="savedMsgL">&#10003; saved to config</span></div>
      </section>

      <section class="view" data-view="atlas" hidden>
        <div class="panel-grid">
          <div class="card" style="grid-column:1/-1">
            <h3>Atlas highlights <span class="tag">&middot; route and ring matching maps in-game</span></h3>
            <p class="hint-oneline">Open Atlas in-game — filters auto-refresh while this tab is active.</p>
            <button type="button" class="hint-toggle" data-hint="hintAtlas">Atlas tips ▾</button>
            <div class="hint-body" id="hintAtlas" hidden>Toggle Track to draw node-to-node routes and rings for matching maps; Arrow points from the screen edge when off-screen. F10 in-game inspects a hovered tile.</div>
            <div class="controls" style="margin:6px 0 12px">
              <button class="addbtn" id="atlasRefresh" style="width:auto;margin:0;padding:9px 16px">&#8635; Refresh</button>
              <span style="flex:1"></span>
              <span class="tag" id="atlasStatus">&mdash;</span>
            </div>
            <div class="row" style="margin:0 0 10px;flex-direction:column;align-items:stretch;gap:6px">
              <div class="controls" style="gap:8px;align-items:center">
                <span class="hint-row" style="flex:1"><b id="atlasHlCount">0 active</b> &mdash; click a row to <b>Track</b> (route + ring in-game); click the <b style="color:#e0b341">&#10148;</b> to <b>Arrow</b> (point to it from the screen edge when off-screen). Track without Arrow = route/ring only, no edge arrow.</span>
                <button class="chip on" id="atlasViewRegion" data-view="region">Region</button>
                <button class="chip" id="atlasViewCatalog" data-view="catalog">Catalog</button>
                <button class="chip" id="atlasViewNodes" data-view="nodes">Nodes</button>
                <input type="search" id="atlasSearch" placeholder="search atlas data&hellip;" style="width:180px">
                <input type="search" id="atlasHlFilter" placeholder="search filters&hellip;" style="width:200px">
                <button class="chip" id="atlasHlSelOnly">Selected</button>
                <button class="chip" id="atlasHlClear">Clear</button>
              </div>
              <div id="atlasHlTable" style="max-height:420px;overflow:auto;border:1px solid var(--line);border-radius:6px">
                <span class="hint-row" style="padding:8px;display:block">Open the Atlas in-game + Refresh to list filters.</span>
              </div>
              <div id="atlasList" style="margin-top:10px;max-height:420px;overflow:auto;border:1px solid var(--line);border-radius:6px"></div>
            </div>
          </div>
        </div>
      </section>

      <section class="view" data-view="settings" hidden>
        <div class="settings-layout">
          <nav class="settings-nav" id="settingsNav">
            <button type="button" class="on" data-setsec="setDisplay">Display</button>
            <button type="button" data-setsec="setLoot">Loot values</button>
            <button type="button" data-setsec="setNav">Navigation</button>
            <button type="button" data-setsec="setIcons">Icons</button>
            <button type="button" data-setsec="setHp">HP bars</button>
            <button type="button" data-setsec="setTerrain">Terrain</button>
            <button type="button" data-setsec="setCalib">Calibration</button>
            <button type="button" data-setsec="setAtlas">Atlas</button>
            <button type="button" data-setsec="setFlask">Auto-flask</button>
            <button type="button" data-setsec="setHotkeys">Hotkeys</button>
          </nav>
          <div class="settings-panels">
          <div class="settings-section panel-grid" id="setDisplay">
          <div class="card">
            <h3>Radar Display <span class="save-stamp" id="stampSettings"></span></h3>
            <div class="row"><div class="rl" title="{{H.ShowMonsters}}">Show monsters &amp; entities<small>entity dots (monsters, NPCs, chests, POIs)</small></div>
              <label class="sw"><input type="checkbox" data-set="showMonsters"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ShowTerrain}}">Show terrain<small>walkable-terrain bitmap</small></div>
              <label class="sw"><input type="checkbox" data-set="showTerrain"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ShowPlayerBlip}}">Show player blip<small>dot at your position on the map</small></div>
              <label class="sw"><input type="checkbox" data-set="showPlayerBlip"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AlwaysShowOverlay}}">Always show overlay<small>draw when PoE2 isn&rsquo;t focused; auto-flask stays focus-gated</small></div>
              <label class="sw"><input type="checkbox" data-set="alwaysShowOverlay"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.HideJunk}}">Hide map clutter<small>cosmetic FX, daemons, and noise dots</small></div>
              <label class="sw"><input type="checkbox" data-set="hideJunk"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.CuratedLandmarks}}">Curated landmark names<small>community labels (boss / reward / exits)</small></div>
              <label class="sw"><input type="checkbox" data-set="useCuratedLandmarks"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.LandmarkClusterGap}}">Landmark cluster gap<small>max tile distance to merge nearby tile markers (0 = no clustering)</small></div>
              <input class="numin" type="number" step="1" min="0" max="64" data-set="landmarkClusterGap"></div>
            <div class="row"><div class="rl" title="{{H.LowImpactMode}}">Low impact mode<small>favor lower memory-read cadence when idle or unfocused</small></div>
              <label class="sw"><input type="checkbox" data-set="lowImpactMode"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.FpsCap}}">Overlay FPS cap<small>15&ndash;360; lower = less GPU load</small></div>
              <input class="numin" type="number" step="1" min="15" max="360" data-set="fpsCap"></div>
            <div class="row"><div class="rl" title="{{H.LiveRefreshHz}}">Live refresh Hz<small>player position, map UI, vitals, camera: 5&ndash;120</small></div>
              <input class="numin" type="number" step="1" min="5" max="120" data-set="liveRefreshHz"></div>
            <div class="row"><div class="rl" title="{{H.WorldRefreshHz}}">World refresh Hz<small>entities, terrain, landmarks, routes: 1&ndash;60</small></div>
              <input class="numin" type="number" step="1" min="1" max="60" data-set="worldRefreshHz"></div>
            <div class="row"><div class="rl" title="{{H.InactiveRefreshHz}}">Inactive refresh Hz<small>world reads while PoE2 is unfocused and overlay is hidden: 1&ndash;10</small></div>
              <input class="numin" type="number" step="1" min="1" max="10" data-set="inactiveRefreshHz"></div>
            <div class="row"><div class="rl" title="{{H.HpBarRefreshHz}}">HP bar refresh Hz<small>live nameplate HP/position reads: 1&ndash;30</small></div>
              <input class="numin" type="number" step="1" min="1" max="30" data-set="hpBarRefreshHz"></div>
            <div class="row"><div class="rl" title="{{H.MaxLiveHpBars}}">Max live HP bars<small>cap read-heavy nameplates: 0&ndash;256</small></div>
              <input class="numin" type="number" step="1" min="0" max="256" data-set="maxLiveHpBars"></div>
            <div class="row"><div class="rl" title="{{H.SmoothOverlayMotion}}">Smooth overlay motion<small>interpolate visual positions between memory samples</small></div>
              <label class="sw"><input type="checkbox" data-set="smoothOverlayMotion"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.OverlaySmoothingMs}}">Overlay smoothing ms<small>path/player/map transform smoothing: 0&ndash;150</small></div>
              <input class="numin" type="number" step="1" min="0" max="150" data-set="overlaySmoothingMs"></div>
            <div class="row"><div class="rl" title="{{H.ChipSmoothingMs}}">Chip smoothing ms<small>label chip rectangle smoothing: 0&ndash;250</small></div>
              <input class="numin" type="number" step="1" min="0" max="250" data-set="chipSmoothingMs"></div>
            <div class="row"><div class="rl" title="{{H.PixelSnapLabels}}">Pixel snap labels<small>round final text/chip positions to whole pixels</small></div>
              <label class="sw"><input type="checkbox" data-set="pixelSnapLabels"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.OverlayVSync}}">Overlay VSync<small>present overlay frames on the display cadence</small></div>
              <label class="sw"><input type="checkbox" data-set="overlayVSync"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.FpsResourceOverlay}}">FPS / resource overlay<small>tick/render FPS + App CPU/GPU/RAM under POE2Radar nav</small></div>
              <label class="sw"><input type="checkbox" data-set="showFpsOverlay"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ExtendedPerfStats}}">Extended perf stats<small>extra timing/read lines under the nav menu</small></div>
              <label class="sw"><input type="checkbox" data-set="showPerfStats"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.MetricsRefreshHz}}">Metrics refresh Hz<small>CPU/RAM sampling cadence when metrics HUD is enabled: 1&ndash;10</small></div>
              <input class="numin" type="number" step="1" min="1" max="10" data-set="metricsRefreshHz"></div>
            <div class="row"><div class="rl" title="{{H.GpuMetricsSeconds}}">GPU metrics seconds<small>GPU/VRAM sampling interval when metrics HUD is enabled: 1&ndash;30</small></div>
              <input class="numin" type="number" step="1" min="1" max="30" data-set="gpuMetricsRefreshSeconds"></div>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setLoot" hidden>
          <div class="card">
            <h3>Ground loot values (poe.ninja)</h3>
            <div class="row"><div class="rl" title="{{H.LootEnabled}}">Enabled</div>
              <label class="sw"><input type="checkbox" data-set="groundItemsEnabled"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.LootLeague}}">League override<small>blank = auto-detect from game</small></div>
              <input class="numin" type="text" data-set="groundItemsLeague" style="width:220px"></div>
            <div class="row"><div class="rl" title="{{H.LootHighlightMin}}">Highlight min (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="groundItemsHighlightMinEx"></div>
            <div class="row"><div class="rl" title="{{H.LootUniqueFloor}}">Unique floor (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="groundItemsUniqueMinEx"></div>
            <div class="row"><div class="rl" title="{{H.LootCurrencyFloor}}">Currency floor (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="groundItemsCurrencyMinEx"></div>
            <div class="row"><div class="rl" title="{{H.LootOtherFloor}}">Other floor (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="groundItemsOtherMinEx"></div>
            <div class="row"><div class="rl" title="{{H.LootMinQty}}">Min listing qty<small>skip low-confidence rows</small></div>
              <input class="numin" type="number" step="1" min="0" data-set="groundItemsMinQuantity"></div>
            <div class="row"><div class="rl" title="{{H.LootAnchorTags}}">Anchor to loot tags</div>
              <label class="sw"><input type="checkbox" data-set="groundItemsAnchorValuesToTags"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl">Price cache</div>
              <span id="priceStatus" class="muted">loading…</span>
              <button type="button" id="priceRefreshBtn" class="btn sm">Refresh</button></div>
          </div>
          <div class="card">
            <h3>Runeshape monoliths</h3>
            <div class="row"><div class="rl" title="{{H.MonolithEnabled}}">Enabled</div>
              <label class="sw"><input type="checkbox" data-set="monolithsEnabled"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.MonolithHighlightMin}}">Highlight min (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="monolithsHighlightMinEx"></div>
            <div class="row"><div class="rl" title="{{H.MonolithMinReward}}">Min reward (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="monolithsMinRewardEx"></div>
            <div class="row"><div class="rl" title="{{H.MonolithMinValue}}">Min monolith value (ex)</div>
              <input class="numin" type="number" step="0.1" min="0" data-set="monolithsMinValueEx"></div>
            <div class="row"><div class="rl" title="{{H.MonolithHideCollected}}">Hide collected</div>
              <label class="sw"><input type="checkbox" data-set="monolithsHideCollected"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.MonolithShowMapLabel}}">Show map label</div>
              <label class="sw"><input type="checkbox" data-set="monolithsShowMapLabel"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.MonolithShowPanel}}">Show reward panel</div>
              <label class="sw"><input type="checkbox" data-set="monolithsShowPanel"><span class="track"></span><span class="knob"></span></label></div>
          </div>
          <div class="card">
            <h3>Ritual Helper</h3>
            <div class="row"><div class="rl" title="{{H.RitualEnabled}}">Enabled</div>
              <label class="sw"><input type="checkbox" data-set="ritualHelperEnabled"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.RitualShowPrices}}">Show prices</div>
              <label class="sw"><input type="checkbox" data-set="ritualHelperShowPrices"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.RitualMinDisplay}}">Min display (ex)</div>
              <input class="numin" type="number" step="1" min="0" data-set="ritualHelperMinDisplayExalted"></div>
            <div class="row"><div class="rl" title="{{H.RitualPriceSource}}">Price source</div>
              <select class="numin selin" data-set="ritualHelperPriceSource">
                <option value="0">poe.ninja</option>
                <option value="1">poe2scout</option>
              </select></div>
            <div class="row"><div class="rl" title="{{H.RitualLeague}}">League override</div>
              <input class="numin" type="text" data-set="ritualHelperLeague" style="width:220px"></div>
            <div class="row"><div class="rl" title="{{H.RitualRefresh}}">Refresh interval (min)</div>
              <input class="numin" type="number" step="1" min="1" max="120" data-set="ritualHelperRefreshIntervalMin"></div>
            <div class="row"><div class="rl" title="{{H.RitualReadHz}}">Read rate (Hz)</div>
              <input class="numin" type="number" step="1" min="1" max="20" data-set="ritualHelperReadHz"></div>
            <div class="row"><div class="rl" title="{{H.RitualDiagnose}}">Diagnose pricing</div>
              <label class="sw"><input type="checkbox" data-set="ritualHelperDiagnosePricing"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.RitualAlert}}">Value alert</div>
              <label class="sw"><input type="checkbox" data-set="ritualHelperPlayValueAlert"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.RitualAlertDiv}}">Alert from (div)</div>
              <input class="numin" type="number" step="0.1" min="0.1" data-set="ritualHelperAlertMinDivine"></div>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setNav" hidden>
          <div class="card">
            <h3>Navigation &amp; paths</h3>
            <div class="row"><div class="rl" title="{{H.ShowPathWorld}}">Path on ground<small>world-projected route when the large map is closed</small></div>
              <label class="sw"><input type="checkbox" data-set="showPathWorld"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ShowGroundWaypoints}}">Ground waypoints<small>world-screen breadcrumbs (requires path on ground)</small></div>
              <label class="sw"><input type="checkbox" data-set="showGroundWaypoints"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ShowPathMap}}">Path on large map<small>route overlay when Tab map is open</small></div>
              <label class="sw"><input type="checkbox" data-set="showPathMap"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.ShowPathMinimap}}">Path on minimap<small>route inside the corner minimap</small></div>
              <label class="sw"><input type="checkbox" data-set="showPathMinimap"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AutoPathNearest}}">Auto-path to nearest targets<small>continuously path to nearest entities whose rule has &ldquo;Show path to this&rdquo; (F6 picks manually too)</small></div>
              <label class="sw"><input type="checkbox" data-set="autoPathNavigable"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.DetectionRadius}}">Detection radius<small>max grid distance for entity dots, nav, and API list (0 = unlimited)</small></div>
              <input class="numin" type="number" step="10" min="0" max="2000" data-set="entityDrawRadiusGrid"></div>
            <div class="row"><div class="rl" title="{{H.ShowAllMonsters}}">Show all monsters<small>include normal/magic grey clutter on the radar (off = curated important-only view)</small></div>
              <label class="sw"><input type="checkbox" data-set-inv="importantOnly"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.NavMenuCorner}}">Nav menu corner<small>where the in-game nav dropdown is pinned</small></div>
              <select class="numin selin" data-set="navMenuCorner">
                <option value="TopLeft">Top left</option>
                <option value="TopRight">Top right</option>
                <option value="BottomLeft">Bottom left</option>
                <option value="BottomRight">Bottom right</option>
              </select></div>
            <p class="hint-oneline" style="margin-top:8px">Per-entity path targets: Rules tab (&ldquo;Show path to this&rdquo;) or Live tab (Nav). Toggle auto-path in-game with the hotkey below.</p>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setIcons" hidden>
          <div class="card" style="grid-column:1/-1">
            <h3>Default icon styles <span class="tag">&middot; category defaults</span></h3>
            <div class="row"><div class="rl" title="{{H.GlobalIconScale}}">Global icon scale<small>multiplier on icons.png sprite size (per-rule size stacks on top)</small></div>
              <input class="numin" type="number" step="0.05" min="0.25" max="4" data-set="globalIconScale"></div>
            <div id="iconStyles"></div>
          </div>
          <div class="card" style="grid-column:1/-1">
            <h3>Mechanic overrides <span class="tag">&middot; metadata matchers</span></h3>
            <p class="hint-oneline">When metadata matches, draw this icon instead of the category default.</p>
            <div id="mechList"></div>
            <button class="addbtn" id="mechAdd" style="width:auto;margin-top:10px;padding:8px 16px">+ Add mechanic rule</button>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setHp" hidden>
          <div class="card">
            <h3>Monster HP Bars <span class="tag">&middot; by rarity</span></h3>
            <p class="hint-oneline">Toggle On per rarity; geometry fields set bar size per tier.</p>
            <div class="hpgrid">
              <span class="hph" title="{{H.HpColOn}}">On</span><span class="hph">Rarity</span><span class="hph" title="{{H.HpColWidth}}">Width</span><span class="hph" title="{{H.HpColBorder}}">Border</span><span class="hph" title="{{H.HpColThick}}">Thick</span>
              <input type="checkbox" data-set="hpBarNormal" title="{{H.HpNormal}}">
              <span class="hpr" title="{{H.HpNormal}}">Normal</span>
              <input class="numin" type="number" step="1" min="4" data-hp="widthNormal">
              <input type="color" class="i-color" data-hpcolor="borderColorNormal">
              <input class="numin" type="number" step="0.5" min="0" max="20" data-hp="borderNormal">
              <input type="checkbox" data-set="hpBarMagic" title="{{H.HpMagic}}">
              <span class="hpr" style="color:var(--magic)" title="{{H.HpMagic}}">Magic</span>
              <input class="numin" type="number" step="1" min="4" data-hp="widthMagic">
              <input type="color" class="i-color" data-hpcolor="borderColorMagic">
              <input class="numin" type="number" step="0.5" min="0" max="20" data-hp="borderMagic">
              <input type="checkbox" data-set="hpBarRare" title="{{H.HpRare}}">
              <span class="hpr" style="color:var(--rare)" title="{{H.HpRare}}">Rare</span>
              <input class="numin" type="number" step="1" min="4" data-hp="widthRare">
              <input type="color" class="i-color" data-hpcolor="borderColorRare">
              <input class="numin" type="number" step="0.5" min="0" max="20" data-hp="borderRare">
              <input type="checkbox" data-set="hpBarUnique" title="{{H.HpUnique}}">
              <span class="hpr" style="color:var(--unique)" title="{{H.HpUnique}}">Unique</span>
              <input class="numin" type="number" step="1" min="4" data-hp="widthUnique">
              <input type="color" class="i-color" data-hpcolor="borderColorUnique">
              <input class="numin" type="number" step="0.5" min="0" max="20" data-hp="borderUnique">
            </div>
            <div class="hpshared">
              <label title="{{H.HpUseTextures}}">Textures<input type="checkbox" data-hpcheck="useTextures"></label>
              <label title="{{H.HpHeight}}">Height<input class="numin" type="number" step="1" min="1" max="30" data-hp="height"></label>
              <label title="{{H.HpOffsetX}}">Offset X<input class="numin" type="number" step="1" data-hp="offsetX"></label>
              <label title="{{H.HpOffsetY}}">Offset Y<input class="numin" type="number" step="1" data-hp="offsetY"></label>
              <label>ES color<input type="color" class="i-color" data-hpcolor="energyShieldColor"></label>
            </div>
            <p class="hint-oneline" style="margin-top:8px">Offset Y negative = above the mob; thickness 0 = no border.</p>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setTerrain" hidden>
          <div class="card">
            <h3>Terrain <span class="tag">&middot; walkable overlay</span></h3>
            <div class="row"><div class="rl" title="{{H.TerrainInterior}}">Interior fill<small>wash over walkable cells</small></div>
              <span class="trow-ctl">
                <input type="color" class="i-color" data-tcolor="interiorColor">
                <input type="range" class="op" min="0" max="100" data-topacity="interiorOpacity">
                <span class="opv" data-topv="interiorOpacity">—</span></span></div>
            <div class="row"><div class="rl" style="color:var(--poi)" title="{{H.TerrainEdge}}">Wall edge<small>outlines around rooms</small></div>
              <span class="trow-ctl">
                <input type="color" class="i-color" data-tcolor="edgeColor">
                <input type="range" class="op" min="0" max="100" data-topacity="edgeOpacity">
                <span class="opv" data-topv="edgeOpacity">—</span></span></div>
            <div class="row"><div class="rl" title="{{H.TerrainEdgeDetail}}">ImGui edge detail<small>higher = smoother terrain edge, more draw work</small></div>
              <input class="numin" type="number" step="1" min="1" max="8" data-tnum="imGuiEdgeDetail"></div>
            <div class="row"><div class="rl" title="{{H.TerrainEdgeThickness}}">ImGui edge thickness<small>visibility of terrain edge points</small></div>
              <input class="numin" type="number" step="0.1" min="0.5" max="4" data-tnum="imGuiEdgeThickness"></div>
            <p class="hint-oneline" style="margin-top:8px">Edits rebuild the terrain bitmap.</p>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setCalib" hidden>
          <div class="card">
            <h3>Map Calibration</h3>
            <div class="row"><div class="rl" title="{{H.LargeMapScale}}">Large-map scale base<small>GameHelper-style diagonal/zoom multiplier</small></div>
              <input class="numin" type="number" step="0.0001" min="0.01" data-set="largeMapScaleMultiplier"></div>
            <div class="row"><div class="rl" title="{{H.MinimapScale}}">Scale multiplier<small>projection scale of the map overlay</small></div>
              <input class="numin" type="number" step="0.01" data-set="scaleMul"></div>
            <div class="row"><div class="rl" title="{{H.OffsetX}}">Offset X</div><input class="numin" type="number" step="1" data-set="offX"></div>
            <div class="row"><div class="rl" title="{{H.OffsetY}}">Offset Y</div><input class="numin" type="number" step="1" data-set="offY"></div>
            <p class="hint-oneline" style="margin-top:8px">Changes apply live.</p>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setAtlas" hidden>
          <div class="card">
            <h3>Atlas overlay</h3>
            <p class="hint-oneline">In-game atlas map drawing. Highlights are on the Atlas tab.</p>
            <div class="row"><div class="rl" title="{{H.AtlasShowAllNodes}}">Show all on-screen nodes<small>when Track filters are active, only tracked nodes draw</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasShowOnScreenNodes"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasShowNames}}">Show map names<small>label on-screen tiles with map name</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasShowNames"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasRevealFog}}">Reveal fog<small>draw fogged nodes at full opacity with a cool tint</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasRevealFog"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasOffScreenArrows}}">Off-screen arrows<small>edge arrows for arrow-tagged highlights (e.g. Citadels)</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasOffScreenArrows"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasShowRoute}}">Show F10 route<small>draw path through the atlas node graph</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasShowRoute"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasRouteFromCurrent}}">Route from current tile<small>when no F10 start is set, use your live atlas position</small></div>
              <label class="sw"><input type="checkbox" data-set="atlasUseCurrentStart"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasSearchQuery}}">Search query<small>comma-separated map/content search; routes matches</small></div>
              <input class="numin" type="text" data-set="atlasSearchQuery" style="width:220px"></div>
            <div class="row"><div class="rl" title="{{H.AtlasHideCompleted}}">Hide completed maps</div>
              <label class="sw"><input type="checkbox" data-set="atlasHideCompletedMaps"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasHideNotAccessible}}">Hide inaccessible maps</div>
              <label class="sw"><input type="checkbox" data-set="atlasHideNotAccessibleMaps"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasHideAvailable}}">Hide available maps</div>
              <label class="sw"><input type="checkbox" data-set="atlasHideAvailableMaps"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasBiomeBorders}}">Biome borders</div>
              <label class="sw"><input type="checkbox" data-set="atlasShowBiomeBorders"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasContentBadges}}">Content badges</div>
              <label class="sw"><input type="checkbox" data-set="atlasShowContentBadges"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasContentCount}}">Content count pips</div>
              <label class="sw"><input type="checkbox" data-set="atlasShowContentCount"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasRouteChevrons}}">Route chevrons</div>
              <label class="sw"><input type="checkbox" data-set="atlasShowRouteChevrons"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.AtlasIconScale}}">Atlas icon scale</div>
              <input class="numin" type="number" step="0.05" min="0.25" max="4" data-set="atlasIconScale"></div>
            <div class="row"><div class="rl" title="{{H.AtlasLabelScale}}">Atlas label scale</div>
              <input class="numin" type="number" step="0.05" min="0.5" max="3" data-set="atlasLabelScale"></div>
            <div class="row"><div class="rl" title="{{H.AtlasRouteThickness}}">Route thickness</div>
              <input class="numin" type="number" step="0.5" min="1" max="8" data-set="atlasRouteLineThickness"></div>
            <div class="row"><div class="rl" title="{{H.AtlasChevronSpacing}}">Chevron spacing</div>
              <input class="numin" type="number" step="1" min="8" max="80" data-set="atlasRouteChevronSpacing"></div>
            <div class="row"><div class="rl" title="{{H.AtlasLanguage}}">Atlas language<small>currently English; ready for translated catalogs</small></div>
              <input class="numin" type="text" data-set="atlasLanguage" style="width:150px"></div>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setFlask" hidden>
          <div class="card">
            <h3>Auto-Flask</h3>
            <div class="row"><div class="rl" title="{{H.FlaskTriggerPool}}">Life flask triggers on<small>which pool the life flask key watches &mdash; ES is ignored if your build has none</small></div>
              <select class="numin selin" data-set="lifeFlaskMode">
                <option value="Health">Health %</option>
                <option value="EnergyShield">Energy Shield %</option>
                <option value="Either">Either (HP or ES)</option>
              </select></div>
            <div class="row"><div class="rl" title="{{H.FlaskLifeThreshold}}">Life threshold %<small>tap life flask below this Life %</small></div>
              <input class="numin" type="number" step="1" min="0" max="100" data-set="lifeThresholdPct"></div>
            <div class="row"><div class="rl" title="{{H.FlaskEsThreshold}}">ES threshold %<small>tap life flask below this Energy Shield % (ES / Either modes)</small></div>
              <input class="numin" type="number" step="1" min="0" max="100" data-set="esThresholdPct"></div>
            <div class="row"><div class="rl" title="{{H.FlaskManaThreshold}}">Mana threshold %<small>tap mana flask below this Mana %</small></div>
              <input class="numin" type="number" step="1" min="0" max="100" data-set="manaThresholdPct"></div>
            <div class="row"><div class="rl" title="{{H.FlaskLifeKey}}">Life flask key</div>
              <input class="numin keyin" type="text" maxlength="1" data-set="lifeKey"></div>
            <div class="row"><div class="rl" title="{{H.FlaskManaKey}}">Mana flask key</div>
              <input class="numin keyin" type="text" maxlength="1" data-set="manaKey"></div>
            <div class="row"><div class="rl" title="{{H.FlaskLifeCooldown}}">Life cooldown<small>min ms between life taps</small></div>
              <input class="numin" type="number" step="100" min="0" data-set="lifeCooldownMs"></div>
            <div class="row"><div class="rl" title="{{H.FlaskManaCooldown}}">Mana cooldown<small>min ms between mana taps</small></div>
              <input class="numin" type="number" step="100" min="0" data-set="manaCooldownMs"></div>
            <p class="hint-oneline" style="margin-top:8px">F8 toggles in-game. Status: <span id="flaskState">&mdash;</span></p>
          </div>
          </div>
          <div class="settings-section panel-grid" id="setHotkeys" hidden>
          <div class="card">
            <h3>In-game hotkeys</h3>
            <p class="hint-oneline">Click <b>Bind</b>, then press a key. Use <b>Pad</b> chips for Xbox buttons, or bind pad buttons in overlay settings (Entities tab).</p>
            <div class="row"><div class="rl" title="{{H.GamepadHotkeys}}">Gamepad hotkeys<small>XInput / Xbox controller on player slot 0–3</small></div>
              <label class="sw"><input type="checkbox" data-set="gamepadHotkeysEnabled"><span class="track"></span><span class="knob"></span></label></div>
            <div class="row"><div class="rl" title="{{H.PadSlot}}">Pad player slot<small>0 = first controller</small></div>
              <input class="numin" type="number" step="1" min="0" max="3" data-set="gamepadUserIndex"></div>
            <div class="row"><div class="rl" title="{{H.HkHideEntity}}">Never show under cursor<small>hover entity → hide type globally</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="hideEntityHotkey">F5</span>
                <button type="button" class="chip" data-hk-bind="hideEntityHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="hideEntityHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="hideEntityHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkTrackEntity}}">Inspect under cursor<small>print entity info to console</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="trackEntityHotkey">F4</span>
                <button type="button" class="chip" data-hk-bind="trackEntityHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="trackEntityHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="trackEntityHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkAutoPath}}">Auto-path toggle<small>continuous auto-pathing (default F3)</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="autoPathToggleHotkey">F3</span>
                <button type="button" class="chip" data-hk-bind="autoPathToggleHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="autoPathToggleHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="autoPathToggleHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkAddNearest}}">Add nearest path<small>F6 default</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="addNearestPathHotkey">F6</span>
                <button type="button" class="chip" data-hk-bind="addNearestPathHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="addNearestPathHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="addNearestPathHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkClearPaths}}">Clear paths<small>F7 default</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="clearPathsHotkey">F7</span>
                <button type="button" class="chip" data-hk-bind="clearPathsHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="clearPathsHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="clearPathsHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkAutoFlask}}">Auto-flask toggle<small>F8 default</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="autoFlaskToggleHotkey">F8</span>
                <button type="button" class="chip" data-hk-bind="autoFlaskToggleHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="autoFlaskToggleHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="autoFlaskToggleHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkAtlasPick}}">Atlas tile pick<small>F10 default — atlas routing</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="atlasPickHotkey">F10</span>
                <button type="button" class="chip" data-hk-bind="atlasPickHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="atlasPickHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="atlasPickHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkToggleSettings}}">Overlay settings<small>F11 default</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="toggleSettingsHotkey">F11</span>
                <button type="button" class="chip" data-hk-bind="toggleSettingsHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="toggleSettingsHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="toggleSettingsHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkOpenDashboard}}">Open dashboard<small>F12 default (PoE2 focused)</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="openDashboardHotkey">F12</span>
                <button type="button" class="chip" data-hk-bind="openDashboardHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="openDashboardHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="openDashboardHotkey">Clear</button></span></div>
            <div class="row"><div class="rl" title="{{H.HkQuit}}">Quit overlay<small>F9 default</small></div>
              <span class="hkctl"><span class="hk-display" data-hk="quitHotkey">F9</span>
                <button type="button" class="chip" data-hk-bind="quitHotkey">Bind</button>
                <button type="button" class="chip" data-hk-pad="quitHotkey">Pad</button>
                <button type="button" class="chip" data-hk-clear="quitHotkey">Clear</button></span></div>
          </div>
          </div>
          </div>
        </div>
        <div style="margin-top:18px; height:14px"><span class="saved" id="savedMsg">&#10003; saved</span></div>
      </section>

    </main>
  </div>
</div>

<script>
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
let state=null, zone=null;
let activeTab='filters';
let atlasData=null, atlasView='region', atlasSel=new Set(), atlasHl=null, atlasArrow=null, atlasHlSelOnly=false;
let connLive=false, lastTickAt=0, liveEnts=[], liveNavIds=new Set(), liveCatFilter='', livePoiOnly=false, livePoll=null, atlasPoll=null;
let lmLiveDist=new Map();
let _liveEntsCache=[], _highlightDrIdx=null;
const UI_LS='poe2radar-dash-ui';

function loadUiState(){
  try{
    const u=JSON.parse(localStorage.getItem(UI_LS)||'{}');
    if(u.tab) activeTab=u.tab;
    if(u.dbCat) dbCatFilter=u.dbCat;
    if(u.dbNoRule) { const el=$('#dbNoRule'); if(el) el.checked=u.dbNoRule; }
    if(typeof lmAreaOnly!=='undefined'&&u.lmAreaOnly!=null) lmAreaOnly=u.lmAreaOnly;
    if(u.sidebarOff) document.body.classList.add('sidebar-off');
    syncSidebarToggle();
    window._pendingSetSec=u.setSec;
  }catch(_){}
}
function saveUiState(extra){
  try{
    const u={tab:activeTab,dbCat:dbCatFilter,dbNoRule:$('#dbNoRule')?.checked,lmAreaOnly,setSec:$$('#settingsNav button.on')[0]?.dataset.setsec||'setDisplay',sidebarOff:document.body.classList.contains('sidebar-off'),...extra};
    localStorage.setItem(UI_LS,JSON.stringify(u));
  }catch(_){}
}
function toast(msg,kind=''){
  const w=$('#toastWrap'); if(!w) return;
  const el=document.createElement('div'); el.className='toast'+(kind?' '+kind:''); el.textContent=msg;
  w.appendChild(el); setTimeout(()=>el.remove(),2800);
}
function markStamp(id,label){
  const el=$('#'+id); if(!el) return;
  const t=new Date(); el.textContent='· saved '+t.toLocaleTimeString(); el.classList.add('recent');
  setTimeout(()=>el.classList.remove('recent'),4000);
}
function confirmAct(msg){ return window.confirm(msg); }

$$('.hint-toggle').forEach(btn=>btn.onclick=()=>{
  const id=btn.dataset.hint, el=$('#'+id); if(!el) return;
  const open=!el.hidden; el.hidden=open; btn.textContent=btn.textContent.replace(open?'▾':'▸',open?'▸':'▾');
});
function syncSidebarToggle(){
  const btn=$('#sidebarToggle'), off=document.body.classList.contains('sidebar-off');
  if(btn){ btn.textContent=off?'☰':'◧'; btn.title=off?'Show sidebar':'Hide sidebar'; btn.setAttribute('aria-pressed',off?'true':'false'); }
}
$('#sidebarToggle')?.addEventListener('click',()=>{
  document.body.classList.toggle('sidebar-off');
  syncSidebarToggle();
  saveUiState();
});
syncSidebarToggle();
$$('#settingsNav button').forEach(b=>b.onclick=()=>showSettingsSection(b.dataset.setsec));
function showSettingsSection(id){
  $$('#settingsNav button').forEach(b=>b.classList.toggle('on',b.dataset.setsec===id));
  $$('.settings-section').forEach(s=>s.hidden=s.id!==id);
  saveUiState();
}
function switchTab(tab){
  activeTab=tab;
  $$('.tab').forEach(x=>x.classList.toggle('on',x.dataset.tab===tab));
  $$('.view').forEach(v=>v.hidden=v.dataset.view!==tab);
  saveUiState();
  if(tab==='settings') loadSettings();
  if(tab==='filters') loadFilters();
  if(tab==='live') loadLive();
  if(tab==='database') loadDb();
  if(tab==='landmarks') loadLandmarks();
  if(tab==='atlas'){ if(!atlasData) loadAtlas(); else renderAtlas(); startAtlasPoll(); }
  else stopAtlasPoll();
  if(tab==='live') startLivePoll(); else stopLivePoll();
  if(tab==='filters') startZoneTypesPoll(); else stopZoneTypesPoll();
}
$$('.tab').forEach(t=>t.onclick=()=>switchTab(t.dataset.tab));
const TAB_KEYS=['filters','live','database','landmarks','atlas','settings'];
document.addEventListener('keydown',e=>{
  if(e.target.matches('input,textarea,select')&&!e.ctrlKey&&!e.metaKey) return;
  if(e.key==='/'&&!e.ctrlKey&&!e.metaKey){
    if(activeTab==='filters'){
      e.preventDefault();
      const el=e.shiftKey?$('#typeSearch'):$('#drSearch');
      if(el){ el.focus(); el.select(); }
      return;
    }
    const map={live:'#liveSearch',database:'#dbSearch',landmarks:'#lmSearch',atlas:'#atlasHlFilter'};
    const sel=map[activeTab]; if(sel){ e.preventDefault(); const el=$(sel); if(el){ el.focus(); el.select(); } }
    return;
  }
  if((e.ctrlKey||e.metaKey)&&e.key==='s'){ e.preventDefault(); if(activeTab==='filters') saveDrules(); else if(activeTab==='settings') toast('Settings save on change'); }
  if(e.key==='Escape'){ $('#pickPop')?.classList.remove('open'); $('#iconPop')?.classList.remove('open'); _pickEl?.classList.remove('open'); }
  const n=+e.key; if(n>=1&&n<=6&&!e.ctrlKey&&!e.metaKey){ switchTab(TAB_KEYS[n-1]); }
});
function openLiveTab(opts={}){
  if(!opts.poi) livePoiOnly=false;
  switchTab('live');
  if(opts.category){ liveCatFilter=opts.category; livePoiOnly=false; saveUiState(); }
  if(opts.search){ const el=$('#liveSearch'); if(el) el.value=opts.search; }
  filterLive();
}
$('#tallyEnt')?.addEventListener('click',()=>openLiveTab());
$('#tallyPoi')?.addEventListener('click',()=>{ livePoiOnly=true; liveCatFilter=''; openLiveTab(); });
$('#tallyMon')?.addEventListener('click',()=>openLiveTab({category:'Monster'}));
$('#tallyLm')?.addEventListener('click',()=>switchTab('landmarks'));
function startLivePoll(){ stopLivePoll(); livePoll=setInterval(()=>{ if(activeTab==='live') loadLive(true); },2000); }
function stopLivePoll(){ if(livePoll){ clearInterval(livePoll); livePoll=null; } }
function startAtlasPoll(){ stopAtlasPoll(); atlasPoll=setInterval(()=>{ if(activeTab==='atlas') loadAtlas(true); },5000); }
function stopAtlasPoll(){ if(atlasPoll){ clearInterval(atlasPoll); atlasPoll=null; } }

/* ── polling (left rail vitals/zone/census) ── */
async function getJSON(u){ const r=await fetch(u,{cache:'no-store'}); if(!r.ok) throw 0; return r.json(); }
function setConn(live){
  connLive=live;
  document.body.classList.toggle('offline',!live);
  $('#conn').classList.toggle('live',live);
  $('#connTxt').textContent=live?'Live':'Offline';
  const b=$('#staleBanner'); if(b) b.hidden=live;
  if(!live&&lastTickAt){ const t=$('#staleTime'); if(t) t.textContent='Last update '+new Date(lastTickAt).toLocaleTimeString(); }
}
function updateHeaderQuick(){
  const s=state; if(!s) return;
  const map=$('#hqMap'); if(map){ map.textContent='Map '+(s.mapVisible?'open':'closed'); map.className='hq '+(s.mapVisible?'on':'off'); }
  const fl=$('#hqFlask'); if(fl){ fl.textContent='Flask '+(s.autoFlask?'ON':'off'); fl.className='hq '+(s.autoFlask?'on':'off'); }
}
async function refreshHeaderJunk(){
  try{ const st=await getJSON('/api/settings'); const j=$('#hqJunk'); if(j){ j.textContent='Clutter '+(st.hideJunk?'hidden':'shown'); j.className='hq '+(st.hideJunk?'on':'off'); } }catch(_){}
}
$('#hqJunk')?.addEventListener('click',async()=>{
  try{ const st=await getJSON('/api/settings'); await saveSetting('hideJunk',!st.hideJunk); refreshHeaderJunk(); }catch(_){}
});
$('#hqFlask')?.addEventListener('click',()=>switchTab('settings')); showSettingsSection('setFlask');
$('#hqMap')?.addEventListener('click',()=>switchTab('settings')); showSettingsSection('setDisplay');

async function tick(){
  try{
    state = await getJSON('/state');
    setConn(true);
    lastTickAt=Date.now();
    try{ zone = await getJSON('/api/zone'); }catch(e){ zone=null; }
    renderState();
    if(activeTab==='live') filterLive();
  }catch(e){ setConn(false); }
}

/* ── settings tab (writes radar/visual + flask via the loopback-gated /api/settings) ── */
let _settingsCache={}, _hkBinding=null;
const VK_NAMES={
  0x08:'Backspace',0x09:'Tab',0x0D:'Enter',0x1B:'Esc',0x20:'Space',
  0x21:'Page Up',0x22:'Page Down',0x23:'End',0x24:'Home',
  0x25:'Left',0x26:'Up',0x27:'Right',0x28:'Down',
  0x2D:'Insert',0x2E:'Delete',
  0x70:'F1',0x71:'F2',0x72:'F3',0x73:'F4',0x74:'F5',0x75:'F6',0x76:'F7',0x77:'F8',
  0x78:'F9',0x79:'F10',0x7A:'F11',0x7B:'F12',
  0xBA:';',0xBB:'=',0xBC:',',0xBD:'-',0xBE:'.',0xBF:'/',0xC0:'`',
  0xDB:'[',0xDC:'\\',0xDD:']',0xDE:"'"
};
const GP_FLAG=0x10000;
const GP_NAMES={
  0x1000:'Pad A',0x2000:'Pad B',0x4000:'Pad X',0x8000:'Pad Y',
  0x0100:'Pad LB',0x0200:'Pad RB',0x0020:'Pad Back',0x0010:'Pad Start',
  0x0040:'Pad L3',0x0080:'Pad R3',
  0x0001:'Pad D-Up',0x0002:'Pad D-Down',0x0004:'Pad D-Left',0x0008:'Pad D-Right'
};
function vkName(vk){
  vk=+vk||0;
  if(vk<=0) return 'None';
  if(vk>=GP_FLAG) return GP_NAMES[vk&0xFFFF]||`Pad 0x${(vk&0xFFFF).toString(16)}`;
  if(vk>=0x30&&vk<=0x39) return String.fromCharCode(vk);
  if(vk>=0x41&&vk<=0x5A) return String.fromCharCode(vk);
  return VK_NAMES[vk]||`VK ${vk}`;
}
function keyEventToVk(e){
  if(e.key==='Escape') return -1;
  const k=e.key;
  if(k&&k.length===1){
    const u=k.toUpperCase().charCodeAt(0);
    if(u>=65&&u<=90||u>=48&&u<=57) return u;
  }
  const fk={
    F1:0x70,F2:0x71,F3:0x72,F4:0x73,F5:0x74,F6:0x75,F7:0x76,F8:0x77,F9:0x78,F10:0x79,F11:0x7A,F12:0x7B,
    Backspace:0x08,Tab:0x09,Enter:0x0D,Escape:0x1B,' ':0x20,
    ArrowLeft:0x25,ArrowUp:0x26,ArrowRight:0x27,ArrowDown:0x28,
    Insert:0x2D,Delete:0x2E,Home:0x24,End:0x23,PageUp:0x21,PageDown:0x22
  };
  return fk[k]||0;
}
function updateHotkeyDisplays(s){
  s=s||_settingsCache;
  $$('[data-hk]').forEach(el=>{ el.textContent=vkName(s[el.dataset.hk]); });
}
function wireHotkeys(){
  $$('[data-hk-bind]').forEach(btn=>{
    btn.onclick=()=>{
      if(_hkBinding){
        const prev=$('[data-hk-bind="'+_hkBinding+'"]');
        if(prev){ prev.textContent='Bind'; prev.classList.remove('binding'); }
      }
      _hkBinding=btn.dataset.hkBind;
      btn.textContent='Press key…';
      btn.classList.add('binding');
    };
  });
  $$('[data-hk-pad]').forEach(btn=>{
    btn.onclick=()=>{
      const key=btn.dataset.hkPad;
      const menu=document.createElement('div');
      menu.className='pad-menu';
      Object.entries(GP_NAMES).forEach(([mask,name])=>{
        const b=document.createElement('button');
        b.type='button'; b.className='chip'; b.textContent=name;
        b.onclick=()=>{ menu.remove(); saveSetting(key,GP_FLAG+(+mask)).then(()=>updateHotkeyDisplays()); };
        menu.appendChild(b);
      });
      btn.after(menu);
      setTimeout(()=>document.addEventListener('click',()=>menu.remove(),{once:true}),0);
    };
  });
  $$('[data-hk-clear]').forEach(btn=>{
    btn.onclick=async()=>{
      await saveSetting(btn.dataset.hkClear,0);
      updateHotkeyDisplays();
    };
  });
}
document.addEventListener('keydown',e=>{
  if(!_hkBinding) return;
  if(e.target.matches('input,textarea,select')&&!e.ctrlKey&&!e.metaKey) return;
  const vk=keyEventToVk(e);
  const btn=$('[data-hk-bind="'+_hkBinding+'"]');
  if(vk===-1){
    if(btn){ btn.textContent='Bind'; btn.classList.remove('binding'); }
    _hkBinding=null;
    return;
  }
  if(!vk) return;
  e.preventDefault();
  const key=_hkBinding;
  _hkBinding=null;
  if(btn){ btn.textContent='Bind'; btn.classList.remove('binding'); }
  saveSetting(key,vk).then(()=>updateHotkeyDisplays());
},{capture:true});
async function loadSettings(){
  try{
    const s = await getJSON('/api/settings');
    _settingsCache=s;
    updateHotkeyDisplays(s);
    $$('[data-set]').forEach(el=>{
      const k=el.dataset.set;
      if(el.type==='checkbox') el.checked=!!s[k];
      else if(el.classList.contains('keyin')) el.value=vkToChar(s[k]);
      else if(s[k]!==undefined) el.value=s[k];
    });
    $$('[data-set-inv]').forEach(el=>{
      const k=el.dataset.setInv;
      if(el.type==='checkbox') el.checked=!s[k];
    });
    hpBars = s.hpBars || null;
    terrain = s.terrain || null;
    styles = s.styles || null;
    renderHpBars(); renderTerrain(); renderIcons(); renderMechanics();
    refreshPriceStatus();
  }catch(e){}
}
async function refreshPriceStatus(){
  try{
    const p=await getJSON('/api/prices');
    const el=$('#priceStatus');
    if(el) el.textContent=p.loaded?`${p.count} items · ${p.league||'?'} · ${p.status}`:(p.status||'not loaded');
  }catch(e){}
}
$('#priceRefreshBtn')?.addEventListener('click',async()=>{
  try{
    await fetch('/api/prices',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({refresh:true})});
    toast('Price refresh started','ok');
    setTimeout(refreshPriceStatus,1500);
  }catch(e){ toast('Price refresh failed'); }
});
async function saveSetting(key,val){
  try{
    const body={[key]:val};
    if(key==='autoPathNavigable'&&val){ body.showPath=true; body.showPathWorld=true; }
    await fetch('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
    const m=$('#savedMsg'); m.classList.add('show'); clearTimeout(m._t); m._t=setTimeout(()=>m.classList.remove('show'),1100);
    markStamp('stampSettings'); toast('Settings saved','ok');
    if(key==='hideJunk') updateHeaderQuick();
    if(key==='autoPathNavigable'&&val){
      const spw=$('[data-set="showPathWorld"]'); if(spw) spw.checked=true;
    }
    if(_settingsCache) _settingsCache[key]=val;
  }catch(e){ toast('Settings save failed'); }
}
function wireSettings(){
  $$('[data-set]').forEach(el=>{
    const k=el.dataset.set;
    if(el.type==='checkbox') el.onchange=()=>saveSetting(k,el.checked);
    else if(el.classList.contains('keyin')) el.onchange=()=>{ const vk=charToVk(el.value); if(vk) saveSetting(k,vk); el.value=vkToChar(vk); };
    else if(el.tagName==='SELECT') el.onchange=()=>saveSetting(k,el.value);
    else if(el.type==='text') el.onchange=()=>saveSetting(k,el.value);
    else el.onchange=()=>{ const v=parseFloat(el.value); if(!isNaN(v)) saveSetting(k,v); };
  });
  $$('[data-set-inv]').forEach(el=>{
    const k=el.dataset.setInv;
    if(el.type==='checkbox') el.onchange=()=>saveSetting(k,!el.checked);
  });
}
$('#mechAdd')?.addEventListener('click',()=>{
  if(!styles) styles={mechanics:[]};
  styles.mechanics=styles.mechanics||[];
  styles.mechanics.push({enabled:true,name:'New mechanic',match:[],categories:[],shape:'Star',color:'#ffffff',opacity:1,size:6});
  renderMechanics(); saveStyles();
});
// Flask key inputs accept a single character ('1'-'9', letters) → Win32 VK (== ASCII of uppercase).
const charToVk = s => { const c=(s||'').trim().toUpperCase().charCodeAt(0); return isNaN(c)?0:c; };
const vkToChar = v => v ? String.fromCharCode(v) : '';

/* ── icon / HP-bar / mechanics editors (nested objects: POST the whole {styles}/{hpBars}) ── */
let styles=null, hpBars=null, terrain=null;
const ICON_KEYS=[
  ['monsterNormal','Monster · Normal'],['monsterMagic','Monster · Magic'],
  ['monsterRare','Monster · Rare'],['monsterUnique','Monster · Unique'],
  ['player','Player'],['npc','NPC'],['chestRare','Chest · Rare'],
  ['chestUnique','Chest · Unique'],['transition','Transition'],
  ['poi','Point of Interest'],['landmark','Landmark']];
const esc=s=>(s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const pct=o=>Math.round((o==null?1:o)*100);
const spriteOf=o=>o.sprite||(o.sprite={sheet:'icons.png',col:0,row:0,cellSize:64,scale:1});
function spriteCtl(o){
  const s=o.sprite||{};
  return `<span class="sprctl" title="Icon from sheet (icons.png)">
    <span class="spr-preview" data-spr-preview>${iconPreview(o.shape||'Circle',o.color,s,20)}</span>
    <span>col</span><input class="numin sprin" type="number" min="0" step="1" data-spr="col" value="${s.col??0}">
    <span>row</span><input class="numin sprin" type="number" min="0" step="1" data-spr="row" value="${s.row??0}">
    <span>cell</span><input class="numin sprin" type="number" min="16" step="1" data-spr="cellSize" value="${s.cellSize??64}">
    <span>x</span><input class="numin sprin" type="number" min="0.2" step="0.1" data-spr="scale" value="${s.scale??1}">
  </span>`;
}
function refreshSpritePreview(row,o){
  const el=row.querySelector('[data-spr-preview]'); if(!el) return;
  el.innerHTML=iconPreview(o.shape||'Circle',o.color,o.sprite,20);
}
function wireSprite(row,o,save){
  row.querySelectorAll('[data-spr]').forEach(el=>el.onchange=()=>{
    const v=parseFloat(el.value); if(isNaN(v)) return;
    const s=spriteOf(o), k=el.dataset.spr;
    s[k]=k==='scale'?v:Math.round(v);
    s.sheet='icons.png';
    refreshSpritePreview(row,o);
    save();
  });
}

/* ── icons.png sprite sheet (primary) + SVG library fallback ── */
let ICONS=[], SPRITE_META=null; const ICONMAP={};
async function loadSpriteMeta(){
  try{ SPRITE_META=await getJSON('/api/sprite-meta'); }catch(_){ SPRITE_META=null; }
}
async function loadIcons(){
  await loadSpriteMeta();
  try{ ICONS=await getJSON('/api/icons')||[]; }catch(e){ ICONS=[]; }
  for(const k in ICONMAP) delete ICONMAP[k];
  ICONS.forEach(d=>ICONMAP[(d.name||'').toLowerCase()]=d);
}
const iconDef=name=>ICONMAP[(name||'').toLowerCase()]||null;
function syncShapeSprite(o,shape){
  const sh=SPRITE_META?.shapes?.[shape]||SPRITE_META?.shapes?.[(shape||'').toLowerCase()];
  if(!sh) return;
  const s=spriteOf(o); s.col=sh.col; s.row=sh.row; s.sheet='icons.png';
}
function resolveSprite(name,spriteObj){
  if(spriteObj&&spriteObj.col!=null) return {col:spriteObj.col,row:spriteObj.row,cell:spriteObj.cellSize||64};
  const shapes=SPRITE_META?.shapes||{};
  const key=(name||'Circle');
  const sh=shapes[key]||shapes[key.toLowerCase()]||shapes.Circle;
  return sh?{col:sh.col,row:sh.row,cell:SPRITE_META?.cellSize||64}:null;
}
function iconPreview(name,color,spriteObj,px){
  px=px||16;
  const sp=resolveSprite(name,spriteObj);
  if(SPRITE_META&&sp){
    const cs=sp.cell||SPRITE_META.cellSize||64, aw=SPRITE_META.width, ah=SPRITE_META.height;
    const bw=aw/cs*px, bh=ah/cs*px;
    return `<span class="spr-icon" style="width:${px}px;height:${px}px;background-position:${-sp.col*px}px ${-sp.row*px}px;background-size:${bw}px ${bh}px" title="${esc(name||'')}"></span>`;
  }
  return iconSvg(name,color);
}
function iconSvg(name,color){
  const d=iconDef(name); if(!d) return '<span class="spr-icon" style="width:16px;height:16px;opacity:.3"></span>';
  const c=color||'currentColor';
  return `<svg viewBox="${d.viewBox}" preserveAspectRatio="xMidYMid meet" width="${16}" height="${16}">`
    + (d.paths||[]).map(p=>`<path d="${esc(p)}" fill="${c}"/>`).join('') + `</svg>`;
}
function pickerNames(){
  if(SPRITE_META?.shapes){
    const names=Object.keys(SPRITE_META.shapes).sort();
    if(names.length) return names;
  }
  return ICONS.map(d=>d.name);
}
function pickerHtml(name,color,spriteObj){
  const nm=name||'Circle';
  return `<span class="iconpick" data-val="${esc(nm)}"><span class="ipreview">`
    + iconPreview(nm,color,spriteObj,16) + `</span><span class="ipname">${esc(nm)}</span><span class="ipcar">▼</span></span>`;
}
function refreshPicker(pk,name,color,spriteObj){
  const nm=name||'Circle';
  pk.dataset.val=nm;
  const pv=pk.querySelector('.ipreview'); if(pv) pv.innerHTML=iconPreview(nm,color,spriteObj,16);
  const pn=pk.querySelector('.ipname'); if(pn) pn.textContent=nm;
}
let _iconPop=null;
function ensureIconPop(){
  if(_iconPop) return _iconPop;
  _iconPop=document.createElement('div'); _iconPop.id='iconPop'; document.body.appendChild(_iconPop);
  document.addEventListener('mousedown',e=>{
    if(_iconPop.classList.contains('open') && !_iconPop.contains(e.target) && !e.target.closest('.iconpick')) _iconPop.classList.remove('open');
  });
  return _iconPop;
}
function openIconPicker(anchor,current,cb,spriteObj){
  const pop=ensureIconPop();
  const names=pickerNames();
  pop.innerHTML='<div class="ipop-grid">'+names.map(n=>
    `<div class="ipop-cell${n.toLowerCase()===(current||'').toLowerCase()?' sel':''}" data-n="${esc(n)}" title="${esc(n)}">`
    + iconPreview(n,null,null,20) + `<span class="cn">${esc(n)}</span></div>`).join('')+'</div>';
  pop.querySelectorAll('.ipop-cell').forEach(c=>c.onclick=()=>{ pop.classList.remove('open'); cb(c.dataset.n); });
  pop.classList.add('open');
  const r=anchor.getBoundingClientRect(), pw=pop.offsetWidth, ph=pop.offsetHeight;
  let left=Math.min(r.left, innerWidth-8-pw), top=r.bottom+4;
  if(top+ph>innerHeight-8) top=Math.max(8, r.top-4-ph);
  pop.style.left=Math.max(8,left)+'px'; pop.style.top=top+'px';
}
const saveStyles=()=>{ if(styles) saveSetting('styles',styles); };
const saveHpBars=()=>{ if(hpBars) saveSetting('hpBars',hpBars); };

function renderHpBars(){
  if(!hpBars) return;
  $$('[data-hp]').forEach(el=>{ if(hpBars[el.dataset.hp]!==undefined) el.value=hpBars[el.dataset.hp]; });
  $$('[data-hpcheck]').forEach(el=>{ el.checked=!!hpBars[el.dataset.hpcheck]; });
  $$('[data-hpcolor]').forEach(el=>{ el.value=hpBars[el.dataset.hpcolor]||'#ffffff'; });
}
function wireHpBars(){
  $$('[data-hp]').forEach(el=>{ el.onchange=()=>{ const v=parseFloat(el.value); if(!isNaN(v)&&hpBars){ hpBars[el.dataset.hp]=v; saveHpBars(); } }; });
  $$('[data-hpcheck]').forEach(el=>{ el.onchange=()=>{ if(hpBars){ hpBars[el.dataset.hpcheck]=el.checked; saveHpBars(); } }; });
  $$('[data-hpcolor]').forEach(el=>{ el.onchange=()=>{ if(hpBars){ hpBars[el.dataset.hpcolor]=el.value; saveHpBars(); } }; });
}

/* ── terrain color/transparency (POSTs the whole {terrain} object; rebuilds the terrain bitmap) ── */
const saveTerrain=()=>{ if(terrain) saveSetting('terrain',terrain); };
function renderTerrain(){
  if(!terrain) return;
  $$('[data-tcolor]').forEach(el=>{ el.value=terrain[el.dataset.tcolor]||'#ffffff'; });
  $$('[data-topacity]').forEach(el=>{ el.value=Math.round((terrain[el.dataset.topacity]??1)*100); });
  $$('[data-topv]').forEach(el=>{ el.textContent=Math.round((terrain[el.dataset.topv]??1)*100)+'%'; });
  $$('[data-tnum]').forEach(el=>{ const k=el.dataset.tnum; if(terrain[k]!==undefined) el.value=terrain[k]; });
}
function wireTerrain(){
  $$('[data-tcolor]').forEach(el=>{ el.onchange=()=>{ if(terrain){ terrain[el.dataset.tcolor]=el.value; saveTerrain(); } }; });
  $$('[data-topacity]').forEach(el=>{
    const k=el.dataset.topacity, v=$(`[data-topv="${k}"]`);
    el.oninput=()=>{ if(v) v.textContent=el.value+'%'; };
    el.onchange=()=>{ if(terrain){ terrain[k]=(+el.value)/100; saveTerrain(); } };
  });
  $$('[data-tnum]').forEach(el=>{ el.onchange=()=>{ if(terrain){ const v=parseFloat(el.value); if(!isNaN(v)){ terrain[el.dataset.tnum]=v; saveTerrain(); } } }; });
}

function iconRow(key,label,o){
  return `<div class="stylerow" data-k="${key}">
    <label class="sw"><input type="checkbox" class="i-en"${o.enabled?' checked':''}><span class="track"></span><span class="knob"></span></label>
    <span class="nm">${label}</span>
    ${pickerHtml(o.shape,o.color)}
    <input type="color" class="i-color" value="${o.color||'#ffffff'}">
    <input type="range" class="op i-op" min="0" max="100" value="${pct(o.opacity)}">
    <span class="opv">${pct(o.opacity)}%</span>
    <input type="number" class="numin sz i-size" step="0.1" min="0.5" value="${o.size}">
    ${spriteCtl(o)}
  </div>`;
}
function renderIcons(){
  if(!styles){ $('#iconStyles').innerHTML=''; return; }
  $('#iconStyles').innerHTML=ICON_KEYS.map(([k,l])=>iconRow(k,l,styles[k]||{})).join('');
  $$('#iconStyles .stylerow').forEach(row=>{
    const o=styles[row.dataset.k]; if(!o) return;
    const pk=row.querySelector('.iconpick');
    row.querySelector('.i-en').onchange=e=>{ o.enabled=e.target.checked; saveStyles(); };
    pk.onclick=()=>openIconPicker(pk,o.shape,n=>{ o.shape=n; syncShapeSprite(o,n); refreshPicker(pk,n,o.color,o.sprite); saveStyles(); });
    row.querySelector('.i-color').onchange=e=>{ o.color=e.target.value; refreshPicker(pk,o.shape,o.color,o.sprite); saveStyles(); };
    const op=row.querySelector('.i-op'), opv=row.querySelector('.opv');
    op.oninput=()=>{ opv.textContent=op.value+'%'; };
    op.onchange=()=>{ o.opacity=(+op.value)/100; saveStyles(); };
    row.querySelector('.i-size').onchange=e=>{ const v=parseFloat(e.target.value); if(!isNaN(v)){ o.size=v; saveStyles(); } };
    wireSprite(row,o,saveStyles);
  });
}

/* Entity categories a mechanic rule can be gated to (value = Poe2Live.EntityCategory name). Empty
   selection = applies to every category. Labels are friendlier than the raw enum names. */
const MECH_CATS=[['Monster','Monsters'],['Chest','Chests'],['Other','Misc / POI'],
  ['Object','Terrain'],['Npc','NPCs'],['Transition','Transitions']];
function mechRow(m,i){
  const cats=m.categories||[];
  return `<div class="mechrow" data-i="${i}">
    <div class="top">
      <label class="sw"><input type="checkbox" class="m-en"${m.enabled?' checked':''}><span class="track"></span><span class="knob"></span></label>
      <input class="mname" placeholder="Name (e.g. Expedition)" value="${esc(m.name)}">
      <button class="delbtn m-del">Remove</button>
    </div>
    <input class="matchin m-match" placeholder="match terms, comma-separated (e.g. Strongbox, StrongBoxes)" value="${esc((m.match||[]).join(', '))}">
    <div class="mcats"><span class="mcats-lbl">Applies to</span>${MECH_CATS.map(([v,l])=>
      `<label class="catchip${cats.includes(v)?' on':''}"><input type="checkbox" class="m-cat" data-cat="${v}"${cats.includes(v)?' checked':''}>${l}</label>`).join('')}
      <span class="mcats-hint">${cats.length?'':'all types'}</span></div>
    <div class="ctl">
      ${pickerHtml(m.shape,m.color)}
      <input type="color" class="m-color" value="${m.color||'#ffffff'}">
      <input type="range" class="op m-op" min="0" max="100" value="${pct(m.opacity)}">
      <span class="opv">${pct(m.opacity)}%</span>
      <input type="number" class="numin sz m-size" step="0.1" min="0.5" value="${m.size}">
      ${spriteCtl(m)}
    </div>
  </div>`;
}
function renderMechanics(){
  if(!styles){ $('#mechList').innerHTML=''; return; }
  styles.mechanics=styles.mechanics||[];
  $('#mechList').innerHTML=styles.mechanics.map((m,i)=>mechRow(m,i)).join('');
  $$('#mechList .mechrow').forEach(row=>{
    const m=styles.mechanics[+row.dataset.i]; if(!m) return;
    const pk=row.querySelector('.iconpick');
    row.querySelector('.m-en').onchange=e=>{ m.enabled=e.target.checked; saveStyles(); };
    row.querySelector('.mname').onchange=e=>{ m.name=e.target.value; saveStyles(); };
    row.querySelector('.m-match').onchange=e=>{ m.match=e.target.value.split(',').map(s=>s.trim()).filter(Boolean); saveStyles(); };
    row.querySelectorAll('.m-cat').forEach(cb=>{ cb.onchange=()=>{
      m.categories=[...row.querySelectorAll('.m-cat:checked')].map(c=>c.dataset.cat);
      cb.closest('.catchip').classList.toggle('on',cb.checked);
      const h=row.querySelector('.mcats-hint'); if(h) h.textContent=m.categories.length?'':'all types';
      saveStyles(); }; });
    pk.onclick=()=>openIconPicker(pk,m.shape,n=>{ m.shape=n; syncShapeSprite(m,n); refreshPicker(pk,n,m.color,m.sprite); saveStyles(); });
    row.querySelector('.m-color').onchange=e=>{ m.color=e.target.value; refreshPicker(pk,m.shape,m.color,m.sprite); saveStyles(); };
    const op=row.querySelector('.m-op'), opv=row.querySelector('.opv');
    op.oninput=()=>{ opv.textContent=op.value+'%'; };
    op.onchange=()=>{ m.opacity=(+op.value)/100; saveStyles(); };
    row.querySelector('.m-size').onchange=e=>{ const v=parseFloat(e.target.value); if(!isNaN(v)){ m.size=v; saveStyles(); } };
    wireSprite(row,m,saveStyles);
    row.querySelector('.m-del').onclick=()=>{ styles.mechanics.splice(+row.dataset.i,1); renderMechanics(); saveStyles(); };
  });
}
/* ── Rules tab: unified Display Rules + Hidden cull patterns ── */
let hidden=[], drules=[], zoneTypesData=null, typeSearchQ='', drSearchQ='';
let zoneTypesPoll=null;
const KNOWN_SEMANTIC_NAMES=new Set(['Boss','Monster · Unique','Monster · Rare','Monster · Magic','Monster · Normal',
  'Player','NPC','Chest · Unique','Chest · Rare','Transition','Quest object','Quest marker','Waypoint','Bridge','Portal',
  'Checkpoint','Map marker','Point of Interest','Stash','Town portal','Expedition','Ritual','Breach','Abyss','Delirium','Ultimatum','Corruption',
  'Strongbox','Strongbox · Unique','Strongbox · Landmark','Strongbox · Cartographer',
  'Strongbox · Arcane','Strongbox · Armourer','Strongbox · Jeweller','Strongbox · Divination','Strongbox · Expedition',
  'Strongbox · Researcher','Strongbox · Abyss',
  'Essence','Shrine','Summoning Circle','Wisp','Rogue Exile']);
const _ztOpen={};
function isStateHideRule(r){ return r.hide&&(r.life==='Dead'||r.chest==='Opened'); }
function isPerTypeEntityRule(r){
  if(isStateHideRule(r)) return false;
  if((r.categories||[]).length>0) return false;
  if(!r.match||r.match.length!==1) return false;
  if((r.name||'').startsWith('Type override:')) return true;
  return !KNOWN_SEMANTIC_NAMES.has(r.name);
}
function druleVisible(r){
  if(isPerTypeEntityRule(r)) return false;
  const q=(drSearchQ||'').trim().toLowerCase();
  if(!q) return true;
  if((r.name||'').toLowerCase().includes(q)) return true;
  if((r.match||[]).some(m=>m.toLowerCase().includes(q))) return true;
  if((r.categories||[]).some(c=>c.toLowerCase().includes(q))) return true;
  for(const f of ['rarity','reaction','life','chest','poi']){
    if(r[f]&&String(r[f]).toLowerCase().includes(q)) return true;
  }
  return false;
}
function ztTierOpen(key){ if(_ztOpen[key]===undefined) _ztOpen[key]=true; return _ztOpen[key]; }
function flashF(){ const m=$('#savedMsgF'); if(!m) return; m.classList.add('show'); clearTimeout(m._t); m._t=setTimeout(()=>m.classList.remove('show'),1100); markStamp('stampRules'); toast('Rules saved','ok'); }
async function postHidden(body){ try{ await fetch('/api/hidden',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}); flashF(); }catch(e){ toast('Save failed'); } }
async function loadFilters(){
  await loadDrules();
  await loadZoneTypes();
  try{ const h=await getJSON('/api/hidden'); hidden=h.patterns||[]; }catch(e){ hidden=[]; }
  renderHidden();
}
async function loadZoneTypes(silent){
  try{
    zoneTypesData=await getJSON('/api/zone-types');
    renderZoneTypes();
  }catch(_){
    const h=$('#zoneTypesHost');
    if(h&&!silent) h.innerHTML='<div class="hint-row">Could not load zone types.</div>';
  }
}
function renderZoneTypes(){
  const host=$('#zoneTypesHost'); if(!host) return;
  const d=zoneTypesData;
  const tag=$('#ztAreaTag');
  if(tag) tag.textContent=d?.areaCode ? '· '+d.areaCode : '· zone type overrides';
  if(!d){ host.innerHTML='<div class="hint-row">Loading…</div>'; return; }
  if(d.empty){
    host.innerHTML='<div class="hint-row">No entities in range (enter a zone / move closer).</div>';
    return;
  }
  const q=(typeSearchQ||'').trim().toLowerCase();
  const tiers=(d.tiers||[]).map(tier=>{
    const types=(tier.types||[]).filter(t=>{
      if(!q) return true;
      return (t.label||'').toLowerCase().includes(q)||(t.token||'').toLowerCase().includes(q);
    });
    if(!types.length) return '';
    const open=ztTierOpen(tier.tier);
    const rows=types.map(t=>{
      const zMark=t.hasZoneOverride?'<span class="zt-zone">· zone</span>':'';
      return `<div class="zt-row" data-token="${esc(t.token)}">
        <label><input type="checkbox" class="zt-show"${t.show?' checked':''}> Show</label>
        <label><input type="checkbox" class="zt-nav"${t.nav?' checked':''}${t.show?'':' disabled'}> Path</label>
        <span class="zt-count">x${t.count}</span>
        <span class="zt-label">${esc(t.label||t.token)}</span>${zMark}</div>`;
    }).join('');
    const grp=tier.ruleNames&&tier.ruleNames.length?`<div class="zt-tier-grp">
        <label><input type="checkbox" class="zt-grp-show"${tier.groupShow?' checked':''}> Show</label>
        <label><input type="checkbox" class="zt-grp-nav"${tier.groupNav?' checked':''}${tier.groupShow?'':' disabled'}> Path</label>
        <span>tier defaults</span></div>`:'';
    return `<div class="zt-tier" data-tier="${esc(tier.tier)}">
      <div class="zt-tier-h"><span class="zt-caret">${open?'▾':'▸'}</span> ${esc(tier.label)} (${tier.count})</div>
      ${open?`<div class="zt-tier-body">${grp}${rows}</div>`:''}</div>`;
  }).filter(Boolean).join('');
  host.innerHTML=tiers||'<div class="hint-row">No types match this search.</div>';
  host.querySelectorAll('.zt-tier-h').forEach(h=>{
    h.onclick=()=>{ const k=h.closest('.zt-tier').dataset.tier; _ztOpen[k]=!ztTierOpen(k); renderZoneTypes(); };
  });
  host.querySelectorAll('.zt-tier').forEach(block=>{
    const tier=block.dataset.tier;
    const grpShow=block.querySelector('.zt-grp-show');
    const grpNav=block.querySelector('.zt-grp-nav');
    if(grpShow) grpShow.onchange=async e=>{
      const show=e.target.checked; if(grpNav) grpNav.disabled=!show;
      try{
        await fetch('/api/zone-types/tier',{method:'POST',headers:{'Content-Type':'application/json'},
          body:JSON.stringify({tier,show,nav:grpNav?.checked||false})});
        const r=await getJSON('/api/display-rules'); drules=r.rules||[]; renderDrules();
        await loadZoneTypes(true);
      }catch(_){ toast('Tier update failed'); }
    };
    if(grpNav) grpNav.onchange=async e=>{
      try{
        await fetch('/api/zone-types/tier',{method:'POST',headers:{'Content-Type':'application/json'},
          body:JSON.stringify({tier,show:grpShow?.checked||false,nav:e.target.checked})});
        const r=await getJSON('/api/display-rules'); drules=r.rules||[]; renderDrules();
        await loadZoneTypes(true);
      }catch(_){ toast('Tier update failed'); }
    };
    block.querySelectorAll('.zt-row').forEach(row=>{
      const token=row.dataset.token, showCb=row.querySelector('.zt-show'), navCb=row.querySelector('.zt-nav');
      showCb?.addEventListener('change',async e=>{
        const show=e.target.checked; if(navCb){ navCb.disabled=!show; if(!show) navCb.checked=false; }
        try{
          await fetch('/api/zone-types',{method:'POST',headers:{'Content-Type':'application/json'},
            body:JSON.stringify({token,show,nav:navCb?.checked||false})});
          await loadZoneTypes(true);
        }catch(_){ toast('Zone override failed'); }
      });
      navCb?.addEventListener('change',async e=>{
        try{
          await fetch('/api/zone-types',{method:'POST',headers:{'Content-Type':'application/json'},
            body:JSON.stringify({token,show:showCb?.checked||false,nav:e.target.checked})});
          await loadZoneTypes(true);
        }catch(_){ toast('Zone override failed'); }
      });
    });
  });
}
function startZoneTypesPoll(){ stopZoneTypesPoll(); zoneTypesPoll=setInterval(()=>{ if(activeTab==='filters') loadZoneTypes(true); },2000); }
function stopZoneTypesPoll(){ if(zoneTypesPoll){ clearInterval(zoneTypesPoll); zoneTypesPoll=null; } }

/* ── Display Rules: the unified ordered ruleset. The page holds the array, edits it, and re-POSTs
   the WHOLE list on any change (add / remove / reorder / toggle / field) — same pattern styles used. ── */
const DR_CATS=['Monster','Chest','Npc','Object','Other','Transition','Player','Tile'];
const DR_SELECTS=[['rarity','Rarity',['Normal','Magic','Rare','Unique']],['reaction','Reaction',['Hostile','Friendly']],
  ['life','Life',['Alive','Dead']],['chest','Chest',['Opened','Unopened']],['poi','POI',['Yes','No']]];
async function saveDrules(){ try{ await fetch('/api/display-rules',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({rules:drules})}); flashF(); updateLiveRuleCol(); }catch(e){ toast('Save failed'); } }
async function refreshLiveCache(){ try{ _liveEntsCache=await getJSON('/entities?limit=2000')||[]; }catch(_){ _liveEntsCache=[]; } }
async function loadDrules(){ try{ const r=await getJSON('/api/display-rules'); drules=r.rules||[]; }catch(e){ drules=[]; } await refreshLiveCache(); renderDrules(); }
function findDbRule(path){
  const seg=lastSeg(path);
  for(let i=0;i<drules.length;i++){ const r=drules[i]; if((r.match||[]).some(m=>path.includes(m)||seg===m)) return {i,r}; }
  return null;
}
function dbRuleBadge(path){
  const hit=findDbRule(path); if(!hit) return '';
  const r=hit.r; return `<span class="db-cat" style="color:${r.color||'var(--accent)'};border-color:${r.color||'var(--accent)'}">${esc(r.name||'rule')}</span>`;
}
function drSel(f,l,o,cur){ return `<label class="drsel">${l}<select class="dr-cond" data-f="${f}"><option value=""${!cur?' selected':''}>any</option>`
  +o.map(x=>`<option${cur===x?' selected':''}>${x}</option>`).join('')+`</select></label>`; }
/* Concise matcher→action summary shown on the collapsed row so the list stays scannable. */
function drSummary(r){
  const p=[];
  p.push((r.categories&&r.categories.length)?r.categories.join('/'):'any type');
  if(r.match&&r.match.length) p.push('“'+r.match.join(', ')+'”');
  ['rarity','reaction','life','chest','poi'].forEach(f=>{ if(r[f]) p.push(r[f]); });
  return esc(p.join(' · '));
}
function compileTerm(term){
  if(!/[?*]/.test(term)) return {sub:term,re:null};
  const p=term.replace(/[.+^${}()|[\]\\]/g,'\\$&').replace(/\?/g,'.').replace(/\*/g,'.*');
  return {sub:null,re:new RegExp('^'+p+'$','i')};
}
function termMatches(meta,term){
  const c=compileTerm(term); const m=meta||'';
  if(c.re) return c.re.test(m);
  return m.toLowerCase().includes((c.sub||'').toLowerCase());
}
function ruleMatchesEntity(e,r){
  if(!r.enabled) return false;
  const cats=r.categories||[];
  if(cats.length&&!cats.some(c=>c.toLowerCase()===(e.category||'').toLowerCase())) return false;
  const match=r.match||[];
  if(match.length&&!match.some(t=>termMatches(e.metadata,t))) return false;
  if(r.rarity&&r.rarity!==e.rarity) return false;
  if(r.reaction==='Friendly'&&!e.friendly) return false;
  if(r.reaction==='Hostile'&&e.friendly) return false;
  const alive=(e.hpMax||0)<=0||(e.hpCur||0)>0;
  if(r.life==='Alive'&&!alive) return false;
  if(r.life==='Dead'&&alive) return false;
  if(r.chest==='Opened'&&!e.opened) return false;
  if(r.chest==='Unopened'&&e.opened) return false;
  if(r.poi==='Yes'&&!e.poi) return false;
  if(r.poi==='No'&&e.poi) return false;
  return true;
}
function countRuleMatches(r){ return _liveEntsCache.filter(e=>ruleMatchesEntity(e,r)).length; }
function winningRuleForEntity(e){
  for(const r of drules){ if(r.enabled&&ruleMatchesEntity(e,r)) return r; }
  return null;
}
function highlightDrRow(i){
  _highlightDrIdx=i; renderDrules();
  const row=$$('#drList .drrow').find(r=>+r.dataset.i===i);
  if(row){ row.classList.add('dr-new'); row.scrollIntoView({block:'center'}); setTimeout(()=>row.classList.remove('dr-new'),1400); }
}
function drRow(r,i){
  const open=!!r._open, cats=r.categories||[];
  const mc=_liveEntsCache.length?countRuleMatches(r):'—';
  const badges=(!r.enabled?'<span class="drbadge paused">paused</span>':'')
    +(r.hide?'<span class="drbadge hide">hidden</span>':'')
    +(r.navigable?'<span class="drbadge">path</span>':'');
  const body=open?`<div class="drbody">
      <div class="dr-section">When this matches</div>
      <div class="top"><input class="mname dr-name" value="${esc(r.name)}" placeholder="Rule name"></div>
      <label class="hint-oneline" style="display:block;margin-bottom:4px">Name contains <span style="color:var(--ink-faint)">(e.g. Waypoint, Ritual, *Daemon* — blank = any)</span></label>
      <input class="matchin dr-match" placeholder="Waypoint, Strongbox, …" value="${esc((r.match||[]).join(', '))}">
      <div class="mcats"><span class="mcats-lbl">Entity type</span>${DR_CATS.map(c=>
        `<label class="catchip${cats.includes(c)?' on':''}"><input type="checkbox" class="dr-cat" data-cat="${c}"${cats.includes(c)?' checked':''}>${c}</label>`).join('')}</div>
      <div class="drconds">${DR_SELECTS.map(([f,l,o])=>drSel(f,l,o,r[f])).join('')}</div>
      <div class="dr-section">Then on the map</div>
      <div class="dr-preview-lg">${r.hide?'':iconPreview(r.shape,r.color,r.sprite,32)}</div>
      <div class="ctl">
        <label class="drflag dr-hideflag" title="{{H.RulesHide}}"><input type="checkbox" class="dr-hide"${r.hide?' checked':''}> Don&rsquo;t show on map</label>
        ${pickerHtml(r.shape,r.color,r.sprite)}
        <input type="color" class="dr-color" value="${r.color||'#ffffff'}">
        <input type="range" class="op dr-op" min="0" max="100" value="${pct(r.opacity)}"><span class="opv">${pct(r.opacity)}%</span>
        <input type="number" class="numin sz dr-size" step="0.1" min="0.5" value="${r.size}">
        <span class="mcats-lbl">Icon from sheet</span>${spriteCtl(r)}
        <input class="mname dr-label" style="flex:1;min-width:70px" value="${esc(r.label||'')}" placeholder="Label (optional)">
        <label class="drflag" title="{{H.RulesPath}}"><input type="checkbox" class="dr-nav"${r.navigable?' checked':''}> Show path to this</label>
      </div>
    </div>`:'';
  return `<div class="mechrow drrow${r.hide?' hideon':''}${open?' open':''}${r.enabled?'':' off'}" data-i="${i}" draggable="true">
    <div class="drhead">
      <button type="button" class="ordbtn dr-drag" title="Drag to reorder">⠿</button>
      <label class="sw" title="{{H.RulesPaused}}"><input type="checkbox" class="dr-en"${r.enabled?' checked':''}><span class="track"></span><span class="knob"></span></label>
      <span class="dr-status${r.enabled?'':' paused'}">${r.enabled?'Active':'Paused'}</span>
      <span class="drcaret">${open?'▾':'▸'}</span>
      <span class="drswatch">${r.hide?'':iconPreview(r.shape,r.color,r.sprite,15)}</span>
      <span class="drnm">${esc(r.name||'(unnamed)')}</span>
      <span class="drsum">${drSummary(r)}</span>
      <span class="drmatch" title="entities in zone matching this rule">${mc}</span>
      <span class="drbadges">${badges}</span>
      <span class="drord"><button type="button" class="ordbtn dr-up" title="Move up (higher priority)">▲</button><button type="button" class="ordbtn dr-dn" title="Move down (lower priority)">▼</button></span>
      <button type="button" class="ordbtn dr-dup" title="duplicate">⧉</button>
      <button type="button" class="delbtn dr-del" title="remove">✕</button>
    </div>
    ${body}
  </div>`;
}
function renderDrules(){
  const host=$('#drList'); if(!host) return;
  const visibleIdx=drules.map((r,i)=>druleVisible(r)?i:-1).filter(i=>i>=0);
  host.innerHTML=visibleIdx.length?visibleIdx.map(i=>drRow(drules[i],i)).join('')
    :(drules.length?'<div class="row"><div class="rl hint-row">No rules match this search.</div></div>'
    :'<div class="row"><div class="rl hint-row">No radar rules yet. Add one below.</div></div>');
  $$('#drList .drrow').forEach(row=>{
    const i=+row.dataset.i, r=drules[i]; if(!r) return;
    const save=saveDrules;
    // Header (always present): click anywhere except a control toggles expand.
    row.querySelector('.drhead').onclick=e=>{ if(e.target.closest('input,button,select,label,.drord')) return; r._open=!r._open; renderDrules(); };
    row.querySelector('.dr-en').onchange=e=>{
      r.enabled=e.target.checked; row.classList.toggle('off',!r.enabled);
      const st=row.querySelector('.dr-status'); if(st){ st.textContent=r.enabled?'Active':'Paused'; st.classList.toggle('paused',!r.enabled); }
      save(); renderDrules();
    };
    row.querySelector('.dr-up').onclick=()=>{ if(i>0){ const t=drules[i-1]; drules[i-1]=drules[i]; drules[i]=t; renderDrules(); save(); } };
    row.querySelector('.dr-dn').onclick=()=>{ if(i<drules.length-1){ const t=drules[i+1]; drules[i+1]=drules[i]; drules[i]=t; renderDrules(); save(); } };
    row.querySelector('.dr-dup').onclick=()=>{ const c=JSON.parse(JSON.stringify(r)); delete c._open; c.name=(r.name||'Rule')+' copy'; drules.splice(i+1,0,c); renderDrules(); save(); };
    row.querySelector('.dr-del').onclick=()=>{ if(!confirmAct('Remove rule “'+(r.name||'unnamed')+'”?')) return; drules.splice(i,1); renderDrules(); save(); };
    let dragFrom=null;
    row.addEventListener('dragstart',e=>{ dragFrom=i; row.classList.add('drag-over'); e.dataTransfer.effectAllowed='move'; });
    row.addEventListener('dragend',()=>{ dragFrom=null; row.classList.remove('drag-over'); $$('#drList .drrow').forEach(x=>x.classList.remove('drag-over')); });
    row.addEventListener('dragover',e=>{ e.preventDefault(); if(dragFrom!=null&&dragFrom!==i) row.classList.add('drag-over'); });
    row.addEventListener('dragleave',()=>row.classList.remove('drag-over'));
    row.addEventListener('drop',e=>{ e.preventDefault(); row.classList.remove('drag-over'); if(dragFrom==null||dragFrom===i) return; const item=drules[dragFrom]; drules.splice(dragFrom,1); drules.splice(i,0,item); renderDrules(); save(); });
    if(!r._open) return; // body controls only exist when expanded
    const pk=row.querySelector('.iconpick');
    row.querySelector('.dr-name').onchange=e=>{ r.name=e.target.value; save(); };
    row.querySelector('.dr-match').onchange=e=>{ r.match=e.target.value.split(',').map(s=>s.trim()).filter(Boolean); save(); };
    row.querySelectorAll('.dr-cat').forEach(cb=>cb.onchange=()=>{ r.categories=[...row.querySelectorAll('.dr-cat:checked')].map(c=>c.dataset.cat); cb.closest('.catchip').classList.toggle('on',cb.checked); save(); });
    row.querySelectorAll('.dr-cond').forEach(sel=>sel.onchange=()=>{ r[sel.dataset.f]=sel.value||null; save(); });
    row.querySelector('.dr-hide').onchange=e=>{
      if(e.target.checked&&!r.hide&&!confirmAct('Matched entities won\u2019t appear on the radar.')){ e.target.checked=false; return; }
      r.hide=e.target.checked; save(); renderDrules();
    };
    pk.onclick=()=>openIconPicker(pk,r.shape,n=>{ r.shape=n; syncShapeSprite(r,n); refreshPicker(pk,n,r.color,r.sprite); refreshSpritePreview(row,r); save(); });
    row.querySelector('.dr-color').onchange=e=>{ r.color=e.target.value; refreshPicker(pk,r.shape,r.color,r.sprite); save(); };
    const op=row.querySelector('.dr-op'),opv=row.querySelector('.opv'); op.oninput=()=>opv.textContent=op.value+'%'; op.onchange=()=>{ r.opacity=(+op.value)/100; save(); };
    row.querySelector('.dr-size').onchange=e=>{ const v=parseFloat(e.target.value); if(!isNaN(v)){ r.size=v; save(); } };
    wireSprite(row,r,save);
    row.querySelector('.dr-label').onchange=e=>{ r.label=e.target.value; save(); };
    row.querySelector('.dr-nav').onchange=e=>{ r.navigable=e.target.checked; save(); };
  });
}
$('#drSearch')?.addEventListener('input',()=>{ drSearchQ=$('#drSearch').value; renderDrules(); });
$('#typeSearch')?.addEventListener('input',()=>{ typeSearchQ=$('#typeSearch').value; renderZoneTypes(); });
$('#drAdd')?.addEventListener('click',()=>{ drules.push({enabled:true,name:'New rule',categories:[],match:[],shape:'Circle',color:'#ffd926',opacity:1,size:4,_open:true}); renderDrules(); saveDrules(); highlightDrRow(drules.length-1); });
$('#drExport')?.addEventListener('click',async()=>{
  try{ const h=await getJSON('/api/hidden'); const blob=new Blob([JSON.stringify({rules:drules,hidden:h.patterns||[]},null,2)],{type:'application/json'});
    const a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download='poe2radar-rules.json'; a.click(); URL.revokeObjectURL(a.href);
  }catch(e){ toast('Export failed'); }
});
$('#drImport')?.addEventListener('click',()=>{
  const inp=document.createElement('input'); inp.type='file'; inp.accept='.json';
  inp.onchange=()=>{ const f=inp.files&&inp.files[0]; if(!f) return; const rd=new FileReader();
    rd.onload=async()=>{ try{
      const j=JSON.parse(rd.result);
      if(j.rules) drules=j.rules; renderDrules(); await saveDrules();
      if(j.hidden&&j.hidden.length) await postHidden({clear:true}); for(const p of (j.hidden||[])) await postHidden({add:p});
      toast('Rules imported','ok');
    }catch(_){ toast('Invalid JSON'); } };
    rd.readAsText(f); };
  inp.click();
});

/* ── Add-rule picker: browse the area's live ENTITIES + terrain TILE names, filter, click to seed a
   rule (entity → entity rule by category; tile → Tile rule). Removes the guesswork of typing metadata. ── */
let _pickEl=null, _pickEnts=[], _pickTiles=[], _pickKind='all', _pickQ='';
const lastSeg=s=>((s||'').split('/').pop()||'').replace(/@\d+$/,'').replace(/\.tdt$/i,'');
function ensurePick(){
  if(_pickEl) return _pickEl;
  _pickEl=document.createElement('div'); _pickEl.id='pickPop';
  _pickEl.innerHTML=`<div class="pickbox">
    <div class="pickhead">
      <input id="pickSearch" type="search" placeholder="filter by name or path…">
      <span class="pickkinds"><button class="chip on" data-k="all">All</button><button class="chip" data-k="entity">Entities</button><button class="chip" data-k="tile">Tiles</button></span>
      <button class="pickclose" title="close">✕</button>
    </div>
    <div class="picklist" id="pickList"></div>
    <div class="pickfoot">Click a target to add a rule for it (opens expanded to refine). Entities seed an entity rule; tiles seed a Tile rule.</div>
  </div>`;
  document.body.appendChild(_pickEl);
  _pickEl.querySelector('.pickclose').onclick=()=>_pickEl.classList.remove('open');
  _pickEl.onclick=e=>{ if(e.target===_pickEl) _pickEl.classList.remove('open'); };
  _pickEl.querySelector('#pickSearch').oninput=e=>{ _pickQ=e.target.value.toLowerCase(); renderPick(); };
  _pickEl.querySelectorAll('.pickkinds .chip').forEach(c=>c.onclick=()=>{ _pickKind=c.dataset.k; _pickEl.querySelectorAll('.pickkinds .chip').forEach(x=>x.classList.toggle('on',x===c)); renderPick(); });
  return _pickEl;
}
async function openPicker(){
  const pop=ensurePick(); pop.classList.add('open');
  _pickQ=''; _pickKind='all';
  pop.querySelector('#pickSearch').value=''; pop.querySelectorAll('.pickkinds .chip').forEach((x,j)=>x.classList.toggle('on',j===0));
  $('#pickList').innerHTML='<div class="pickempty">Loading…</div>';
  try{ _pickEnts=await getJSON('/entities?limit=1000')||[]; }catch(_){ _pickEnts=[]; }
  try{ const t=await getJSON('/api/tiles'); _pickTiles=(t&&t.tiles)||[]; }catch(_){ _pickTiles=[]; }
  renderPick(); pop.querySelector('#pickSearch').focus();
}
function pickItems(){
  const q=_pickQ, out=[];
  if(_pickKind!=='tile'){
    const seen=new Set();
    _pickEnts.forEach(e=>{ const k=e.category+'|'+e.metadata; if(seen.has(k))return; seen.add(k);
      if(q && !((e.metadata||'').toLowerCase().includes(q)||(e.name||'').toLowerCase().includes(q)||(e.category||'').toLowerCase().includes(q)))return;
      out.push({kind:'entity',cat:e.category,name:e.name||lastSeg(e.metadata),sub:e.metadata,rarity:e.rarity}); });
  }
  if(_pickKind!=='entity'){
    _pickTiles.forEach(p=>{ if(q && !p.toLowerCase().includes(q))return; out.push({kind:'tile',cat:'Tile',name:lastSeg(p),sub:p}); });
  }
  return out;
}
function renderPick(){
  const items=pickItems(), list=$('#pickList');
  list.innerHTML = items.length ? items.slice(0,600).map((it,i)=>
    `<div class="pickrow" data-i="${i}"><span class="pickbadge ${it.kind}">${it.kind==='tile'?'TILE':esc(it.cat)}</span>`
    +`<span class="picknm">${esc(it.name)}</span><span class="picksub">${esc(it.sub)}</span>`
    +(it.rarity&&it.rarity!=='NonMonster'?`<span class="pickrar">${esc(it.rarity)}</span>`:'')+`</div>`).join('')
    : `<div class="pickempty">No matches${(_pickEnts.length+_pickTiles.length===0)?' — are you in game?':''}.</div>`;
  $$('#pickList .pickrow').forEach(row=>row.onclick=()=>pickItem(items[+row.dataset.i]));
}
function pickItem(it){
  if(!it) return;
  const r = it.kind==='tile'
    ? {enabled:true,name:it.name,categories:['Tile'],match:[lastSeg(it.sub)],shape:'Diamond',color:'#f259f2',opacity:1,size:5,navigable:true,_open:true}
    : {enabled:true,name:it.name,categories:[it.cat],match:[lastSeg(it.sub)],shape:'Star',color:'#ffd926',opacity:1,size:6,_open:true};
  drules.unshift(r); renderDrules(); saveDrules();
  _pickEl.classList.remove('open');
  highlightDrRow(0);
}
$('#drPick')?.addEventListener('click',openPicker);

/* ── Entity database (static GGPK paths via /api/database) ── */
let db=[], dbCatFilter='';
const JUNK_PATTERNS=['/attachments','monstermods','microtransactions','/timelines/','stashskins','/fx/','/mat/','/ao/','/epk/','/graph/','/audio/','/pet/','/clone/','playersummoned','essencemoddaemons','tormentedspirits','/daemon/','bossroomminimapicon','/environment/','hairstyles','/outfits/','/runemarked'];
function isJunkDb(p){const l=p.toLowerCase();return JUNK_PATTERNS.some(j=>l.includes(j));}
function getDbCat(p){const parts=p.split('/');return parts.length>=2?parts[1]:'?';}
function inferDbCategories(path){
  const c=getDbCat(path).toLowerCase();
  if(c==='monsters') return ['Monster'];
  if(c==='npcs'||c==='npc') return ['Npc'];
  if(c==='chests') return ['Chest'];
  if(c==='terrain') return ['Transition'];
  if(c==='characters') return ['Player'];
  return ['Other'];
}
function hasDbRule(path){
  const seg=lastSeg(path);
  return drules.some(r=>(r.match||[]).some(m=>path.includes(m)||seg===m));
}
async function loadDb(){
  const cnt=$('#dbCount'); if(cnt) cnt.textContent='Loading…';
  try{ db=await getJSON('/api/database')||[]; }catch(_){ db=[]; }
  if(cnt) cnt.textContent=db.length+' paths';
  filterDb();
}
function filterDb(){
  const s=($('#dbSearch')?.value||'').toLowerCase();
  const hj=$('#dbHideJunk')?.checked;
  const noRule=$('#dbNoRule')?.checked;
  const cats=new Set();
  const f=db.filter(p=>{
    if(hj&&isJunkDb(p)) return false;
    if(s&&!p.toLowerCase().includes(s)) return false;
    if(noRule&&hasDbRule(p)) return false;
    const c=getDbCat(p); cats.add(c);
    return !dbCatFilter||c===dbCatFilter;
  });
  const filt=$('#dbCatFilters');
  if(filt) filt.innerHTML=['All',...[...cats].sort()].map(c=>
    `<button class="filter-btn ${dbCatFilter===(c==='All'?'':c)?'active':''}" onclick="setDbCat('${c==='All'?'':c}')">${c}</button>`).join('');
  const show=f.slice(0,200);
  const body=$('#dbBody');
  if(body) body.innerHTML=show.map(p=>{
    const ruled=hasDbRule(p);
    return `<tr class="${ruled?'watched':''}"><td><span class="db-cat">${esc(getDbCat(p))}</span></td>
      <td class="db-path" title="${esc(p)}">${esc(p)}</td>
      <td>${ruled?dbRuleBadge(p):`<button class="addbtn db-rule-btn" style="padding:4px 10px;margin:0" data-path="${esc(p)}">+ Rule</button>`}</td></tr>`;
  }).join('')+(f.length>200?`<tr><td colspan="3" class="hint-row">Showing 200/${f.length}. Narrow search.</td></tr>`:'');
  $$('#dbBody .db-rule-btn').forEach(b=>b.onclick=()=>dbAddRule(b.dataset.path));
  const cnt=$('#dbCount'); if(cnt) cnt.textContent=f.length+' matches';
}
function setDbCat(c){dbCatFilter=c;saveUiState();filterDb();}
async function dbAddRule(path){
  const r={enabled:true,name:lastSeg(path),categories:inferDbCategories(path),match:[lastSeg(path)],shape:'Star',color:'#ffd926',opacity:1,size:6,_open:true};
  drules.unshift(r); renderDrules(); await saveDrules(); filterDb();
  switchTab('filters'); highlightDrRow(0);
}
{ const el=$('#dbSearch'); if(el) el.addEventListener('input',filterDb); }
{ const el=$('#dbHideJunk'); if(el) el.onchange=filterDb; }
{ const el=$('#dbNoRule'); if(el) el.onchange=()=>{ saveUiState(); filterDb(); }; }

/* ── Live entities tab ── */
async function loadLive(silent){
  if(!silent){ const c=$('#liveCount'); if(c) c.textContent='Loading…'; }
  try{
    const alive=$('#liveAlive')?.checked;
    const rad=parseFloat($('#liveRadius')?.value||'0')||0;
    let url='/entities?limit=1000'; if(alive) url+='&alive=true'; if(rad>0) url+='&radius='+rad;
    liveEnts=await getJSON(url)||[];
    _liveEntsCache=liveEnts;
    try{ const nav=await getJSON('/api/nav'); liveNavIds=new Set((nav.selected||[]).map(s=>s.id)); }catch(_){ liveNavIds=new Set(); }
    filterLive();
    if(activeTab==='filters') renderDrules();
  }catch(_){ liveEnts=[]; filterLive(); }
}
function filterLive(){
  const q=($('#liveSearch')?.value||'').toLowerCase();
  const cats=new Set();
  let rows=liveEnts.filter(e=>{
    if(livePoiOnly&&!e.poi) return false;
    if(liveCatFilter&&e.category!==liveCatFilter) return false;
    if(q&&!((e.name||'').toLowerCase().includes(q)||(e.metadata||'').toLowerCase().includes(q)||(e.category||'').toLowerCase().includes(q))) return false;
    cats.add(e.category); return true;
  });
  const filt=$('#liveCatFilters');
  if(filt) filt.innerHTML=['All',...[...cats].sort()].map(c=>
    `<button type="button" class="filter-btn ${liveCatFilter===(c==='All'?'':c)?'active':''}" data-lc="${c==='All'?'':c}">${c}</button>`).join('');
  filt?.querySelectorAll('[data-lc]').forEach(b=>b.onclick=()=>{ liveCatFilter=b.dataset.lc; livePoiOnly=false; filterLive(); });
  const show=rows.slice(0,300);
  const body=$('#liveBody');
  if(body) body.innerHTML=show.map(e=>{
    const wr=winningRuleForEntity(e);
    const hp=e.hpMax>0?Math.round(100*e.hpCur/e.hpMax):'—';
    const navId='e:'+e.id, navOn=liveNavIds.has(navId);
    return `<tr data-id="${esc(navId)}"><td>${esc(e.name||lastSeg(e.metadata))}</td><td>${esc(e.category)}</td>
      <td class="rar-${esc(e.rarity)}">${esc(e.rarity)}</td><td class="num-r">${e.dist||0}</td>
      <td>${hp==='—'?hp:`<span class="hpbar"><i style="width:${hp}%"></i></span>`}</td>
      <td>${wr?esc(wr.name):'<span class="hint-row">—</span>'}</td>
      <td class="live-actions"><button type="button" class="chip live-nav${navOn?' on':''}">${navOn?'Nav ✓':'Nav'}</button>
      <button type="button" class="chip live-rule">+ Rule</button></td></tr>`;
  }).join('')+(rows.length>300?`<tr><td colspan="7" class="hint-row">Showing 300/${rows.length}.</td></tr>`:'');
  $$('#liveBody tr[data-id]').forEach(tr=>{
    const id=tr.dataset.id, ent=liveEnts.find(x=>'e:'+x.id===id); if(!ent) return;
    tr.querySelector('.live-nav')?.addEventListener('click',async()=>{
      try{ await fetch('/api/nav',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({toggle:id})}); await loadLive(true); }catch(_){}
    });
    tr.querySelector('.live-rule')?.addEventListener('click',()=>{
      const r={enabled:true,name:ent.name||lastSeg(ent.metadata),categories:[ent.category],match:[lastSeg(ent.metadata)],shape:'Star',color:'#ffd926',opacity:1,size:6,_open:true};
      drules.unshift(r); renderDrules(); saveDrules(); switchTab('filters'); highlightDrRow(0);
    });
  });
  const lc=$('#liveCount'); if(lc) lc.textContent=rows.length+' shown';
  const nc=$('#liveNavCount'); if(nc) nc.textContent=liveNavIds.size+' nav targets';
}
function updateLiveRuleCol(){ if(activeTab==='live') filterLive(); }
$('#liveSearch')?.addEventListener('input',filterLive);
$('#liveAlive')?.addEventListener('change',()=>loadLive());
$('#liveRadius')?.addEventListener('change',()=>loadLive());
$('#liveNavClear')?.addEventListener('click',async()=>{ try{ await fetch('/api/nav',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({clear:true})}); await loadLive(true); }catch(_){} });

function renderHidden(){
  $('#hideList').innerHTML = hidden.length ? hidden.map(p=>
    `<span class="chip on" data-p="${esc(p)}">${esc(p)} <b style="margin-left:5px;cursor:pointer">&#10005;</b></span>`).join('')
    : '<span style="color:var(--ink-faint);font-size:11px">No never-show patterns yet.</span>';
  $$('#hideList .chip').forEach(c=>c.querySelector('b').onclick=()=>{ postHidden({remove:c.dataset.p}).then(loadFilters); });
}
$('#hideAdd').onclick=()=>{
  const p=$('#hidePattern').value.trim(); if(!p) return;
  $('#hidePattern').value='';
  postHidden({add:p}).then(loadFilters);
};
$('#hidePattern').onkeydown=e=>{ if(e.key==='Enter') $('#hideAdd').click(); };

/* ── Landmarks tab: view/edit the curated map-label table (baked + user overlay) + import/export ── */
let lmEntries=[], lmAreaOnly=true, lmQ='';
function flashL(){ const m=$('#savedMsgL'); if(!m) return; m.classList.add('show'); clearTimeout(m._t); m._t=setTimeout(()=>m.classList.remove('show'),1100); markStamp('stampLm'); toast('Landmarks saved','ok'); }
async function loadLmTiles(){
  try{ const t=await getJSON('/api/tiles'); const dl=$('#lmTileList'); if(dl) dl.innerHTML=(t.tiles||[]).slice(0,500).map(p=>`<option value="${esc(p)}">`).join(''); }catch(_){}
}
async function loadLmLiveDist(){
  lmLiveDist=new Map();
  try{
    const live=await getJSON('/landmarks')||[];
    live.forEach(l=>{ if(l.path) lmLiveDist.set(l.path,l.dist); if(l.name) lmLiveDist.set(l.name,l.dist); });
  }catch(_){}
}
async function loadLandmarks(){
  try{ const r=await getJSON('/api/landmarks'); lmEntries=r.entries||[]; }catch(e){ lmEntries=[]; }
  await loadLmLiveDist();
  loadLmTiles();
  const a=$('#lmArea'); if(a && !a.value) a.value=(state&&state.areaCode)||'';
  $('#lmAreaOnly')?.classList.toggle('on',lmAreaOnly);
  renderLandmarks();
}
async function postLandmarks(body){
  try{ const r=await fetch('/api/landmarks',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}); const j=await r.json(); if(j&&j.entries) lmEntries=j.entries; flashL(); }catch(e){}
  renderLandmarks();
}
function lmDistFor(e){
  for(const [k,d] of lmLiveDist){ if(e.pattern&&e.pattern.includes(k)||(e.label&&k===e.label)) return d; }
  return null;
}
function lmRow(e){
  const badge=e.suppressed?'hidden':e.source;
  const del=e.suppressed?'Restore':(e.source==='user'?'Remove':'Hide');
  const dist=lmDistFor(e);
  return `<div class="lmrow${e.suppressed?' sup':''}" data-area="${esc(e.area)}" data-pat="${esc(e.pattern)}">
    <span class="lmbadge ${badge}">${badge}</span>
    <span class="lmarea">${esc(e.area)}</span>
    <span class="lm-dist">${dist!=null?dist+'u':'—'}</span>
    <input class="mname lmlabel" value="${esc(e.label||'')}" placeholder="${e.suppressed?'(hidden)':'label'}">
    <span class="lmpath" title="${esc(e.pattern)}">${esc(e.pattern)}</span>
    <button type="button" class="delbtn lm-del">${del}</button>
  </div>`;
}
function renderLandmarks(){
  const host=$('#lmList'); if(!host) return;
  const area=(state&&state.areaCode)||'';
  const rows=lmEntries.filter(e=>{
    if(lmAreaOnly && e.area!=='*' && e.area!==area) return false;
    if(lmQ){ if(!((e.area+' '+e.pattern+' '+(e.label||'')).toLowerCase().includes(lmQ))) return false; }
    return true;
  });
  host.innerHTML = rows.length ? rows.map(lmRow).join('')
    : `<div class="row"><div class="rl hint-row">No curated landmarks${lmAreaOnly?' for this area ('+esc(area||'—')+')':''}. Add one below${lmAreaOnly?', or turn off &ldquo;This area only&rdquo;':''}.</div></div>`;
  $$('#lmList .lmrow').forEach(row=>{
    const area=row.dataset.area, pat=row.dataset.pat, e=lmEntries.find(x=>x.area===area&&x.pattern===pat); if(!e) return;
    row.querySelector('.lmlabel').onchange=ev=>postLandmarks({set:{area,pattern:pat,label:ev.target.value}});
    row.querySelector('.lm-del').onclick=()=>{
      if(e.suppressed || e.source==='user') postLandmarks({remove:{area,pattern:pat}}); // restore baked / delete user
      else postLandmarks({set:{area,pattern:pat,label:null}});                          // suppress a baked entry
    };
  });
}
$('#lmSearch')?.addEventListener('input',e=>{ lmQ=e.target.value.toLowerCase(); renderLandmarks(); });
$('#lmAreaOnly')?.addEventListener('click',()=>{ lmAreaOnly=!lmAreaOnly; $('#lmAreaOnly').classList.toggle('on',lmAreaOnly); saveUiState(); renderLandmarks(); });
$('#lmAdd')?.addEventListener('click',()=>{
  const area=($('#lmArea').value||'').trim(), pat=($('#lmPat').value||'').trim(), label=($('#lmLabel').value||'').trim();
  if(!area||!pat||!label) return;
  $('#lmPat').value=''; $('#lmLabel').value='';
  postLandmarks({set:{area,pattern:pat,label}});
});
$('#lmExport')?.addEventListener('click',async()=>{
  try{ const txt=await (await fetch('/api/landmarks?export=1',{cache:'no-store'})).text();
    const a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([txt],{type:'application/json'}));
    a.download='CustomLandmarks.json'; a.click(); URL.revokeObjectURL(a.href);
  }catch(e){}
});
$('#lmImport')?.addEventListener('click',()=>{
  const inp=document.createElement('input'); inp.type='file'; inp.accept='.json,application/json';
  inp.onchange=()=>{ const f=inp.files&&inp.files[0]; if(!f) return; const rd=new FileReader();
    rd.onload=()=>{ try{ postLandmarks({import:JSON.parse(rd.result)}); }catch(_){ alert('Invalid JSON file'); } };
    rd.readAsText(f); };
  inp.click();
});

/* ── atlas tab (read-only inspection of the map-data we can read) ── */
async function loadAtlas(silent){
  if(!silent){ const st=$('#atlasStatus'); if(st) st.textContent='reading…'; }
  try{ atlasData=await getJSON('/api/atlas'); }catch(e){ atlasData={located:false,note:'request failed'}; }
  renderAtlas();
}
function renderAtlas(){
  const d=atlasData; if(!d){ return; }
  const st=$('#atlasStatus'); const nd=d.nodes;
  if(!(nd&&nd.total)) st.textContent = d.note ? 'scanning…' : 'atlas closed — open it in-game + Refresh';
  else st.textContent = nd.total+' nodes · '+nd.hasContent+' with content · '
        +(d.allTags?.length||0)+' content / '+(d.allMaps?.length||0)+' map filters';
  // Seed active rules from the overlay (once): tracked + arrow sets. Then render the filter table.
  if(atlasHl===null){ atlasHl=new Set((d.highlightTags||[]).map(t=>t.toLowerCase())); atlasArrow=new Set((d.arrowTags||[]).map(t=>t.toLowerCase())); }
  renderAtlasHighlight(d);
  const f=($('#atlasSearch')?.value||'').trim().toLowerCase();
  if(atlasView==='catalog') renderAtlasCatalog(d,f);
  else if(atlasView==='nodes') renderAtlasNodes(d,f);
  else renderAtlasRegion(d,f);
}
// Biome index → friendly-ish label (best-effort; index is the ground truth).
const BIOMES=['Grass','Sand','Swamp','Forest','Snow','Stone','Volcanic','Coast','Cave','Vaal','Water','Desert','Special'];
const biomeName=i=>(i>=0&&i<BIOMES.length)?BIOMES[i]:('biome '+i);

// Highlight-rule chips: one per distinct content tag on the atlas. Click to toggle → ONLY matching maps
// are drawn in-game. Active set is pushed to the overlay (persisted there).
// Classify a filter row into a category for the table (and grouping/colour).
function catContent(t){ const s=t.toLowerCase(); if(/not shown|\[dnt\]/.test(s))return'Hidden'; if(/boss/.test(s))return'Boss'; if(/influence/.test(s))return'Influence'; return'Mechanic'; }
function catMap(t){ const s=t.toLowerCase(); if(/citadel/.test(s))return'Citadel'; if(/tower/.test(s))return'Tower'; if(/temple/.test(s))return'Temple'; if(/vaal/.test(s))return'Vaal'; return'Map'; }
// Per-category colour (badge tint).
const CATCOL={Boss:'#e0533a',Mechanic:'#3ca0ff',Influence:'#a06cff',Hidden:'#ff5db1',Citadel:'#e0b341',Tower:'#2fb6a8',Temple:'#d98a2b',Vaal:'#c0395a',Map:'#8a93a0'};
function catBadge(cat){ const c=CATCOL[cat]||'#8a93a0'; return '<span style="display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;font-weight:600;background:'+c+'26;color:'+c+';border:1px solid '+c+'66">'+esc(cat)+'</span>'; }
// Build the unified filter list (content + map) with {title,count,cat,group}.
function atlasFilterRows(d){
  const rows=[];
  (d.allTags||[]).forEach(t=>rows.push({title:t.tag,count:t.count,group:'Content',cat:catContent(t.tag)}));
  (d.allMaps||[]).forEach(t=>rows.push({title:t.tag,count:t.count,group:'Map',cat:catMap(t.tag)}));
  return rows;
}
let atlasHlSort={key:'count',dir:-1};
function renderAtlasHighlight(d){
  const box=$('#atlasHlTable'); if(!box) return;
  let rows=atlasFilterRows(d);
  if(rows.length===0){ box.innerHTML='<span class="hint-row" style="padding:8px;display:block">No filters yet (open the Atlas + Refresh).</span>'; updateHlCount(); return; }
  const flt=($('#atlasHlFilter')?.value||'').trim().toLowerCase();
  if(flt) rows=rows.filter(r=>r.title.toLowerCase().includes(flt)||r.cat.toLowerCase().includes(flt)||r.group.toLowerCase().includes(flt));
  if(atlasHlSelOnly) rows=rows.filter(r=>atlasHl.has(r.title.toLowerCase())||atlasArrow.has(r.title.toLowerCase()));
  const k=atlasHlSort.key, dir=atlasHlSort.dir;
  rows.sort((a,b)=>{ let v= k==='count' ? a.count-b.count : (''+a[k]).localeCompare(''+b[k]); return v*dir || a.title.localeCompare(b.title); });
  const sa=key=> atlasHlSort.key===key ? (atlasHlSort.dir<0?' ▼':' ▲') : '';
  const cell='display:grid;grid-template-columns:30px 34px 1fr 50px 90px;gap:8px;align-items:center;padding:5px 9px';
  let html='<div style="'+cell+';position:sticky;top:0;background:var(--panel,#1a1a1a);border-bottom:1px solid var(--line);font-weight:600;font-size:11px;text-transform:uppercase;opacity:.75">'
    +'<span title="Track: route and ring the map in-game">&#9745;</span>'
    +'<span title="Arrow: edge arrow toward it when off-screen">&#10148;</span>'
    +'<span data-sort="title" style="cursor:pointer">Title'+sa('title')+'</span>'
    +'<span data-sort="count" style="cursor:pointer;text-align:right">Count'+sa('count')+'</span>'
    +'<span data-sort="cat" style="cursor:pointer">Category'+sa('cat')+'</span></div>';
  html+=rows.map(r=>{
    const key=r.title.toLowerCase(); const trk=atlasHl.has(key), arw=atlasArrow.has(key);
    return '<div class="hlrow" data-tag="'+esc(r.title)+'" style="'+cell+';cursor:pointer;border-bottom:1px solid var(--line)'+((trk||arw)?';background:rgba(60,160,255,.14)':'')+'">'
      +'<span style="font-size:15px">'+(trk?'☑':'☐')+'</span>'
      +'<span class="hlarw" data-tag="'+esc(r.title)+'" title="toggle off-screen arrow" style="font-size:15px;cursor:pointer;color:'+(arw?'#e0b341':'#4a525c')+'">➤</span>'
      +'<span title="'+esc(r.title)+'">'+esc(r.title)+'</span>'
      +'<span class="amono" style="text-align:right">'+r.count+'</span>'
      +'<span>'+catBadge(r.cat)+'</span></div>';
  }).join('');
  box.innerHTML=html;
  $$('#atlasHlTable [data-sort]').forEach(h=>h.onclick=()=>{ const key=h.dataset.sort; if(atlasHlSort.key===key) atlasHlSort.dir*=-1; else atlasHlSort={key,dir:key==='count'?-1:1}; renderAtlasHighlight(d); });
  $$('#atlasHlTable .hlarw[data-tag]').forEach(a=>a.onclick=e=>{
    e.stopPropagation(); const key=a.dataset.tag.toLowerCase();
    if(atlasArrow.has(key)) atlasArrow.delete(key); else atlasArrow.add(key);
    renderAtlasHighlight(d); postAtlasHighlight();
  });
  $$('#atlasHlTable .hlrow[data-tag]').forEach(row=>row.onclick=()=>{
    const key=row.dataset.tag.toLowerCase();
    if(atlasHl.has(key)) atlasHl.delete(key); else atlasHl.add(key);
    renderAtlasHighlight(d); postAtlasHighlight();
  });
  updateHlCount();
}
function updateHlCount(){ const el=$('#atlasHlCount'); if(el) el.textContent=(atlasHl?atlasHl.size:0)+' tracked · '+(atlasArrow?atlasArrow.size:0)+' arrow'; }
// Push the active highlight tags (original-case, from allTags) to the overlay.
async function postAtlasHighlight(){
  // Build {tag,color,track,arrow} rules: colour = the row's category colour, so in-game rings match the table.
  const rows=atlasData?atlasFilterRows(atlasData):[];
  const rules=rows.filter(r=>{const k=r.title.toLowerCase(); return atlasHl.has(k)||atlasArrow.has(k);})
    .map(r=>{const k=r.title.toLowerCase(); return {tag:r.title, color:(CATCOL[r.cat]||'#3ca0ff'), track:atlasHl.has(k), arrow:atlasArrow.has(k)};});
  try{ await fetch('/api/atlas-highlight',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({rules})}); }catch(e){}
}
$('#atlasHlClear')?.addEventListener('click',()=>{ if(!confirmAct('Clear all atlas track/arrow filters?')) return; atlasHl.clear(); atlasArrow.clear(); if(atlasData) renderAtlasHighlight(atlasData); postAtlasHighlight(); });
$('#atlasHlFilter')?.addEventListener('input',()=>{ if(atlasData) renderAtlasHighlight(atlasData); });
$('#atlasHlSelOnly')?.addEventListener('click',e=>{ atlasHlSelOnly=!atlasHlSelOnly; e.target.classList.toggle('on',atlasHlSelOnly); if(atlasData) renderAtlasHighlight(atlasData); });

// Live-nodes grid: each row is a real atlas node. Click a row to SELECT it → the overlay highlights
// it in-game (projection calibration loop). Selection is the set of element addresses.
function renderAtlasNodes(d, f){
  let list=d.nodeList||[];
  if(f) list=list.filter(n=> (''+n.id).includes(f) || biomeName(n.biome).toLowerCase().includes(f)
      || (n.map||'').toLowerCase().includes(f) || (n.hasContent&&'content'.includes(f))
      || (!n.visited&&'unvisited'.includes(f)) || ('biome '+n.biome).includes(f)
      || (n.tags||[]).some(t=>t.toLowerCase().includes(f)));   // match on map name + content names
  if(list.length===0){ $('#atlasList').innerHTML='<div class="hint-row">No live nodes (open the Atlas in-game, then Refresh).</div>'; return; }
  // Content nodes first (the interesting ones), then by tag count.
  list=list.slice().sort((a,b)=>((b.tags||[]).length)-((a.tags||[]).length));
  const head='<div class="arow ahead nrow"><span>Map</span><span>Content</span><span>Biome</span><span>Pos</span></div>';
  const body=list.slice(0,1200).map(n=>{
    const sel=atlasSel.has(n.el)?' sel':'';
    const hot=((n.map&&atlasHl.has(n.map.toLowerCase()))||(n.tags||[]).some(t=>atlasHl.has(t.toLowerCase())));
    const val=(n.tags&&n.tags.length)?' val':'';
    const content=(n.tags||[]).map(t=>'<span class="ntag tc">'+esc(t)+'</span>').join(' ')||'<span class="hint-row">—</span>';
    return '<div class="arow nrow'+val+sel+(hot?' sel':'')+'" data-el="'+esc(n.el)+'">'
      +'<span title="'+esc(n.map||'')+'">'+esc(n.map||'—')+(n.visited?' <span class="ntag tv">✓</span>':'')+'</span>'
      +'<span>'+content+'</span><span>'+esc(biomeName(n.biome))+'</span>'
      +'<span class="amono">('+n.x+','+n.y+')</span></div>';
  }).join('');
  $('#atlasList').innerHTML=head+body
    +'<div class="hint-row" style="margin-top:10px"><b>Click a node row to highlight it in-game</b> (drives the overlay’s atlas highlight — use it to confirm positions / calibrate). Click again to deselect. Showing '+Math.min(list.length,1200)+' of '+list.length+' nodes.</div>';
  $$('#atlasList .nrow[data-el]').forEach(row=>row.onclick=()=>{
    const el=row.dataset.el;
    if(atlasSel.has(el)) atlasSel.delete(el); else atlasSel.add(el);
    row.classList.toggle('sel',atlasSel.has(el));
    postAtlasSel();
  });
}
async function postAtlasSel(){ try{ await fetch('/api/atlas-select',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({els:[...atlasSel]})}); }catch(e){} }

function renderAtlasCatalog(d, f){
  let list=d.atlasCatalog||d.catalog||[];
  if(f) list=list.filter(m=>(m.name||'').toLowerCase().includes(f)||(m.code||'').toLowerCase().includes(f)||(m.type||'').toLowerCase().includes(f)||((m.tags||[]).join(' ').toLowerCase().includes(f)));
  if(list.length===0){ $('#atlasList').innerHTML='<div class="hint-row" style="padding:8px">No catalog rows.</div>'; return; }
  const head='<div class="arow ahead nrow"><span>Map</span><span>Code</span><span>Type</span><span>Tags</span></div>';
  const body=list.slice(0,1200).map(m=>'<div class="arow nrow">'
    +'<span>'+esc(m.name||'—')+'</span><span class="amono">'+esc(m.code||'')+'</span>'
    +'<span>'+esc(m.type||m.kind||'')+'</span><span>'+((m.tags||[]).map(t=>'<span class="ntag tc">'+esc(t)+'</span>').join(' ')||'—')+'</span></div>').join('');
  $('#atlasList').innerHTML=head+body+'<div class="hint-row" style="padding:8px">Showing '+Math.min(list.length,1200)+' of '+list.length+' catalog maps.</div>';
}

function renderAtlasRegion(d, f){
  let list=d.region||[];
  if(f) list=list.filter(m=>(m.name||'').toLowerCase().includes(f)||(m.code||'').toLowerCase().includes(f)||(m.kind||'').toLowerCase().includes(f));
  if(list.length===0){ $('#atlasList').innerHTML='<div class="hint-row" style="padding:8px">No region maps yet (open the Atlas in-game).</div>'; return; }
  const head='<div class="arow ahead nrow"><span>Map</span><span>Code</span><span>Kind</span><span></span></div>';
  const body=list.map(m=>'<div class="arow nrow"><span>'+esc(m.name||'—')+'</span><span class="amono">'+esc(m.code||'')+'</span><span>'+esc(m.kind||'')+'</span><span></span></div>').join('');
  $('#atlasList').innerHTML=head+body;
}

$('#atlasRefresh')?.addEventListener('click',loadAtlas);
$('#atlasSearch')?.addEventListener('input',()=>{ if(atlasData) renderAtlas(); });
$$('#atlasViewCatalog,#atlasViewRegion,#atlasViewNodes').forEach(b=>b?.addEventListener('click',()=>{
  atlasView=b.dataset.view;
  $$('#atlasViewCatalog,#atlasViewRegion,#atlasViewNodes').forEach(x=>x.classList.toggle('on',x===b));
  renderAtlas();
}));

/* ── left rail ── */
function renderState(){
  const s=state; if(!s) return;
  const hp=Math.max(0,Math.min(100,s.hpPct||0)), mp=Math.max(0,Math.min(100,s.manaPct||0)), es=Math.max(0,Math.min(100,s.esPct||0));
  $('#hpBar').style.width=hp+'%'; $('#mpBar').style.width=mp+'%'; $('#esBar').style.width=es+'%';
  $('#hpNum').textContent=hp.toFixed(0)+'%'; $('#mpNum').textContent=mp.toFixed(0)+'%'; $('#esNum').textContent=es.toFixed(0)+'%';
  const areaName=(s.areaName&&s.areaName!==s.areaCode)?s.areaName:'';
  $('#kAreaName').textContent=areaName||s.areaCode||'—';
  $('#kArea').textContent=s.areaCode||'—';
  const act=s.areaAct||0;
  $('#kAlvl').textContent=(act?'Act '+act+' · ':'')+(s.areaLevel?('lvl '+s.areaLevel):'—');
  $('#kMap').textContent=s.mapVisible?'yes':'no';
  $('#kFlask').textContent=(s.autoFlask?'on':'off')+(s.flask?' · '+s.flask:'');
  const fs=$('#flaskState'); if(fs) fs.textContent=(s.autoFlask?'ON':'OFF')+(s.flask?' · '+s.flask:'');
  $('#cEnt').textContent=s.entityCount||0;
  $('#cPoi').textContent=s.poiCount||0;
  $('#cMon').textContent=(s.counts&&s.counts.Monster)||0;
  $('#cLm').textContent=s.landmarkCount||0;
  $('#areaChip').innerHTML = (areaName||s.areaCode||'—') + ' <b>·</b> ' + (s.inGame?'in game':'town/menu');
  const cc=$('#charChip'); if(cc&&s.charName){ cc.hidden=false; $('#charName').textContent=s.charName; $('#charLvl').textContent=s.charLevel||'—'; }
  updateHeaderQuick(); refreshHeaderJunk();

  const zn=$('#zoneNotes');
  if(zone && (zone.notes||'').trim()){
    zn.hidden=false;
    zn.innerHTML='<div class="zt">'+esc(zone.title||zone.name||'')+'</div>'+esc(zone.notes);
  } else { zn.hidden=true; }
}

// Update banner: show a download link if a newer version exists on GitHub (best-effort).
async function checkVersion(){
  try{
    const v=await getJSON('/api/version');
    if(v && v.updateAvailable){
      const b=$('#updateBanner'); if(!b) return;
      const m=$('#updateMsg'); if(m) m.textContent=' — '+(v.latest||'')+' (you have v'+(v.current||'?')+')';
      b.href=v.url||'#'; b.hidden=false; b.style.display='flex';
    }
  }catch(e){}
}

wireSettings(); wireHotkeys(); wireHpBars(); wireTerrain();
loadUiState();
loadIcons().then(()=>{
  if(window._pendingSetSec) showSettingsSection(window._pendingSetSec);
  switchTab(activeTab);
  if(activeTab!=='filters') loadFilters();
  refreshHeaderJunk();
});
tick(); setInterval(tick, 1000);
checkVersion();
</script>
</body>
</html>
""";
}
