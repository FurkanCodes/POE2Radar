using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class UiBranchCandidatesTests
{
    private const nint GameUi = (nint)0x1000;
    private const nint ControllerUi = (nint)0x2000;
    private const nint UiRoot = (nint)0x3000;
    private const nint FixedRoot = (nint)0x4000;

    [Fact]
    public void Fill_NoHint_DefaultsToKeyboardMouseFirst()
    {
        Span<nint> roots = stackalloc nint[6];
        var count = UiBranchCandidates.Fill(roots, GameUi, ControllerUi, UiRoot, FixedRoot);

        Assert.Equal(4, count);
        Assert.Equal(GameUi, roots[0]);
        Assert.Equal(ControllerUi, roots[1]);
        Assert.Equal(UiRoot, roots[2]);
        Assert.Equal(FixedRoot, roots[3]);
    }

    [Fact]
    public void Fill_ControllerHint_StillIncludesKeyboardMouse()
    {
        Span<nint> roots = stackalloc nint[6];
        var count = UiBranchCandidates.Fill(
            roots, GameUi, ControllerUi, UiRoot, FixedRoot, Poe2UiAnchors.BranchKind.Controller);

        Assert.Equal(4, count);
        Assert.Equal(ControllerUi, roots[0]);
        Assert.Equal(GameUi, roots[1]);
        Assert.True(ContainsRoot(roots, count, GameUi));
        Assert.True(ContainsRoot(roots, count, ControllerUi));
    }

    [Fact]
    public void Fill_LastSuccessRoot_IsProbedFirst()
    {
        Span<nint> roots = stackalloc nint[6];
        var count = UiBranchCandidates.Fill(
            roots, GameUi, ControllerUi, UiRoot, FixedRoot,
            Poe2UiAnchors.BranchKind.KeyboardMouse,
            lastSuccessRoot: ControllerUi);

        Assert.True(count >= 2);
        Assert.Equal(ControllerUi, roots[0]);
        Assert.Equal(GameUi, roots[1]);
    }

    [Fact]
    public void Fill_DeduplicatesWhenLastSuccessMatchesHintedBranch()
    {
        Span<nint> roots = stackalloc nint[6];
        var count = UiBranchCandidates.Fill(
            roots, GameUi, ControllerUi, UiRoot, FixedRoot,
            Poe2UiAnchors.BranchKind.KeyboardMouse,
            lastSuccessRoot: GameUi);

        Assert.Equal(4, count);
        Assert.Equal(GameUi, roots[0]);
        Assert.Equal(ControllerUi, roots[1]);
        Assert.Equal(UiRoot, roots[2]);
        Assert.Equal(FixedRoot, roots[3]);
    }

    [Fact]
    public void BranchForRoot_ClassifiesKnownAnchors()
    {
        Assert.Equal(Poe2UiAnchors.BranchKind.KeyboardMouse,
            UiBranchCandidates.BranchForRoot(GameUi, GameUi, ControllerUi));
        Assert.Equal(Poe2UiAnchors.BranchKind.Controller,
            UiBranchCandidates.BranchForRoot(ControllerUi, GameUi, ControllerUi));
        Assert.Equal(Poe2UiAnchors.BranchKind.None,
            UiBranchCandidates.BranchForRoot((nint)0x9999, GameUi, ControllerUi));
    }

    [Theory]
    [InlineData(Poe2UiAnchors.BranchKind.None, false)]
    [InlineData(Poe2UiAnchors.BranchKind.KeyboardMouse, false)]
    [InlineData(Poe2UiAnchors.BranchKind.Controller, true)]
    public void PreferControllerOrder_ReflectsHintOnly(Poe2UiAnchors.BranchKind hint, bool expected)
    {
        Assert.Equal(expected, UiBranchCandidates.PreferControllerOrder(hint));
    }

    private static bool ContainsRoot(Span<nint> roots, int count, nint root)
    {
        for (var i = 0; i < count; i++)
            if (roots[i] == root) return true;
        return false;
    }
}
