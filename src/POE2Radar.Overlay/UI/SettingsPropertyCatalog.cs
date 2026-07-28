using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Settings;

namespace POE2Radar.Overlay.UI;

internal readonly record struct SettingsPropertyPresentation(
    string Category,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<int, string>? IntegerChoices = null,
    bool IsHotkey = false);

/// <summary>
/// Human-facing information architecture for the classic property sheets. This metadata is kept
/// outside RadarSettings so JSON serialization and the live settings model remain unchanged.
/// </summary>
internal static partial class SettingsPropertyCatalog
{
    private const string General = "01 · General";

    private static readonly IReadOnlyDictionary<int, string> PriceSources = new Dictionary<int, string>
    {
        [0] = "poe.ninja",
        [1] = "poe2scout",
    };

    private static readonly IReadOnlyDictionary<int, string> DisplayCurrencies = new Dictionary<int, string>
    {
        [0] = "Divine",
        [1] = "Exalted",
        [2] = "Chaos",
    };

    private static readonly IReadOnlyDictionary<int, string> LootDisplayCurrencies = new Dictionary<int, string>
    {
        [LootTrackerSettings.CurrencyAuto] = "Automatic",
        [LootTrackerSettings.CurrencyDivine] = "Divine",
        [LootTrackerSettings.CurrencyExalted] = "Exalted",
        [LootTrackerSettings.CurrencyChaos] = "Chaos",
    };

    private static readonly IReadOnlyDictionary<int, string> BorderStyles = new Dictionary<int, string>
    {
        [0] = "Solid",
        [1] = "Dashed",
        [2] = "Dotted",
    };

    private static readonly IReadOnlyDictionary<int, string> ArrowCorners = new Dictionary<int, string>
    {
        [0] = "Top-left",
        [1] = "Top-right",
        [2] = "Bottom-left",
        [3] = "Bottom-right",
    };

    private static readonly IReadOnlyDictionary<int, string> PickupModes = new Dictionary<int, string>
    {
        [0] = "Assist only",
        [1] = "Hovered item · hold",
        [2] = "Nearby items · hold",
        [3] = "Nearby items · automatic toggle",
    };

    private static readonly IReadOnlyDictionary<int, string> CraftingModes = new Dictionary<int, string>
    {
        [0] = "Guided · you click",
        [1] = "Automatic clicks",
    };

    private static readonly IReadOnlyDictionary<int, string> CraftingTargets = new Dictionary<int, string>
    {
        [0] = "Waystones",
        [1] = "Precursor tablets",
    };

    private static readonly IReadOnlyDictionary<int, string> CraftingRecipes = new Dictionary<int, string>
    {
        [0] = "Upgrade",
        [1] = "Corrupt / Ancient Infuser",
        [2] = "Paranoia guidance / Alchemy",
    };

    private static readonly IReadOnlyDictionary<int, string> RunecraftColorModes = new Dictionary<int, string>
    {
        [0] = "No value tint",
        [1] = "Relative to visible rewards",
        [2] = "Fixed value thresholds",
    };

    public static SettingsPropertyPresentation For(
        object target,
        string page,
        PropertyDescriptor property)
    {
        var name = property.Name;
        var displayName = DisplayName(target, name) ?? FilteredSettingsView.FriendlyName(name);
        var category = Category(target, page, name);
        var description = Description(target, name, displayName, property.PropertyType);
        return new SettingsPropertyPresentation(
            category,
            displayName,
            description,
            Choices(target, name),
            IsHotkey(name));
    }

    private static string Category(object target, string page, string name)
        => target switch
        {
            PickupHelperSettings => PickupCategory(name),
            PickupPolicySettings => PickupPolicyCategory(name),
            LootTrackerSettings => LootTrackerCategory(name),
            StashValueSettings => StashValueCategory(name),
            StashUtilitySettings => StashUtilityCategory(name),
            WaystoneAlchemySettings => CraftingCategory(name),
            AmanamuSettings => AmanamuCategory(name),
            RitualSettings => RitualCategory(name),
            RunecraftSettings => RunecraftCategory(name),
            SekhemaSettings => SekhemaCategory(name),
            HpBarSettings => HpBarCategory(name),
            TerrainSettings => TerrainCategory(name),
            IconStyle => IconStyleCategory(name),
            CampaignSettings => CampaignCategory(name),
            RadarSettings => RadarCategory(page, name),
            _ => General,
        };

    private static string CampaignCategory(string name)
        => name switch
        {
            nameof(CampaignSettings.Enabled)
                or nameof(CampaignSettings.AutoActivate)
                or nameof(CampaignSettings.AutoRoute)
                or nameof(CampaignSettings.SafeAutoCheck)
                or nameof(CampaignSettings.GuideMode)
                or nameof(CampaignSettings.ShowCompletedObjectives) => "01 · Operation",
            nameof(CampaignSettings.WidgetX)
                or nameof(CampaignSettings.WidgetY)
                or nameof(CampaignSettings.WidgetScale)
                or nameof(CampaignSettings.WidgetOpacity)
                or nameof(CampaignSettings.WidgetCollapsed) => "02 · Widget",
            nameof(CampaignSettings.ShowDiagnosticTargetStatus) => "03 · Diagnostics",
            _ => General,
        };

    private static string PickupCategory(string name)
        => name switch
        {
            nameof(PickupHelperSettings.Enabled)
                or nameof(PickupHelperSettings.Mode)
                or nameof(PickupHelperSettings.HumanSpeed)
                or nameof(PickupHelperSettings.AutoModeAcknowledged) => "01 · Operation",
            nameof(PickupHelperSettings.ActivationHotkey)
                or nameof(PickupHelperSettings.EmergencyStopHotkey)
                or nameof(PickupHelperSettings.ShowHiddenItemsHotkey) => "02 · Controls",
            nameof(PickupHelperSettings.MaxPickupDistance) => "03 · Targeting",
            nameof(PickupHelperSettings.ShowTargetHighlight) => "04 · Display",
            nameof(PickupHelperSettings.PauseWhileShowHiddenHeld)
                or nameof(PickupHelperSettings.MaxMissesBeforeCooldown) => "05 · Safety",
            _ when name.EndsWith("Ms", StringComparison.Ordinal)
                || name.Contains("Delay", StringComparison.Ordinal)
                || name.Contains("Timeout", StringComparison.Ordinal) => "06 · Timing",
            _ => General,
        };

    private static string PickupPolicyCategory(string name)
        => name switch
        {
            nameof(PickupPolicySettings.AllowEquipment)
                or nameof(PickupPolicySettings.AllowPatterns) => "01 · Include",
            nameof(PickupPolicySettings.DenyPatterns) => "02 · Exclude",
            nameof(PickupPolicySettings.PriorityPatterns) => "03 · Priority",
            _ => General,
        };

    private static string LootTrackerCategory(string name)
        => name switch
        {
            nameof(LootTrackerSettings.Enabled)
                or nameof(LootTrackerSettings.KeepVisibleAfterRun)
                or nameof(LootTrackerSettings.ShowKills) => "01 · Operation",
            nameof(LootTrackerSettings.PriceSource)
                or nameof(LootTrackerSettings.League)
                or nameof(LootTrackerSettings.RefreshIntervalMin)
                or nameof(LootTrackerSettings.DisplayCurrency)
                or nameof(LootTrackerSettings.ShowPricesInDivineOnly) => "02 · Pricing",
            nameof(LootTrackerSettings.HistorySize)
                or nameof(LootTrackerSettings.MaxSessions) => "03 · Session history",
            nameof(LootTrackerSettings.BarBottomOffset)
                or nameof(LootTrackerSettings.BarOnRight)
                or nameof(LootTrackerSettings.BarOpacity)
                or nameof(LootTrackerSettings.CompactHeight)
                or nameof(LootTrackerSettings.CompactWidth)
                or nameof(LootTrackerSettings.UiScale) => "04 · Layout",
            nameof(LootTrackerSettings.ShowPickupToasts)
                or nameof(LootTrackerSettings.NotifyMinEx)
                or nameof(LootTrackerSettings.NotifyDurationSec) => "05 · Pickup notifications",
            nameof(LootTrackerSettings.DetailsHotkey) => "06 · Controls",
            _ => General,
        };

    private static string StashValueCategory(string name)
        => name switch
        {
            nameof(StashValueSettings.ShowOverlay)
                or nameof(StashValueSettings.ShowInventoryOverlay)
                or nameof(StashValueSettings.HidePriceOnHover)
                or nameof(StashValueSettings.MinValueEx) => "01 · Display rules",
            nameof(StashValueSettings.PriceSource)
                or nameof(StashValueSettings.League)
                or nameof(StashValueSettings.RefreshIntervalMin)
                or nameof(StashValueSettings.DisplayCurrency) => "02 · Pricing",
            nameof(StashValueSettings.PriceFontScale)
                or nameof(StashValueSettings.PriceOffsetX)
                or nameof(StashValueSettings.PriceOffsetY)
                or nameof(StashValueSettings.PriceTextColor) => "03 · Label appearance",
            nameof(StashValueSettings.ShowDebugInfo) => "04 · Diagnostics",
            _ => General,
        };

    private static string StashUtilityCategory(string name)
    {
        if (name is nameof(StashUtilitySettings.EnableWaystones)
            or nameof(StashUtilitySettings.EnableTablets)
            or nameof(StashUtilitySettings.IncludeStash)
            or nameof(StashUtilitySettings.IncludeInventory)
            or nameof(StashUtilitySettings.ShowWaystoneTiersOnHover)
            or nameof(StashUtilitySettings.ShowTabletTiersOnHover))
            return "01 · Scope";
        if (name.StartsWith("Filter", StringComparison.Ordinal)
            || name.StartsWith("Min", StringComparison.Ordinal)
            || name.StartsWith("Max", StringComparison.Ordinal)
            || name == nameof(StashUtilitySettings.HideNormalWaystones))
            return "02 · Waystone thresholds";
        if (name.StartsWith("GreatBy", StringComparison.Ordinal)
            || name.StartsWith("GreatItem", StringComparison.Ordinal)
            || name.StartsWith("GreatPack", StringComparison.Ordinal)
            || name.StartsWith("GreatDrop", StringComparison.Ordinal)
            || name.StartsWith("GreatExplicit", StringComparison.Ordinal))
            return "03 · GREAT thresholds";
        if (name.Contains("WaystoneMods", StringComparison.Ordinal)
            || name is nameof(StashUtilitySettings.BadOnlyWhenNumericalFiltersPass)
                or nameof(StashUtilitySettings.RedTakesPriority))
            return "04 · Waystone mod lists";
        if (name.EndsWith("Color", StringComparison.Ordinal))
            return "07 · Colors";
        if (name.Contains("Tablet", StringComparison.Ordinal))
            return "05 · Tablet rules";
        return "06 · Marker appearance";
    }

    private static string CraftingCategory(string name)
        => name switch
        {
            nameof(WaystoneAlchemySettings.Enabled)
                or nameof(WaystoneAlchemySettings.Mode)
                or nameof(WaystoneAlchemySettings.TargetType)
                or nameof(WaystoneAlchemySettings.Recipe) => "01 · Operation",
            nameof(WaystoneAlchemySettings.RunHotkey)
                or nameof(WaystoneAlchemySettings.EmergencyStopHotkey) => "02 · Controls",
            nameof(WaystoneAlchemySettings.MinimumTier)
                or nameof(WaystoneAlchemySettings.UseRegalOnMagic)
                or nameof(WaystoneAlchemySettings.ApplyExaltedToRare)
                or nameof(WaystoneAlchemySettings.DesiredExplicitMods) => "03 · Waystones",
            nameof(WaystoneAlchemySettings.DesiredTabletExplicitMods)
                or nameof(WaystoneAlchemySettings.TabletAlchemyUnlocked) => "04 · Tablets",
            nameof(WaystoneAlchemySettings.ActionDelayMs)
                or nameof(WaystoneAlchemySettings.AutoModeAcknowledged) => "05 · Safety",
            _ => General,
        };

    private static string AmanamuCategory(string name)
        => name switch
        {
            nameof(AmanamuSettings.Enabled)
                or nameof(AmanamuSettings.OnlyRareOrUnique)
                or nameof(AmanamuSettings.MaxDistanceGrid) => "01 · Detection",
            nameof(AmanamuSettings.ShowWorldOverlay)
                or nameof(AmanamuSettings.ShowMapMarkers)
                or nameof(AmanamuSettings.DrawLabels)
                or nameof(AmanamuSettings.DrawOffscreenArrows)
                or nameof(AmanamuSettings.DrawCircle) => "02 · Display",
            nameof(AmanamuSettings.CircleRadius)
                or nameof(AmanamuSettings.LabelYOffset)
                or nameof(AmanamuSettings.ArrowEdgeMargin) => "03 · Geometry",
            _ => "04 · Colors",
        };

    private static string RitualCategory(string name)
        => name switch
        {
            nameof(RitualSettings.ShowOverlay)
                or nameof(RitualSettings.ShowPricesWindow) => "01 · Display",
            nameof(RitualSettings.PriceSource)
                or nameof(RitualSettings.League)
                or nameof(RitualSettings.RefreshIntervalMin)
                or nameof(RitualSettings.DisplayCurrency)
                or nameof(RitualSettings.MinDisplayExalted) => "02 · Pricing",
            nameof(RitualSettings.PlayValueAlert)
                or nameof(RitualSettings.AlertMinDivine)
                or nameof(RitualSettings.AlertSound) => "03 · Value alert",
            nameof(RitualSettings.PriceFontScale)
                or nameof(RitualSettings.PriceOffsetX)
                or nameof(RitualSettings.PriceOffsetY)
                or nameof(RitualSettings.PriceTextColor) => "04 · Label appearance",
            _ => "05 · Diagnostics",
        };

    private static string RunecraftCategory(string name)
    {
        if (name is nameof(RunecraftSettings.PriceSource)
            or nameof(RunecraftSettings.League)
            or nameof(RunecraftSettings.RefreshIntervalMin))
            return "01 · Pricing";
        if (name is nameof(RunecraftSettings.ShowOverlay)
            or nameof(RunecraftSettings.ColorMode)
            or nameof(RunecraftSettings.OverlayXOffset)
            or nameof(RunecraftSettings.HighlightBestRecipe)
            or nameof(RunecraftSettings.HighlightLockedRecipe))
            return "02 · Reward overlay";
        if (name is nameof(RunecraftSettings.ShowExpeditionPlanner)
            or nameof(RunecraftSettings.ShowExpeditionRouteOnMap)
            or nameof(RunecraftSettings.ShowExpeditionNextPlacementWorld)
            or nameof(RunecraftSettings.ExpeditionManualCharges)
            or nameof(RunecraftSettings.ExpeditionMinMarkersPerSpareCharge))
            return "05 · Expedition planner";
        if (name.StartsWith("Expedition", StringComparison.Ordinal))
            return "06 · Expedition scoring";
        if (name.Contains("Map", StringComparison.Ordinal)
            || name.StartsWith("MapValue", StringComparison.Ordinal))
            return "03 · Map labels";
        if (name.Contains("Monolith", StringComparison.Ordinal)
            && !name.StartsWith("Expedition", StringComparison.Ordinal))
            return "04 · Monolith window";
        return "07 · Diagnostics";
    }

    private static string SekhemaCategory(string name)
    {
        if (name is nameof(SekhemaSettings.Enabled)
            or nameof(SekhemaSettings.CurrentProfile)
            or nameof(SekhemaSettings.Profiles))
            return "01 · Profile";
        if (name is nameof(SekhemaSettings.DrawBestPath)
            or nameof(SekhemaSettings.BestPathColor)
            or nameof(SekhemaSettings.FrameThickness))
            return "02 · Best path";
        if (name.StartsWith("Suppress", StringComparison.Ordinal)
            || name.Contains("Threshold", StringComparison.Ordinal))
            return "03 · Reward suppression";
        if (name.StartsWith("Hazard", StringComparison.Ordinal)
            || name == nameof(SekhemaSettings.DrawHazardRoute))
            return "04 · Hazards";
        if (name.Contains("Chest", StringComparison.Ordinal))
            return "05 · Chest priority";
        if (name.Contains("Portal", StringComparison.Ordinal)
            || name.Contains("Lever", StringComparison.Ordinal)
            || name == nameof(SekhemaSettings.RoomObjectMarkerRadius))
            return "06 · Room objects";
        return "07 · Diagnostics";
    }

    private static string HpBarCategory(string name)
        => name switch
        {
            nameof(HpBarSettings.UseTextures)
                or nameof(HpBarSettings.Height)
                or nameof(HpBarSettings.OffsetX)
                or nameof(HpBarSettings.OffsetY) => "01 · Shared geometry",
            _ when name.StartsWith("Width", StringComparison.Ordinal) => "02 · Width by rarity",
            _ when name.StartsWith("BorderColor", StringComparison.Ordinal)
                || name == nameof(HpBarSettings.EnergyShieldColor) => "04 · Colors",
            _ => "03 · Border thickness",
        };

    private static string TerrainCategory(string name)
        => name.StartsWith("Interior", StringComparison.Ordinal)
            ? "01 · Walkable fill"
            : "02 · Boundary edge";

    private static string IconStyleCategory(string name)
        => name == nameof(IconStyle.Sprite) ? "02 · Sprite" : "01 · Marker appearance";

    private static string RadarCategory(string page, string name)
        => page switch
        {
            "Performance" => name switch
            {
                nameof(RadarSettings.LowImpactMode)
                    or nameof(RadarSettings.FpsCap)
                    or nameof(RadarSettings.OverlayVSync) => "01 · Frame pacing",
                nameof(RadarSettings.LiveRefreshHz)
                    or nameof(RadarSettings.WorldRefreshHz)
                    or nameof(RadarSettings.InactiveRefreshHz)
                    or nameof(RadarSettings.HpBarRefreshHz)
                    or nameof(RadarSettings.MaxLiveHpBars) => "02 · Read cadence",
                nameof(RadarSettings.SmoothOverlayMotion)
                    or nameof(RadarSettings.OverlaySmoothingMs)
                    or nameof(RadarSettings.ChipSmoothingMs)
                    or nameof(RadarSettings.PixelSnapLabels) => "03 · Motion smoothing",
                _ => "04 · Metrics",
            },
            "Flasks" => name.Contains("Mana", StringComparison.Ordinal)
                ? "02 · Mana flask"
                : name.Contains("Key", StringComparison.Ordinal)
                  || name.Contains("Hotkey", StringComparison.Ordinal)
                    ? "03 · Controls"
                    : "01 · Life / energy shield flask",
            "Hotkeys" => name.StartsWith("Gamepad", StringComparison.Ordinal)
                ? "01 · Controller"
                : "02 · Bindings",
            "Application" => name switch
            {
                nameof(RadarSettings.GameExePath) => "01 · Game install",
                nameof(RadarSettings.ApiPort) => "02 · Web dashboard",
                nameof(RadarSettings.UiFontPath)
                    or nameof(RadarSettings.UiFontSize)
                    or nameof(RadarSettings.UiFontGlyphRange) => "03 · In-game font",
                nameof(RadarSettings.HideFromScreenCapture) => "04 · Privacy",
                _ => "05 · Taskbar",
            },
            "Atlas" => AtlasCategory(name),
            "Radar" => RadarDisplayCategory(name),
            _ => page,
        };

    private static string AtlasCategory(string name)
    {
        if (name.Contains("Uncharted", StringComparison.Ordinal)
            || name.Contains("Ship", StringComparison.Ordinal)
            || name.Contains("Leyline", StringComparison.Ordinal))
            return "06 · Uncharted Waters";
        if (name.Contains("IslandRumour", StringComparison.Ordinal))
            return "07 · Island Rumours";
        if (name.Contains("Ritual", StringComparison.Ordinal))
            return "08 · Ritual line";
        if (name.Contains("Route", StringComparison.Ordinal)
            || name.Contains("PathTo", StringComparison.Ordinal)
            || name.Contains("DrawLines", StringComparison.Ordinal))
            return "04 · Routes";
        if (name.Contains("Search", StringComparison.Ordinal)
            || name.StartsWith("AtlasHide", StringComparison.Ordinal))
            return "03 · Search and filters";
        if (name.Contains("Content", StringComparison.Ordinal)
            || name.Contains("Biome", StringComparison.Ordinal)
            || name.Contains("MapGroup", StringComparison.Ordinal))
            return "05 · Content and biomes";
        if (name.Contains("Scale", StringComparison.Ordinal)
            || name.Contains("Offset", StringComparison.Ordinal)
            || name.Contains("Nudge", StringComparison.Ordinal)
            || name.Contains("Base", StringComparison.Ordinal))
            return "02 · Layout";
        return "01 · Nodes and labels";
    }

    private static string RadarDisplayCategory(string name)
    {
        if (name.StartsWith("ShowPath", StringComparison.Ordinal)
            || name.Contains("Waypoint", StringComparison.Ordinal)
            || name.Contains("AutoPath", StringComparison.Ordinal))
            return "03 · Navigation paths";
        if (name.Contains("Scale", StringComparison.Ordinal)
            || name is nameof(RadarSettings.OffX) or nameof(RadarSettings.OffY))
            return "04 · Calibration";
        if (name.Contains("Landmark", StringComparison.Ordinal))
            return "02 · Landmarks";
        return "01 · Map contents";
    }

    private static string? DisplayName(object target, string name)
        => (target, name) switch
        {
            (PickupHelperSettings, nameof(PickupHelperSettings.HumanSpeed)) => "Fast human-like cadence",
            (PickupHelperSettings, nameof(PickupHelperSettings.Mode)) => "Pickup mode",
            (PickupHelperSettings, nameof(PickupHelperSettings.ActivationHotkey)) => "Activation hotkey",
            (PickupHelperSettings, nameof(PickupHelperSettings.MaxMissesBeforeCooldown)) => "Misses before cooldown",
            (PickupHelperSettings, nameof(PickupHelperSettings.AutoModeAcknowledged)) => "I understand automatic mode",
            (PickupPolicySettings, nameof(PickupPolicySettings.AllowPatterns)) => "Only pick names containing",
            (PickupPolicySettings, nameof(PickupPolicySettings.DenyPatterns)) => "Never pick names containing",
            (PickupPolicySettings, nameof(PickupPolicySettings.PriorityPatterns)) => "Pick first when name contains",
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.AutoModeAcknowledged)) => "I understand automatic clicks",
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.TargetType)) => "Item type",
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.Mode)) => "Control mode",
            (StashUtilitySettings, nameof(StashUtilitySettings.GoodWaystoneMods)) => "Required waystone mods",
            (StashUtilitySettings, nameof(StashUtilitySettings.GodTabletMods)) => "GREAT tablet mods",
            (StashUtilitySettings, nameof(StashUtilitySettings.GoodTabletMods)) => "Required tablet mods",
            (RitualSettings, nameof(RitualSettings.MinDisplayExalted)) => "Minimum displayed value (Exalted)",
            (RunecraftSettings, nameof(RunecraftSettings.ColorMode)) => "Value color mode",
            _ => null,
        };

    private static string Description(object target, string name, string displayName, Type propertyType)
    {
        var explicitDescription = ExplicitDescription(target, name);
        if (explicitDescription is not null) return explicitDescription;
        if (name.EndsWith("Hotkey", StringComparison.Ordinal) || name.EndsWith("Key", StringComparison.Ordinal))
            return "Shows the key name and its stored value. Use the legacy advanced editor for press-to-bind capture.";
        if (name.EndsWith("Color", StringComparison.Ordinal))
            return "Marker color in #RRGGBB hexadecimal format.";
        if (name.EndsWith("Opacity", StringComparison.Ordinal))
            return "Visibility from 0 (invisible) to 1 (opaque).";
        if (name.EndsWith("Pct", StringComparison.Ordinal) || name.Contains("ThresholdPct", StringComparison.Ordinal))
            return $"{displayName} as a percentage.";
        if (name.EndsWith("Ms", StringComparison.Ordinal))
            return $"{displayName} in milliseconds.";
        if (name.EndsWith("Min", StringComparison.Ordinal))
            return $"{displayName} in minutes.";
        if (name.EndsWith("OffsetX", StringComparison.Ordinal))
            return "Move the element horizontally in pixels; negative moves left.";
        if (name.EndsWith("OffsetY", StringComparison.Ordinal))
            return "Move the element vertically in pixels; negative moves up.";
        if (propertyType != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
            return "Select this row and use its (…) button to edit the collection.";
        if (propertyType == typeof(bool))
            return $"Turn {displayName.ToLowerInvariant()} on or off.";
        return $"Controls {displayName.ToLowerInvariant()}.";
    }

    private static string? ExplicitDescription(object target, string name)
        => (target, name) switch
        {
            (PickupHelperSettings, nameof(PickupHelperSettings.Enabled))
                => "Master switch for pickup assistance. Input remains game-focus and UI-state gated.",
            (PickupHelperSettings, nameof(PickupHelperSettings.Mode))
                => "Assist highlights only; held modes require the activation bind; automatic toggle runs until stopped.",
            (PickupHelperSettings, nameof(PickupHelperSettings.HumanSpeed))
                => "Use the quicker bounded cadence while preserving cooldowns, confirmation, focus, and emergency-stop checks.",
            (PickupHelperSettings, nameof(PickupHelperSettings.AutoModeAcknowledged))
                => "Required acknowledgement before the automatic toggle mode may click items.",
            (PickupHelperSettings, nameof(PickupHelperSettings.MaxPickupDistance))
                => "Farthest label distance eligible for pickup. Lower values avoid reaching across the screen.",
            (PickupPolicySettings, nameof(PickupPolicySettings.AllowEquipment))
                => "Off excludes weapons, armour, jewellery, flasks, and charms. Turn on only if you want equipment considered.",
            (PickupPolicySettings, nameof(PickupPolicySettings.AllowPatterns))
                => "Optional comma or newline-separated fragments. Empty allows every non-equipment item.",
            (PickupPolicySettings, nameof(PickupPolicySettings.DenyPatterns))
                => "Comma or newline-separated fragments. A deny match always wins.",
            (PickupPolicySettings, nameof(PickupPolicySettings.PriorityPatterns))
                => "Ordered fragments. Items matching an earlier fragment are selected before later matches.",
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.Enabled))
                => SettingHints.CraftingAssistant.Enabled,
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.Mode))
                => "Guided mode only highlights the next action. Automatic mode moves and clicks the mouse.",
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.TargetType))
                => SettingHints.CraftingAssistant.Target,
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.Recipe))
                => SettingHints.CraftingAssistant.Recipe,
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.AutoModeAcknowledged))
                => SettingHints.CraftingAssistant.AutoAck,
            (WaystoneAlchemySettings, nameof(WaystoneAlchemySettings.TabletAlchemyUnlocked))
                => "Confirm that Partial Translation is unlocked before Alchemy is allowed on normal tablets.",
            (RitualSettings, nameof(RitualSettings.ShowOverlay)) => SettingHints.Ritual.ShowOverlay,
            (RitualSettings, nameof(RitualSettings.ShowPricesWindow)) => SettingHints.Ritual.ShowPricesWindow,
            (RitualSettings, nameof(RitualSettings.PriceSource)) => SettingHints.Ritual.PriceSource,
            (RitualSettings, nameof(RitualSettings.League)) => SettingHints.Ritual.League,
            (RitualSettings, nameof(RitualSettings.RefreshIntervalMin)) => SettingHints.Ritual.RefreshIntervalMin,
            (RunecraftSettings, nameof(RunecraftSettings.ShowOverlay)) => SettingHints.Runecraft.ShowOverlay,
            (RunecraftSettings, nameof(RunecraftSettings.ColorMode)) => SettingHints.Runecraft.ColorMode,
            (RunecraftSettings, nameof(RunecraftSettings.ShowMapLabels)) => SettingHints.Runecraft.ShowMapLabels,
            (RunecraftSettings, nameof(RunecraftSettings.ShowMonolithWindow)) => SettingHints.Runecraft.ShowMonolithWindow,
            (AmanamuSettings, nameof(AmanamuSettings.Enabled)) => SettingHints.Amanamu.Enabled,
            (AmanamuSettings, nameof(AmanamuSettings.OnlyRareOrUnique)) => SettingHints.Amanamu.RareOnly,
            (AmanamuSettings, nameof(AmanamuSettings.MaxDistanceGrid)) => SettingHints.Amanamu.Distance,
            (StashValueSettings, nameof(StashValueSettings.PriceSource))
                => "Public price-data provider used for stash and inventory values.",
            (StashValueSettings, nameof(StashValueSettings.MinValueEx))
                => "Hide price labels below this Exalted Orb value.",
            (LootTrackerSettings, nameof(LootTrackerSettings.KeepVisibleAfterRun))
                => "Keep the completed run summary visible after leaving the area.",
            (LootTrackerSettings, nameof(LootTrackerSettings.ShowPickupToasts))
                => "Show a short notification when a pickup meets the minimum value.",
            (SekhemaSettings, nameof(SekhemaSettings.CurrentProfile))
                => "Weight profile used to score Trial of the Sekhemas room choices.",
            (SekhemaSettings, nameof(SekhemaSettings.Profiles))
                => "Profile weight tables. Use the legacy advanced editor for detailed dictionary editing.",
            _ => null,
        };

    private static IReadOnlyDictionary<int, string>? Choices(object target, string name)
    {
        if (name == "PriceSource"
            && target is RitualSettings or RunecraftSettings or StashValueSettings or LootTrackerSettings)
            return PriceSources;
        if (name == "DisplayCurrency" && target is LootTrackerSettings)
            return LootDisplayCurrencies;
        if (name == "DisplayCurrency"
            && target is RitualSettings or StashValueSettings)
            return DisplayCurrencies;
        if (target is PickupHelperSettings && name == nameof(PickupHelperSettings.Mode))
            return PickupModes;
        if (target is WaystoneAlchemySettings)
        {
            if (name == nameof(WaystoneAlchemySettings.Mode)) return CraftingModes;
            if (name == nameof(WaystoneAlchemySettings.TargetType)) return CraftingTargets;
            if (name == nameof(WaystoneAlchemySettings.Recipe)) return CraftingRecipes;
        }
        if (target is RunecraftSettings && name == nameof(RunecraftSettings.ColorMode))
            return RunecraftColorModes;
        if (target is StashUtilitySettings)
        {
            if (name is nameof(StashUtilitySettings.GoodBorderStyle)
                or nameof(StashUtilitySettings.BadBorderStyle))
                return BorderStyles;
            if (name == nameof(StashUtilitySettings.GreatArrowCorner))
                return ArrowCorners;
        }
        return null;
    }

    private static bool IsHotkey(string name)
        => name.EndsWith("Hotkey", StringComparison.Ordinal)
           || name is nameof(RadarSettings.LifeKey) or nameof(RadarSettings.ManaKey);

    internal sealed class IntegerChoiceConverter : TypeConverter
    {
        private readonly IReadOnlyDictionary<int, string> _choices;

        public IntegerChoiceConverter(IReadOnlyDictionary<int, string> choices) => _choices = choices;

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object value)
        {
            if (value is string text)
            {
                foreach (var choice in _choices)
                    if (string.Equals(choice.Value, text, StringComparison.CurrentCultureIgnoreCase))
                        return choice.Key;
                if (int.TryParse(text, NumberStyles.Integer, culture, out var parsed))
                    return parsed;
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            if (destinationType == typeof(string) && value is int number)
                return _choices.TryGetValue(number, out var label) ? label : number.ToString(culture);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(_choices.Keys.ToArray());
    }

    internal sealed partial class HotkeyValueConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object value)
        {
            if (value is string text)
            {
                var match = StoredNumberRegex().Match(text.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out var stored))
                    return stored;
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(text[2..], NumberStyles.HexNumber, culture, out var hex))
                    return hex;
                if (int.TryParse(text, NumberStyles.Integer, culture, out var number))
                    return number;
                for (var vk = 0; vk <= 0xFF; vk++)
                    if (string.Equals(VirtualKeyHelper.Name(vk), text, StringComparison.OrdinalIgnoreCase))
                        return vk;
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            if (destinationType == typeof(string) && value is int number)
                return number <= 0 ? "(none)" : $"{HotkeyCodes.DisplayName(number)} ({number})";
            return base.ConvertTo(context, culture, value, destinationType);
        }

        [GeneratedRegex(@"\((-?\d+)\)\s*$", RegexOptions.CultureInvariant)]
        private static partial Regex StoredNumberRegex();
    }
}
