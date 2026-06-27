namespace POE2Radar.Core.Game;

/// <summary>Immutable panel open/signature state for UI features (Ritual first).</summary>
public readonly record struct RitualPanelState(
    bool Open,
    bool SignatureDetected,
    int InBoundsTiles,
    nint Branch,
    long ItemSignature,
    Poe2RitualShop.IdleProbeKind ProbeKind,
    bool FastPathHit);

public readonly record struct PanelCatalogSnapshot(
    bool Valid,
    RitualPanelState Ritual,
    long Generation)
{
    public static readonly PanelCatalogSnapshot Invalid = new(false, default, 0);
}

/// <summary>
/// Shared open-window tracker so Ritual, runeforge, and loot panels do not each solve detection alone.
/// </summary>
public sealed class Poe2PanelCatalog
{
    private readonly Poe2RitualShop _ritual;
    private long _generation;

    public Poe2PanelCatalog(Poe2RitualShop ritual) => _ritual = ritual;

    public Poe2RitualShop RitualShop => _ritual;

    public PanelCatalogSnapshot CaptureRitualWindow(
        GameContextSnapshot game,
        UiContextSnapshot ui,
        bool allowLocate,
        bool preferController)
    {
        if (!game.Valid)
            return PanelCatalogSnapshot.Invalid;

        _generation++;
        var winW = ui.Valid ? ui.WindowWidth : 1920f;
        var winH = ui.Valid ? ui.WindowHeight : 1080f;
        var state = _ritual.ReadWindowState(game.InGameState, winW, winH, allowLocate, preferController);
        var ritual = new RitualPanelState(
            state.PanelOpen,
            state.SignatureDetected,
            state.InBoundsTiles,
            state.Branch,
            state.ItemSignature,
            state.ProbeKind,
            state.FastPathHit);
        return new PanelCatalogSnapshot(true, ritual, _generation);
    }

    public void ResetRitualSession() => _ritual.ResetSession();
}
