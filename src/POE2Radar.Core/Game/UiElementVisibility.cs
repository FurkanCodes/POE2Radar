namespace POE2Radar.Core.Game;

internal static class UiElementVisibility
{
    private const int MaxParentDepth = 64;

    public static bool IsHierarchicallyVisible(MemoryReader reader, nint element)
    {
        var current = element;
        for (var depth = 0; current != 0 && depth < MaxParentDepth; depth++)
        {
            if (!reader.TryReadStruct<uint>(
                    current + Poe2.UiElement.Flags,
                    out var flags) ||
                ((flags >> Poe2.UiElement.FlagVisibleBit) & 1) == 0)
                return false;

            if (!reader.TryReadStruct<nint>(
                    current + Poe2.UiElement.Parent,
                    out var parent))
                return false;
            if (parent == 0 || parent == current)
                return true;
            current = parent;
        }

        return false;
    }
}
