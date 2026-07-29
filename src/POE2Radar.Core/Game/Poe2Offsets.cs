namespace POE2Radar.Core.Game;

/// <summary>
/// PoE2 memory offsets — the going-forward source of truth, sourced from the GameHelper2
/// <c>GameOffsets/</c> dump and validated against the live client where marked ✓.
///
/// <para>This is separate from the legacy PoE1-shaped <see cref="KnownOffsets"/> (which the
/// overlay still references and which is being migrated). As each PoE2 structure is validated
/// here, the corresponding overlay reader is rechained to use it.</para>
///
/// Unofficial cross-ref after patches: https://github.com/imkk000/poe2-offsets (validate via Research, do not copy blindly).
///
/// Markers: ✓ = confirmed against live PoE2; (GH2) = from GameHelper2, not yet live-checked;
/// ✗ = transcribed from a third-party IDA dump (a private fork), NOT yet validated against our
/// live client and NOT yet wired into any read path. Validate via the Research probes before using
/// any ✗ offset — patch drift means these may be wrong for the current build.
/// </summary>
public static class Poe2
{
    /// <summary>Tile→world = 250, tile→grid = 23 ⇒ world/grid ratio ≈ 10.8696. ✓</summary>
    public const float WorldToGridRatio = 250f / 23f;

    /// <summary>Conservative network-bubble radius in grid units (GH2 uses 150). </summary>
    public const int NetworkBubbleGrid = 150;

    /// <summary>
    /// GameState root — found via the "Game States" AOB pattern (<see cref="AobPatterns"/>).
    /// Holds the array of game-state slots; one of them is InGameState.
    /// </summary>
    public static class GameState
    {
        public const int CurrentStatePtr = 0x08;  // (GH2) StdVector — current state
        public const int States          = 0x48;  // (GH2) inline array of 12 × StdTuple2D<IntPtr> (16 bytes each)
        public const int StateSlotStride = 0x10;   // each slot is StdTuple2D<IntPtr> (ptr + extra)
        public const int StateSlotCount  = 12;
    }

    /// <summary>
    /// InGameState. Resolve it from <c>GameState.CurrentStatePtr</c> (StdVector @ +0x08): the
    /// vector's first element is the active state pointer when in-game. ✓ (matches States[] slot).
    /// </summary>
    public static class InGameState
    {
        public const int AreaInstanceData = 0x290; // ✓ → AreaInstance (validated: target holds the local player)
        /// <summary>Keyboard/mouse <see cref="UiRootStruct"/> manager — atlas fingerprint walk starts here
        /// (GameHelper <c>GameUi.Address</c>). Dereference <see cref="UiRootStruct.UiRootPtr"/> for the
        /// outer UiElement tree.</summary>
        public const int KeyboardUiRootStructPtr = 0x2F0; // ✓ live 0.5.x (was mislabeled direct UiRoot)
        public const int GamepadUiRootStructPtr  = 0x318; // ✓ controller UiRootStruct
        public const int MouseOverHostPtr        = 0x300; // ✓ host -> +0x3F0 -> +0xA8 hovered entity
        public const int UiRoot = KeyboardUiRootStructPtr; // legacy alias — prefer <see cref="UiRootResolver"/>
        public const int UiRootStructPtr = KeyboardUiRootStructPtr; // legacy name
        public const int Camera           = 0x368; // ✓ → Camera object (Zoom @ +0x528 == 1.0 confirmed)
        public const int WorldData        = 0x310; // (GH2-drift) → WorldData (area name + camera) — TBD
    }

    public static class UiRootStruct
    {
        public const int UiRootPtr           = 0x340; // ✓ live 0.5.x (was GH2 0x5A8)
        public const int GameUiPtr           = 0xBE0; // (GH2) inner HUD branch inside the manager
        public const int GameUiControllerPtr = 0xBE8; // (GH2) controller HUD branch
        public const int LeftPanelPtr        = 0x6D8; // (GH2) currently open left-side panel
        public const int RightPanelPtr       = 0x6E0; // (GH2) currently open right-side panel
    }

    public static class MouseOver
    {
        public const int HostSubPtr = 0x3F0; // ✓ GameHelper PoE2 v0.5.4
        public const int SubEntityPtr = 0xA8; // ✓ 0 when nothing is hovered
    }

    /// <summary>
    /// The big per-area container: area metadata, player, entity maps, terrain.
    /// <para>⚠ GameHelper2's internal offsets are DRIFTED in this build — confirmed by the live
    /// probe (PlayerInfo moved from GH2's 0xA00 to ~0x5A0; LocalPlayer at 0x5C0). The values
    /// marked (GH2-drift) below must be re-discovered (see <c>--find-entities</c> / <c>--find-terrain</c>).</para>
    /// </summary>
    public static class AreaInstance
    {
        public const int AreaInfoPtr      = 0x0A0;  // ✓ → AreaInfo; +0x00 → UTF-16 "Code\0Name\0" (Code validated 'G1_town')
        public const int LocalPlayer      = 0x5C0;  // ✓ → player Entity (2026-07-17 drift: was 0x5B8)
        public const int ServerDataPtr    = 0x5A0;  // ✓ PlayerInfo base; +0x00 -> ServerData object, +0x20 -> LocalPlayer (2026-07-17: was 0x598)
        public const int AwakeEntities    = 0x6E0;  // ✓ StdMap of live entities (2026-07-17: was 0x6D8; validated size=112)
        public const int SleepingEntities = 0x6F0;  // ✓ StdMap (2026-07-17: was 0x6E8; validated size=15)
        public const int TerrainMetadata  = 0x8C0;  // ✓ TerrainStruct base (2026-07-17: was 0x8B8)
        public const int CurrentAreaLevel = 0x0C4;  // ✓ int — per-area, validated 27/32 (GH2's 0xBC drifted)
        public const int CurrentAreaHash  = 0x11C;  // ✓ uint — per-area random hash (GH2's 0xFC drifted; +0x120 paired seed)
        /// <summary>Inline StdVector&lt;StatArrayStruct&gt; containing current area modifiers.</summary>
        public const int MapStats         = 0x158;  // GH2 RunecraftHelper Expedition planner
    }

    /// <summary>Expedition encounter controller reached through ServerData.</summary>
    public static class ExpeditionController
    {
        public const int ServerDataPointer = 0x2618;
        public const int PlacedExplosives  = 0x220; // StdVector&lt;pointer&gt;
        public const int TotalExplosives   = 0x2B0; // low byte is the usable count
    }

    public static class ExpeditionStats
    {
        public const int PlacementRangePercent = 13685;
        public const int ExplosiveRadiusPercent = 13471;
    }

    /// <summary>Server-side minimap icons stored inside ServerData (scanned dynamically).</summary>
    public static class ServerIcon
    {
        public const int Stride    = 0xC0;    // ✓ inline array element size
        public const int RowPtr    = 0x00;    // ✓ -> row; *(row+0x00) -> UTF-16 name
        public const int ID        = 0x10;    // ✓
        public const int GridX     = 0x14;    // ✓
        public const int GridY     = 0x18;    // ✓
        public const int ScanSpan  = 0x28000; // ✓ scan range within ServerData for the vector
        public const int GridLimit = 5000;    // ✓ plausible grid upper bound
    }

    /// <summary>Entity StdMap conventions. Maps live at AreaInstance+0x6E0 (Awake) / +0x6F0 (Sleeping).</summary>
    public static class EntityList
    {
        public const int StdMapSize = 0x10; // each StdMap is {Head ptr, int Size, pad} = 16 bytes
        /// <summary>Entity ids below this are real entities; above are visuals/decorations (GH2 filter). ✓ confirmed live.</summary>
        public const uint VisualIdThreshold = 0x40000000;
    }

    /// <summary>std::map node: Left/Parent/Right ptrs, Color, IsNil byte, then Data{Key,Value} @ +0x20.</summary>
    public static class StdMapNode
    {
        public const int Left   = 0x00;
        public const int Parent = 0x08;
        public const int Right  = 0x10;
        public const int IsNil  = 0x19; // bool
        public const int Data   = 0x20; // Key (EntityNodeKey: uint id + pad = 8 bytes), then Value (IntPtr EntityPtr)
        public const int KeyId  = 0x20; // uint entity id
        public const int ValueEntityPtr = 0x28; // IntPtr
    }

    /// <summary>An Entity object.</summary>
    public static class Entity
    {
        public const int EntityDetailsPtr = 0x08; // ✓ → EntityDetails
        public const int ComponentList    = 0x10; // ✓ StdVector of component pointers (8-byte elems)
        public const int Id               = 0x80; // (GH2) uint  (read 0 for local player — revisit)
        public const int IsValid          = 0x84; // (GH2) byte; valid when bit0 clear
    }

    public static class EntityDetails
    {
        public const int Name              = 0x08; // ✓ StdWString — metadata path (e.g. Metadata/Characters/<Class>/<Variant>)
        public const int ComponentLookUpPtr = 0x28; // ✓ → ComponentLookUp
    }

    /// <summary>ComponentLookUp: a StdBucket of (NamePtr, Index) at +0x28; index → ComponentList[index].</summary>
    public static class ComponentLookUp
    {
        public const int NameAndIndexBucket = 0x28; // ✓ StdBucket; its Data StdVector starts here
        public const int EntryStride        = 0x10; // ✓ {IntPtr NamePtr; int Index; int pad}
    }

    // ── Components (offsets from the component object base) ───────────────────

    /// <summary>Life — ✓ re-validated live 2026-06-04 after the patch (980/980 HP, 427 mana, 274 ES).
    /// The vital blocks slid (each grew ~8 bytes): Health 0x1A8→0x1B0, Mana 0x1F8→0x208, ES 0x230→0x248.
    /// The VitalStruct's internal layout (Max@+0x2C, Current@+0x30) was UNCHANGED — only these
    /// per-vital offsets moved. (Prior build: 442/442 HP, 271 mana, 186/186 ES at 0x1A8/0x1F8/0x230.)</summary>
    public static class Life
    {
        public const int Owner        = 0x008; // ComponentHeader.EntityPtr (back-pointer to entity)
        public const int Health       = 0x1B0; // ✓ VitalStruct (was 0x1A8 pre-patch)
        public const int Mana         = 0x208; // ✓ VitalStruct (was 0x1F8 pre-patch)
        public const int EnergyShield = 0x248; // ✓ VitalStruct (was 0x230 pre-patch)
    }

    /// <summary>VitalStruct — ✓ (Max/Current confirmed). Reuse <see cref="VitalStruct"/> for reads.</summary>
    public static class Vital
    {
        public const int ReservedFlat = 0x10;
        public const int Regen        = 0x28;
        public const int Max          = 0x2C; // ✓
        public const int Current      = 0x30; // ✓
    }

    /// <summary>Render component.</summary>
    public static class Render
    {
        public const int TerrainHeight        = 0x130; // ✓ float; ground Z used by minimap projection
        public const int CurrentWorldPosition = 0x138; // ✓ Vector3 (X,Y,Z); grid = XY / WorldToGridRatio
        public const int ModelBounds          = 0x144; // candidate (3 floats right after world pos)
    }

    /// <summary>Player component — character name + level. ✓ validated (name StdWString, level byte 27).</summary>
    public static class PlayerComponent
    {
        public const int Name  = 0x1B0; // ✓ StdWString
        public const int Level = 0x204; // ✓ byte (low byte of a u32 slot)
    }

    /// <summary>Camera object (at InGameState+0x368). Holds the WorldToScreen matrix.</summary>
    public static class Camera
    {
        // The matrix is stored duplicated (two identical 0x40-byte copies back-to-back); the first
        // copy is at +0x1A0. Row-major Matrix4x4; screen = project(world * M). Validated visually.
        public const int WorldToScreenMatrix = 0x1A0;
        public const int Zoom = 0x528; // float, == 1.0 confirmed
    }

    /// <summary>MinimapIcon component — present on entities the game marks as map POIs (waypoints,
    /// checkpoints, league encounters…). <see cref="CompletedState"/> is an int the game flips when a
    /// repeatable encounter is finished: it then FADES the icon rather than removing it. ✓ validated
    /// live on an Expedition2Encounter — 0 while not-started/ready/active/looting, 1 after the reward
    /// was claimed. Read it live (don't cache the value): the component stays put; only the flag flips.</summary>
    public static class MinimapIcon
    {
        public const int CompletedState = 0x10; // ✓ int — 0 = active/shown, non-zero = completed/faded
        public const int IconRow         = 0x20; // -> row; *(row) -> UTF-16 icon id
    }

    public static class TriggerableBlockage
    {
        public const int IsBlocked = 0x30;
    }

    /// <summary>ObjectMagicProperties component — monster/chest rarity.</summary>
    public static class ObjectMagicProperties
    {
        // ✓ validated live across 21 monsters (values 0 and 2 seen). Enum: 0=Normal,1=Magic,2=Rare,3=Unique.
        public const int Rarity = 0x144;
        public const int Mods = 0x150; // GH2: Details1.Mods vectors
    }

    /// <summary>Buffs component. GameHelper2-sourced; validate with a Research probe after patches.</summary>
    public static class Buffs
    {
        public const int StatusEffects = 0x160; // GH2 BuffsOffsets.StatusEffectPtr, vector&lt;nint&gt;
    }

    /// <summary>One active status-effect object and its BuffDefinitions.dat row.</summary>
    public static class StatusEffect
    {
        public const int BuffDefinition = 0x08; // GH2 StatusEffectStruct.BuffDefinationPtr
        public const int BuffDefinitionName = 0x00; // GH2 BuffDefinitionsOffset.Name -> UTF-16
        public const int PointerStride = 0x08;
        public const int MaxCount = 256;
    }

    /// <summary>Chest component. ✓ OpenState @ +0x168 — the offset is stable, but the 2026-06-06 patch
    /// INVERTED its polarity: now 0 = closed/openable, non-zero = opened/used (was 1=closed/0=opened,
    /// per the 2026-06-03 read). Re-validated live by diffing a rare chest closed-vs-opened (+0x168
    /// flipped 0→1). The fork's extra sub-offsets did NOT survive validation on our build.</summary>
    public static class ChestComponent
    {
        public const int OpenState       = 0x168; // ✓ 0 = closed/openable, non-zero = opened/used (polarity flipped 2026-06-06)
        // ⚠ INVALID on our build (live 2026-06-03, G3_3): 0x20/0x21/0x25 read 184/7/127 — identical
        // across a magic AND a normal chest, sitting inside pointer bytes (component header). The
        // fork's IDA offsets drifted; the real Locked/Large flags need rediscovery (--validate).
        public const int OpeningDestroys = 0x20;  // ⚠ INVALID — pointer-field garbage; do not use
        public const int Large           = 0x21;  // ⚠ INVALID — pointer-field garbage; do not use
        public const int Locked          = 0x25;  // ⚠ INVALID — pointer-field garbage; do not use
    }

    /// <summary>Monster component (name confirmed live: "Monster"). ⚠ The fork's IsBoss did NOT
    /// validate: a Unique boss ("Mighty Silverfist", QuadrillaBoss) still read 0 at +0x27 because the
    /// byte is the high byte of a pointer at +0x20 (2026-06-03). Use Rarity == Unique (✓ validated) to
    /// flag bosses/uniques instead — IsBoss here is both wrong and redundant.</summary>
    public static class MonsterComponent
    {
        public const int IsBoss = 0x27; // ⚠ INVALID — pointer high-byte, 0 even for a Unique boss; use Rarity
    }

    /// <summary>Targetable component (name confirmed live: "Targetable"). ⚠ The fork's field offsets
    /// did NOT validate: +0x18 read a constant 144 (0x90) across every monster (2026-06-03), so it is
    /// NOT the IsTargetable bool. Offsets need rediscovery.</summary>
    public static class Targetable
    {
        public const int Attackable   = 0x17; // ⚠ unconfirmed (read 0); likely wrong
        public const int IsTargetable = 0x18; // ⚠ INVALID — read constant 144, not a bool; rediscover
    }

    /// <summary>Pathfinding component (name confirmed live: "Pathfinding"). BaseSpeed PLAUSIBLE —
    /// read varying values ~1183–1338 across monsters (2026-06-03), looks like a real per-monster int,
    /// but the "speed / 0 ⇒ immobile" semantics are unconfirmed. Flying suspect (read 4/5, not a bool).</summary>
    public static class PathfindingComponent
    {
        public const int BaseSpeed = 0xEC; // ✗ int — plausible (varies per monster); semantics unconfirmed
        public const int Flying    = 0xE5; // ⚠ suspect — read 4/5, not a clean bool
    }

    /// <summary>AreaTransition component. ✗ IDA-sourced, NOT yet validated (no transitions in the
    /// validation sample). Validate via <c>--validate</c> near a zone exit before use.</summary>
    public static class AreaTransitionComponent
    {
        public const int GracePeriod   = 0x18; // ✗ float — unvalidated
        public const int TeleportDelay = 0x1C; // ✗ float — unvalidated
    }

    /// <summary>Positioned component.</summary>
    public static class Positioned
    {
        // ✓ validated live: player (friendly) = 0x01, hostile MastodonBoss = 0x00.
        // GameHelper2 rule: IsFriendly = (Reaction & 0x7F) == 1.
        public const int Reaction = 0x1E0;

        // ✓ validated live (presence buff on/off sweep, Research --presence): the presence
        // area-of-effect scalar. Float, defaults to 1.0; a "+20% Presence AoE" buff drove it to
        // 1.0 from a ~0.92 base (≈ √1.2 radius scaling), and it tracked the buff on→off→on with
        // nothing else moving. Effective presence radius = base radius × this scalar.
        public const int PresenceAoeScale = 0x2A0;
    }

    /// <summary>
    /// TerrainStruct (base at AreaInstance+0x8C0). Validated live: TotalTiles (54,48) -> 2592 tiles
    /// (matches TileDetails count); walkable grid 685584 bytes; BytesPerRow 621 → cellsPerRow 1242;
    /// grid 1242×1104 = (54×23)×(48×23). PoE2 has FOUR grid layers (0xD0/0xE8/0x100/0x118), so
    /// BytesPerRow sits at 0x130 — not GH2's 0x100.
    /// </summary>
    public static class Terrain
    {
        public const int TotalTiles        = 0x18;  // ✓ StdTuple2D<long> (tilesX, tilesY)
        public const int TileDetailsPtr    = 0x28;  // ✓ StdVector of TileStructure (0x38 bytes)
        public const int GridWalkableData  = 0xD0;  // ✓ StdVector — packed walkable grid bytes
        public const int GridLandscapeData = 0xE8;  // ✓ StdVector
        public const int GridLayer3        = 0x100; // ✓ StdVector (extra PoE2 layer)
        public const int GridLayer4        = 0x118; // ✓ StdVector (extra PoE2 layer)
        public const int BytesPerRow       = 0x130; // ✓ int (621 live) — cellsPerRow = ×2
        public const int TileGridCells     = 23;    // tile = 23×23 grid cells
    }

    /// <summary>One entry in Terrain.TileDetailsPtr (0x38 bytes). ✓ validated (TgtPath gives tile names).</summary>
    public const int TileStructureSize = 0x38;
    public static class TileStructure
    {
        public const int SubTileDetailsPtr = 0x00; // pointer
        public const int TgtFilePtr        = 0x08; // ✓ → TgtFileStruct
        public const int TileHeight        = 0x30; // short
        public const int RotationSelector  = 0x36; // byte
    }

    public static class TgtFileStruct
    {
        public const int TgtPath = 0x08; // ✓ StdWString — full tile .tdt path (e.g. .../Feature/arena_01.tdt)
    }

    // ── Map UI — validated live against the standalone client ──
    public static class ImportantUi
    {
        public const int MapParentPtr = 0x7C8; // ✓ live standalone 2026-07-23, from GameUi manager
    }

    public static class MapParent
    {
        // The legacy names are retained for callers, but these are not two MapUiElements:
        // +0x28 is shared map content (Shift/Zoom + live anchor); +0x30 is the corner frame UiElement.
        public const int LargeMapPtr = 0x28; // ✓ shared map-content MapUiElement, live standalone 2026-07-23
        public const int MiniMapPtr  = 0x30; // ✓ 402x402 corner-minimap frame UiElement, live standalone 2026-07-23
    }

    /// <summary>
    /// MapUiElement (large map + minimap share this class/vtable). ✓ validated live: exactly two
    /// elements carry DefaultShift=(0,-20) with Zoom=0.5. Struct shape matches GH2 (shifted +0x70):
    /// Shift→DefaultShift = 8, DefaultShift→Zoom = 0x38.
    /// </summary>
    public static class MapUiElement
    {
        public const int Shift        = 0x368; // ✓ StdTuple2D<float>
        public const int DefaultShift = 0x370; // ✓ StdTuple2D<float> (0,-20)
        public const int Zoom         = 0x3A8; // ✓ float (0.5 live)
    }

    /// <summary>StateMachine component — drives stateful devices (runeshape monoliths).</summary>
    public static class StateMachine
    {
        public const int ListenerVec = 0x20; // ✓ StdVector of listener-node ptrs
        public const int StatesPtr = 0x158; // ✓ ptr to state-name table
        public const int StatesValues = 0x160; // ✓ StdVector<long> parallel to state names
        public const int Used = 0x10; // terminal-state byte: 0 active, non-zero finished
        public const int StateStructSize = 0xC0;
    }

    /// <summary>RuneStation heap object behind a runeshape monolith device. ✓ validated 2026-06-20.</summary>
    /// <summary>
    /// Trial of the Sekhemas room-map and HUD layout. Sourced from MordWraith/Gamehelper 1.4.8
    /// (snapshot 7e7a235, 2026-07-18); keep league-specific drift isolated here.
    /// </summary>
    public static class Sekhema
    {
        public const int GameUiPanelChild = 84;
        public const int PanelChild = 0;

        public const int PanelFloorObject = 0x3B8;
        public const int FloorObjectVariantFlag = 0x25A;
        public const int FloorDataActive = 0x1F8;
        public const int FloorDataAlternate = 0x1B0;

        public const int FloorLayers = 0x00;
        public const int FloorClassifications = 0x18;
        public const int FloorChoices = 0x38;
        public const int FloorCounter = 0x40;
        public const int LayerStride = 0x20;
        public const int RoomStride = 0x38;
        public const int ClassificationStride = 0x40;
        public const int WidgetContentForeignKey = 0x4D8;
        public const int ResourceText = 0x4C0;

        // UI role fingerprints, with UiElement visible bit (0x800) masked before comparison.
        // These are the patch-resistant fallback when a child index moves.
        public const uint WaterFp0 = 0x00502EF1;
        public const uint WaterFp1 = 0x00502EF1;
        public const uint WaterFp2 = 0x00502EF3;
        public const uint WaterFp3 = 0x00502EE1;
        public const uint HonourFp0 = 0x005026F1;
        public const uint HonourFp1 = 0x00502EF1;
        public const uint HonourFp2 = 0x00502EF7;

        public const int StatsChangedByItemsPtr = 0x160;
        public const int StatsChangedByBuffsPtr = 0x1C8;
        public const int StatsVectorInContainer = 0xF8;
        public const int StatEntryStride = 0x08;
    }

    public static class RuneStation
    {
        public const int Owner = 0x10;
        public const int AnchorRef = 0x28;
        public const int AnchorHolder = 0x30;
        public const int HoleCount = 0x38;
        public const int AnchorPos = 0x3C;
        public const int SelectedRecipe = 0x60; // ✓ locked/selected recipe row ptr (sealed monolith)
        public const int PanelOpenListener = 0xB8; // ✓ in-module vtable while combinations panel open
        public const int ListenerSub = 0xA0; // ✓ listener node ptr = station + 0xA0 (2026-06-25 patch shifted +0x08, was 0x98; re-validated live: N=4 Tidal @ hole 3)
        public const int RuneStride = 0x68; // ✓ Expedition2Runes row stride (anchorIdx = (rowPtr-base)/stride). 2026-06-25 patch: 0x6C→0x68 (re-validated: delta 0x5B0/0x68 = 14 = Tidal)
        public const int RuneCount = 34;
    }

    /// <summary>WorldItem component on dropped item containers.</summary>
    public static class WorldItemComponent
    {
        public const int ItemEntity = 0x28; // ⚠ → inner item entity
    }

    /// <summary>RenderItem component — 2D art path on the inner item entity.</summary>
    public static class RenderItemComponent
    {
        public const int ResourcePath = 0x28; // ⚠ → UTF-16 .dds art path
    }

    /// <summary>Base component — rendered display name for non-unique price lookup.</summary>
    public static class BaseComponent
    {
        public const int NameRow = 0x10;
        public const int RowDisplayName = 0x30;
    }

    /// <summary>Mods component on items — rarity/identified at different offsets than OMP.</summary>
    public static class ModsComponent
    {
        public const int Identified = 0x90;
        public const int Rarity = 0x94;
        public const int Mods = 0xA0; // GH2: Details0.Mods vectors
        public const int StatsFromMods = 0x148; // GH2: vector<StatArrayStruct>
    }

    public static class QualityComponent
    {
        public const int Value = 0x18; // GH2 QualityOffsets.ItemQuality
    }

    public static class StackComponent
    {
        public const int Count = 0x18; // GH2 StackOffsets.Count
    }

    /// <summary>Ritual Favours UI item slot.</summary>
    public static class RitualUi
    {
        public const int TileItemEntity = 0x4F8; // GH2/RitualHelper: item entity pointer on reward tile UiElement
    }

    public static class ModVectors
    {
        public const int Implicit = 0x00;
        public const int Explicit = 0x18;
        public const int Enchant = 0x30;
        public const int Hellscape = 0x48;
        public const int Crucible = 0x60;
        public const int EntryStride = 0x40;
    }

    /// <summary>ServerData league name for price auto-detect. ✓ validated 2026-06-22.</summary>
    public static class ServerData
    {
        public const int PlayerDataVector = 0x48; // GH LootTracker: vector<IntPtr>, [0] -> local player server data
        public const int League = 0x21E0;
    }

    /// <summary>MainInventory1 chain used by LootTracker-style inventory diffs.</summary>
    public static class PlayerData
    {
        public const int Inventories = 0x320; // vector<InventoryArrayStruct>
    }

    public static class InventoryArrayEntry
    {
        public const int Stride = 0x18;
        public const int Id = 0x00;
        public const int InventoryPtr = 0x08;
        public const int MainInventory1Id = 1;
    }

    public static class InventoryStruct
    {
        public const int TotalBoxes = 0x150; // int X, int Y
        public const int ItemList = 0x170;   // vector<IntPtr invItem>
    }

    public static class InventoryItemStruct
    {
        public const int ItemEntity = 0x00;
    }

    /// <summary>UiElement base — ✓ validated live (GH2's offsets drifted: Self 0x30→0x8, Flags 0x1B8→0x180).
    /// Parent/Position/Size from the 2026-06-07 community offset dump (resources/additional offsets.txt);
    /// Position + Size confirmed live on the atlas-node class (size = 40×40 icons, positions vary per node).</summary>
    public static class UiElement
    {
        public const int Self           = 0x08;  // ✓ self pointer
        public const int Children       = 0x10;  // ✓ StdVector begin (child UiElement ptrs); End @ +0x18
        public const int ChildrenEnd    = 0x18;  // ✓ StdVector end
        public const int Parent         = 0xB8;  // (community) parent UiElement; true UI root = *(UiRoot+0xB8)
        public const int PositionModifier = 0x0E0; // (GH2) parent position modifier, used when flags bit 0x0A is set (map path)
        public const int UiPositionModifier = 0xF0; // screen-rect path (GameHelper UiElementBase)
        public const int RelativePos    = 0x118; // ✓ StdTuple2D<float> position relative to parent (varies per atlas node)
        public const int ScrollOffset   = 0x120; // ✓ StdTuple2D<float> scroll translation on viewport mask elements
        public const int LocalScaleMultiplier = 0x12C; // (GH2) UI element scale multiplier (map/minimap path)
        public const int LocalScaleMul = 0x130; // screen-rect path scale multiplier
        public const int ScaleIndex      = 0x130; // atlas zoom on node elements (map path)
        public const int UiScaleIndex = 0x18A; // byte selecting UI scale row (screen-rect path)
        public const int Text = 0x390; // std::wstring displayed text (loot tags)
        public const int Flags          = 0x180; // ✓ uint; IsVisibleLocal = bit 0x0B (toggle-diff: 0x2EF1↔0x26F1)
        public const int FlagModifyPositionBit = 0x0A; // (GH2) add parent PositionModifier while resolving position
        public const int FlagModifyPosBit = 0x0A;
        public const int FlagVisibleBit = 0x0B;  // ✓ visible bit (set when shown)
        public const int SizeW          = 0x288; // ✓ float unscaled width  (atlas node = 40)
        public const int SizeH          = 0x28C; // ✓ float unscaled height (atlas node = 40)
        public const double BaseResW = 2560.0;
        public const double BaseResH = 1600.0;
        // Full visibility is hierarchical: an element is shown iff its own bit 0x0B AND every
        // ancestor's bit are set. Walk Parent (+0xB8) up to the root.
    }

    /// <summary>Atlas map-node UiElement (a subclass with its own vtable; ~1200+ instances live in the
    /// open Atlas). Fields from the 2026-06-07 community dump; structurally confirmed live: biome
    /// (+0x32E) spread 0..12, per-node positions (UiElement.RelativePos), 40×40 size, scale (+0x130) =
    /// the atlas zoom. (+0x300 is a map-TYPE id shared by same-type nodes — NOT unique per node.)
    ///
    /// <para><b>PROJECTION (✓ live, pan + zoom):</b> a node's on-screen position is
    /// <c>screen = (UIscale × zoom) × relPos + offset</c>, where relPos = +0x118 (read live; the game
    /// rewrites it on PAN so pan is free), zoom = +0x130 (read live; ~0.85 max zoom-out → larger zoomed
    /// in), UIscale = winH/1600, offset ≈ factor×½icon ≈ (15,13) @ 1080p/zoom-0.85. NOT a perspective
    /// homography. The overlay derives the WHOLE projection live from the window height + live zoom
    /// (RadarApp.AtlasProjection) — resolution-correct with no calibration. <b>Recovery after a patch:</b> run
    /// <c>POE2Radar.Research --atlas-probe</c> (Atlas map open) — it re-locates the class + canvas,
    /// validates every offset, and prints the derived projection. Only the node-class vtable drifts.
    /// See resources/atlas-research-notes.md "FULLY SOLVED".</para></summary>
    public static class AtlasNode
    {
        public const int MapNodeId   = 0x300; // ✓ u32 — distinct per node
        public const int Content     = 0x310; // (community) u32 content (0 = none)
        public const int State       = 0x32C; // (community) u8 state (seen =1 on loaded nodes)
        public const int Biome       = 0x32E; // ✓ u8 biome index (0..12)
        public const int Flags       = 0x32F; // (community) u8: bit0 unlocked, bit1 visited
        public const int GridPos     = 0x320; // ✓ live 2026-06-08 — StdTuple2D<int> atlas grid coord (X,Y); 1:1 with node, range small (e.g. X[-16..31] Y[0..47]). The key for node-graph pathfinding. (GameHelper2-sourced)
        public const int Completion  = 0x339; // (community) u8 per-node completion id
        public const int ContentVec  = 0x350; // (community) StdVector begin (content list); End @ +0x358

        /// <summary>Alternate node-DATA model (GameHelper2): <c>*(*(node+0x10)+0x20)</c> → a struct with
        /// biome <c>+0x2CE</c> / status byte <c>+0x2CF</c> (bit0 accessible, bit1 completed) / mapId at
        /// <c>+0x2A0</c> (ptr→ptr→ptr→UTF-16 "MapXxx"). Validated live 2026-06-08 (biome matches the
        /// element's own <see cref="Biome"/> 200/200). POE2Radar reads biome/mapId DIRECTLY off the
        /// element (<see cref="Biome"/>, <see cref="MapNodeId"/> + the +0x300 EndgameMaps row), so this
        /// deeper model is an alternate source, not required.</summary>
        public const int DataStorage = 0x10;   // *(node+0x10) → storage
        public const int DataModel   = 0x20;   // *(storage+0x20) → nodeData
        public const int DataBiome   = 0x2CE;  // u8 within nodeData
        public const int DataStatus  = 0x2CF;  // u8 within nodeData: bit0 accessible, bit1 completed
        public const int DataMapId   = 0x2A0;  // ptr chain → UTF-16 "MapXxx"
    }

    /// <summary>Atlas CONNECTION GRAPH (✓ live 2026-06-08, GameHelper2-sourced). The node canvas (the
    /// parent holding the most node-class children — POE2Radar's detected <c>_nodeCanvas</c>) carries a
    /// <c>StdVector</c> of edges at <c>+0x5A8</c>. Each edge is 20 bytes: <c>{ int unknown; StdTuple2D&lt;int&gt;
    /// source; StdTuple2D&lt;int&gt; target }</c> — source @ +0x04, target @ +0x0C, both in node grid
    /// coords (<see cref="AtlasNode.GridPos"/>). Live: 291 edges, 100% endpoints on real grid positions,
    /// avg degree 2.9 / max 5 (a real sparse atlas graph). This is what enables "route from the player's
    /// current node to a target node in the fewest hops" (A* over the graph, per GH2's FindShortestPathAStar).
    /// Re-discover after a patch with <c>POE2Radar.Research --atlas-graph</c>.</summary>
    public static class AtlasGraph
    {
        public const int ConnectionsVec = 0x5A8; // on the node canvas: StdVector<edge> begin; End @ +0x5B0
        public const int EdgeStride     = 20;
        public const int EdgeSourceOff  = 0x04;  // StdTuple2D<int>
        public const int EdgeTargetOff  = 0x0C;  // StdTuple2D<int>

        /// <summary>Current-location ("player icon") marker: the SINGLE non-node UiElement in the atlas
        /// UI subtree whose <c>+0x300</c> field points at a node-class element. That target node is the map
        /// the player is currently in (✓ live 2026-06-08 — held even while standing in a hideout). The
        /// accessor is structural, not vtable-keyed (the marker's class drifts per patch), so it's found by
        /// "the lone non-node element whose +0x300 ∈ node set". <c>currentNode = *(marker + 0x300)</c>, then
        /// read the node's <see cref="AtlasNode.GridPos"/>. Re-discover with <c>--atlas-marker</c>.</summary>
        public const int CurrentMarkerNodePtr = 0x300;

        // Ritual atlas line (Atlas2 / yokkenUA) — fields on the node-list / panel container.
        // Re-validate with Research after patches; values from GameHelper Atlas2 RitualFeatures.
        public const int RitualPanelLineMode = 0x637; // u8 — zero=normal atlas, nonzero=ritual line
        public const int RitualPanelLineId = 0x63C;   // u32 TinyMT seed word 0
        public const int RitualPanelPendingVec = 0x648;
        public const int RitualPanelCommittedVec = 0x660;
        public const int RitualCandTableBegin = 0x590;
        public const int RitualCandTableEnd = 0x598;
        public const int RitualCandEntryStride = 0x44;
        public const int RitualStatsRoot = 0x320;
        public const int RitualStatsRootNext = 0x1B0;
        public const int RitualStatsHolder = 0x3A20;
        public const int RitualStatsVec = 0x408;
        public const int RitualStatsEntryStride = 0x28;
        public const int RitualStatsId = 0x00;
        public const int RitualStatsValue = 0x08;

        // Atlas region buttons (GameHelper ImportantUiElements). These are non-node children of the
        // node canvas. Ocean row 2 drives the Uncharted Waters ship/leyline overlay.
        public const int RegionButtonRowPtr = 0x320;
        public const int RegionButtonGrid = 0x330;
        public const int RegionButtonRowIndex = 0x338;
        public const int OceanRegionButtonRow = 2;
    }

    /// <summary>Atlas screen panel — a PERSISTENT direct child of UiRoot (the element at
    /// <c>InGameState+0x2F0</c>, walked via its Children StdVector <c>+0x10</c>) at <see cref="UiRootChildIndex"/>.
    /// Present from a cold launch even when the atlas has NEVER been opened (✓ live 2026-06-08); its
    /// UiElement visible bit (Flags <c>+0x180</c> bit <c>0x0B</c>) is the only thing that toggles when the
    /// atlas opens/closes (closed flags 0x5626F5 → open 0x562EF5). This is the cheap atlas open-gate:
    /// reading this one element's visible bit is ~4 reads, versus BFS-walking the ~50k-element UI tree to
    /// (re)detect the node class — which while the atlas is closed can never succeed and so would burn that
    /// BFS every retry. <b>If a patch shifts UiRoot's children this index drifts</b> — re-discover by
    /// diffing the DevTree <c>/api/ui-flat</c> tree closed-vs-open (the direct child whose visible bit
    /// flips SHOWN when the Endgame Atlas MAP opens — DevTree 2026-06-13: <c>root/17</c>, ~8 children;
    /// do not confuse with <c>root/12</c> which is the Endgame shell visible when closed and hidden when
    /// the map opens). <see cref="ExpectedChildCount"/> is a secondary signature (~8 direct children).</summary>
    public static class AtlasPanel
    {
        public const int UiRootChildIndex  = 17; // ✓ DevTree diff 2026-06-13 (was 22 pre-patch)
        public const int ExpectedChildCount = 8;  // ✓ map panel direct-child count (was 18 @ index 22)
    }

    /// <summary>World hover tracker (community, 2026-06-07): <c>*(UiRoot+0x7D8)+0x630</c>; hovered entity
    /// at +0x18. Singletons share vtable (image+0x2D707D8). The capture anchor for "what am I pointing at".</summary>
    public static class HoverTracker
    {
        public const int FromUiRoot   = 0x7D8; // *(UiRoot + 0x7D8) → tracker container
        public const int WorldTracker = 0x630; // + 0x630 → world hover tracker
        public const int HoveredEntity = 0x18; // + 0x18 → hovered entity/element
    }
}
