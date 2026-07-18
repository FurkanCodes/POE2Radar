using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public readonly record struct AmanamuAlert(
    uint Id,
    NumVec2 Grid,
    System.Numerics.Vector3 World,
    bool InsideCloud,
    float DistanceGrid);

public readonly record struct AmanamuView(
    bool Enabled,
    bool ShowWorldOverlay,
    bool ShowMapMarkers,
    bool DrawLabels,
    bool DrawOffscreenArrows,
    bool DrawCircle,
    float CircleRadius,
    float LabelYOffset,
    float ArrowEdgeMargin,
    string InsideCloudColor,
    string OutsideCloudColor,
    AmanamuAlert[] Alerts);
