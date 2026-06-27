using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class UiBranchCandidatesTests
{
    [Fact]
    public void Fill_DefaultHint_TriesKeyboardThenController()
    {
        Span<nint> branches = stackalloc nint[4];

        var count = UiBranchCandidates.Fill(
            branches,
            gameUi: 0x1000,
            controllerGameUi: 0x2000,
            uiRoot: 0x3000,
            fixedRoot: 0x4000);

        Assert.Equal(4, count);
        Assert.Equal(0x1000, branches[0]);
        Assert.Equal(0x2000, branches[1]);
    }

    [Fact]
    public void Fill_ControllerHint_TriesControllerThenKeyboard()
    {
        Span<nint> branches = stackalloc nint[4];

        var count = UiBranchCandidates.Fill(
            branches,
            gameUi: 0x1000,
            controllerGameUi: 0x2000,
            uiRoot: 0x3000,
            fixedRoot: 0x4000,
            Poe2UiAnchors.BranchKind.Controller);

        Assert.Equal(4, count);
        Assert.Equal(0x2000, branches[0]);
        Assert.Equal(0x1000, branches[1]);
    }

    [Fact]
    public void Fill_LastSuccessRoot_WinsAndIsDeduped()
    {
        Span<nint> branches = stackalloc nint[4];

        var count = UiBranchCandidates.Fill(
            branches,
            gameUi: 0x1000,
            controllerGameUi: 0x2000,
            uiRoot: 0x3000,
            fixedRoot: 0x4000,
            Poe2UiAnchors.BranchKind.KeyboardMouse,
            lastSuccessRoot: 0x2000);

        Assert.Equal(4, count);
        Assert.Equal(0x2000, branches[0]);
        Assert.Equal(0x1000, branches[1]);
        Assert.Equal(0x3000, branches[2]);
        Assert.Equal(0x4000, branches[3]);
    }
}
