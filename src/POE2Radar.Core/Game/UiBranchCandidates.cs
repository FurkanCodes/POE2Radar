namespace POE2Radar.Core.Game;

/// <summary>
/// Orders GameUi / GameUiController probe candidates. Hints affect order only — every caller
/// should still try all candidates before reporting failure.
/// </summary>
public static class UiBranchCandidates
{
    /// <summary>
    /// Fill <paramref name="dest"/> with unique UI roots: last-success (if known), hinted primary,
    /// alternate branch, then generic fallbacks. Default hint is KBM-first.
    /// </summary>
    public static int Fill(
        Span<nint> dest,
        nint gameUi,
        nint controllerGameUi,
        nint uiRoot,
        nint fixedRoot,
        Poe2UiAnchors.BranchKind hint = Poe2UiAnchors.BranchKind.None,
        nint lastSuccessRoot = 0)
    {
        var count = 0;
        if (lastSuccessRoot != 0)
            Add(dest, ref count, lastSuccessRoot);

        if (hint == Poe2UiAnchors.BranchKind.Controller)
        {
            Add(dest, ref count, controllerGameUi);
            Add(dest, ref count, gameUi);
        }
        else
        {
            Add(dest, ref count, gameUi);
            Add(dest, ref count, controllerGameUi);
        }

        Add(dest, ref count, uiRoot);
        Add(dest, ref count, fixedRoot);
        return count;
    }

    public static Poe2UiAnchors.BranchKind BranchForRoot(nint root, nint gameUi, nint controllerGameUi)
    {
        if (root == 0) return Poe2UiAnchors.BranchKind.None;
        if (root == controllerGameUi) return Poe2UiAnchors.BranchKind.Controller;
        if (root == gameUi) return Poe2UiAnchors.BranchKind.KeyboardMouse;
        return Poe2UiAnchors.BranchKind.None;
    }

    public static bool PreferControllerOrder(Poe2UiAnchors.BranchKind hint)
        => hint == Poe2UiAnchors.BranchKind.Controller;

    private static void Add(Span<nint> dest, ref int count, nint root)
    {
        if (root == 0) return;
        for (var i = 0; i < count; i++)
            if (dest[i] == root) return;
        if (count < dest.Length)
            dest[count++] = root;
    }
}
