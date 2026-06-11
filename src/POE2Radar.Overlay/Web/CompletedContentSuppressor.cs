using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Web;

/// <summary>Per-instance, per-type-token hide for completed league mechanics and opened chests.
/// Tokens reappear after TTL expiry or when <see cref="OnAreaHashChanged"/> clears the instance bucket.</summary>
public sealed class CompletedContentSuppressor
{
    private readonly object _gate = new();
    private uint _areaHash;
    private Dictionary<string, DateTime> _byToken = new(StringComparer.Ordinal);

    public void OnAreaHashChanged(uint areaHash)
    {
        lock (_gate)
        {
            if (_areaHash == areaHash) return;
            _areaHash = areaHash;
            _byToken.Clear();
        }
    }

    /// <summary>Record newly completed type tokens from the live entity set.</summary>
    public void Observe(uint areaHash, IReadOnlyList<Poe2Live.EntityDot> entities, bool enabled, int suppressMinutes)
    {
        if (!enabled || entities.Count == 0) return;
        lock (_gate)
        {
            if (_areaHash != areaHash)
            {
                _areaHash = areaHash;
                _byToken.Clear();
            }

            PruneExpired(suppressMinutes);
            foreach (var e in entities)
            {
                if (!IsCompleted(e)) continue;
                var token = EntityDisplayHelper.TypeToken(e.Metadata);
                if (token.Length == 0) continue;
                _byToken.TryAdd(token, DateTime.UtcNow);
            }
        }
    }

    public bool IsSuppressed(uint areaHash, string token, bool enabled, int suppressMinutes)
    {
        if (!enabled || string.IsNullOrEmpty(token)) return false;
        lock (_gate)
        {
            if (_areaHash != areaHash) return false;
            if (!_byToken.TryGetValue(token, out var at)) return false;
            if (DateTime.UtcNow >= at.AddMinutes(suppressMinutes))
            {
                _byToken.Remove(token);
                return false;
            }
            return true;
        }
    }

    private void PruneExpired(int minutes)
    {
        var now = DateTime.UtcNow;
        foreach (var key in _byToken.Keys.ToList())
            if (now >= _byToken[key].AddMinutes(minutes))
                _byToken.Remove(key);
    }

    private static bool IsCompleted(Poe2Live.EntityDot e)
        => e.IconComplete || (e.Category == Poe2Live.EntityCategory.Chest && e.Opened);
}
