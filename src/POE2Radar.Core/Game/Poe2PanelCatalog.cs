namespace POE2Radar.Core.Game;

/// <summary>Immutable panel open/signature state for UI features.</summary>
public readonly record struct RitualPanelState(
    bool Open,
    bool SignatureDetected,
    int InBoundsTiles,
    nint Branch,
    long ItemSignature,
    Poe2RitualShop.IdleProbeKind ProbeKind,
    bool FastPathHit);

public readonly record struct RuneforgePanelState(bool Open, nint PanelRoot, int RewardCount);

public readonly record struct AtlasPanelState(bool Open);

public readonly record struct PanelCatalogSnapshot(
    bool Valid,
    RitualPanelState Ritual,
    RuneforgePanelState Runeforge,
    AtlasPanelState Atlas,
    long Generation)
{
    public static readonly PanelCatalogSnapshot Invalid = new(false, default, default, default, 0);
}

/// <summary>
/// Shared open-window tracker so Ritual, runeforge, atlas, and loot panels do not each solve detection alone.
/// </summary>
public sealed class Poe2PanelCatalog
{
    private readonly Poe2RitualShop _ritual;
    private readonly Poe2Runeforge _runeforge;
    private readonly Poe2Atlas _atlas;
    private long _generation;

    public Poe2PanelCatalog(Poe2RitualShop ritual, Poe2Runeforge runeforge, Poe2Atlas atlas)
    {
        _ritual = ritual;
        _runeforge = runeforge;
        _atlas = atlas;
    }

    public Poe2RitualShop RitualShop => _ritual;
    public Poe2Runeforge Runeforge => _runeforge;

    public PanelCatalogSnapshot Capture(
        GameContextSnapshot game,
        UiContextSnapshot ui,
        bool ritualAllowLocate,
        bool preferController)
    {
        if (!game.Valid)
            return PanelCatalogSnapshot.Invalid;

        _generation++;
        var winW = ui.Valid ? ui.WindowWidth : 1920f;
        var winH = ui.Valid ? ui.WindowHeight : 1080f;

        var ritualState = _ritual.ReadWindowState(game.InGameState, winW, winH, ritualAllowLocate, preferController);
        var ritual = new RitualPanelState(
            ritualState.PanelOpen,
            ritualState.SignatureDetected,
            ritualState.InBoundsTiles,
            ritualState.Branch,
            ritualState.ItemSignature,
            ritualState.ProbeKind,
            ritualState.FastPathHit);

        var runeWindow = _runeforge.ReadWindowState(game.InGameState, ui, winW, winH);
        var runeforge = new RuneforgePanelState(runeWindow.Open, runeWindow.PanelRoot, runeWindow.RewardCount);

        var atlas = new AtlasPanelState(_atlas.IsAtlasOpen(game.InGameState));

        return new PanelCatalogSnapshot(true, ritual, runeforge, atlas, _generation);
    }

    public void ResetRitualSession() => _ritual.ResetSession();
    public void ResetRuneforgeSession() => _runeforge.ResetSession();
}
