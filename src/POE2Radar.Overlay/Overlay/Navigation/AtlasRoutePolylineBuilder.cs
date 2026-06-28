using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Navigation;

internal static class AtlasRoutePolylineBuilder
{
    public static IReadOnlyList<AtlasRouteLine> BuildSegments(
        IReadOnlyList<(int X, int Y)>? path,
        IReadOnlyDictionary<(int X, int Y), NumVec2> centers,
        string label,
        string color,
        int hops,
        float thickness = 1f,
        int phaseIndex = 0)
    {
        if (path is null || path.Count < 2) return Array.Empty<AtlasRouteLine>();

        var segments = new List<AtlasRouteLine>();
        var current = new List<NumVec2>();
        for (var i = 0; i < path.Count; i++)
        {
            if (!centers.TryGetValue(path[i], out var point) || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                Flush(labelOnSegment: false);
                continue;
            }

            current.Add(point);
        }

        Flush(labelOnSegment: true);
        return segments;

        void Flush(bool labelOnSegment)
        {
            if (current.Count >= 2)
                segments.Add(new AtlasRouteLine(current.ToArray(), labelOnSegment ? label : "", color,
                    labelOnSegment ? hops : 0, thickness, phaseIndex));
            current.Clear();
        }
    }
}
