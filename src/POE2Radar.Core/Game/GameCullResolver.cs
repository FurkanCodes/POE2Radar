namespace POE2Radar.Core.Game;

internal sealed class GameCullResolver
{
    private readonly MemoryReader _reader;
    private nint _address;
    private bool _resolved;

    public GameCullResolver(MemoryReader reader)
    {
        _reader = reader;
    }

    public float Read(float windowWidth, float windowHeight)
    {
        if (!_resolved)
            Resolve();

        if (_address != 0 &&
            _reader.TryReadStruct<int>(_address, out var value) &&
            value >= 0 &&
            value <= windowWidth * 0.5f)
            return value;

        return UiElementProjection.HorizontalCull(windowWidth, windowHeight);
    }

    private void Resolve()
    {
        _resolved = true;
        foreach (var address in AobScanner.ScanForResolvedAddresses(
                     _reader.Process,
                     _reader,
                     AobPatterns.GameCullSize))
        {
            if (!_reader.TryReadStruct<int>(address, out var value) ||
                value < 0 ||
                value > 16_384)
                continue;
            _address = address;
            return;
        }
    }
}
