namespace POE2Radar.Core.Game;

/// <summary>
/// Cached UI anchors for one tick — GameUi, controller branch, UiRoot, and probe order.
/// </summary>
public readonly struct UiContextSnapshot
{
    public static readonly UiContextSnapshot Invalid = default;

    public bool Valid { get; init; }
    public nint InGameState { get; init; }
    public nint UiRootStruct { get; init; }
    public nint GameUi { get; init; }
    public nint GameUiController { get; init; }
    public nint FixedUiRoot { get; init; }
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    /// <summary>True when probe order puts GameUiController before GameUi (hint only).</summary>
    public bool PreferController { get; init; }
    public Poe2UiAnchors.BranchKind ProbeHint { get; init; }
    public long Generation { get; init; }
    public nint Branch0 { get; init; }
    public nint Branch1 { get; init; }
    public nint Branch2 { get; init; }
    public nint Branch3 { get; init; }
    public int BranchCount { get; init; }

    private static readonly nint[] EmptyBranches = Array.Empty<nint>();

    public ReadOnlySpan<nint> Branches
    {
        get
        {
            if (BranchCount <= 0) return EmptyBranches;
            if (BranchCount == 1) return new[] { Branch0 };
            if (BranchCount == 2) return new[] { Branch0, Branch1 };
            if (BranchCount == 3) return new[] { Branch0, Branch1, Branch2 };
            return new[] { Branch0, Branch1, Branch2, Branch3 };
        }
    }

    public static UiContextSnapshot Capture(
        MemoryReader reader,
        Poe2Live live,
        GameContextSnapshot game,
        int windowWidth,
        int windowHeight,
        Poe2UiAnchors.BranchKind probeHint = Poe2UiAnchors.BranchKind.None,
        nint lastSuccessRoot = 0,
        bool allowAnchorScan = false)
    {
        if (!game.Valid || game.InGameState == 0)
            return Invalid;

        Poe2UiAnchors.TryDiscoverCached(reader, game.InGameState, allowAnchorScan,
            out var gameUi, out var controllerGameUi);

        var uiRootStruct = Ptr(reader, game.InGameState + Poe2.InGameState.UiRootStructPtr);
        var fixedRoot = Ptr(reader, game.InGameState + Poe2.InGameState.UiRoot);
        var branches = live.GetUiBranches(game.InGameState, probeHint, lastSuccessRoot);

        nint b0 = 0, b1 = 0, b2 = 0, b3 = 0;
        var n = branches.Length;
        if (n > 0) b0 = branches[0];
        if (n > 1) b1 = branches[1];
        if (n > 2) b2 = branches[2];
        if (n > 3) b3 = branches[3];

        return new UiContextSnapshot
        {
            Valid = true,
            InGameState = game.InGameState,
            UiRootStruct = uiRootStruct,
            GameUi = gameUi,
            GameUiController = controllerGameUi,
            FixedUiRoot = fixedRoot,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            PreferController = UiBranchCandidates.PreferControllerOrder(probeHint),
            ProbeHint = probeHint,
            Generation = game.Generation,
            Branch0 = b0,
            Branch1 = b1,
            Branch2 = b2,
            Branch3 = b3,
            BranchCount = n,
        };
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
