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

public readonly record struct AtlasPanelState(bool Open);

public readonly record struct PanelCatalogSnapshot(
    bool Valid,
    RitualPanelState Ritual,
    AtlasPanelState Atlas,
    long Generation)
{
    public static readonly PanelCatalogSnapshot Invalid = new(false, default, default, 0);
}

/// <summary>
/// Shared open-window tracker so Ritual, atlas, and loot panels do not each solve detection alone.
/// </summary>
public sealed class Poe2PanelCatalog
{
    private readonly Poe2RitualShop _ritual;
    private readonly Poe2Atlas _atlas;
    private long _generation;

    public Poe2PanelCatalog(Poe2RitualShop ritual, Poe2Atlas atlas)
    {
        _ritual = ritual;
        _atlas = atlas;
    }

    public Poe2RitualShop RitualShop => _ritual;

    public PanelCatalogSnapshot Capture(
        GameContextSnapshot game,
        UiContextSnapshot ui,
        bool ritualAllowLocate,
        Poe2UiAnchors.BranchKind probeHint)
    {
        if (!game.Valid)
            return PanelCatalogSnapshot.Invalid;

        _generation++;
        var winW = ui.Valid ? ui.WindowWidth : 1920f;
        var winH = ui.Valid ? ui.WindowHeight : 1080f;

        var ritualState = _ritual.ReadWindowState(game.InGameState, winW, winH, ritualAllowLocate, probeHint);
        var ritual = new RitualPanelState(
            ritualState.PanelOpen,
            ritualState.SignatureDetected,
            ritualState.InBoundsTiles,
            ritualState.Branch,
            ritualState.ItemSignature,
            ritualState.ProbeKind,
            ritualState.FastPathHit);

        var atlas = new AtlasPanelState(_atlas.IsAtlasOpen(game.InGameState));

        return new PanelCatalogSnapshot(true, ritual, atlas, _generation);
    }

    public void ResetRitualSession() => _ritual.ResetSession();
}
