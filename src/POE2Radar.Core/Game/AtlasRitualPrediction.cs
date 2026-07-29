using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Atlas ritual-line TinyMT prediction ported from GameHelper Atlas2 / yokkenUA.
/// Pure math plus the embedded <c>atlas2_ritualmods.json</c> reservoir.
/// </summary>
public static class AtlasRitualPrediction
{
    public readonly record struct RitualMod(int Row, int Weight, int Cond, int Stat, string Text);

    public const int StatAdditionalMaps = 0x670B;
    public const int StatSecondModChance = 0x670C;
    public const int BaseLineLength = 5;
    private const uint SecondModCoinSalt = 0x91DA3AD9;

    private static readonly Lazy<IReadOnlyList<RitualMod>> Pool = new(LoadPool);

    public static IReadOnlyList<RitualMod> Mods => Pool.Value;

    /// <summary>
    /// The Atlas panel uses zero for normal navigation and a nonzero page state while the Rite line
    /// selector is active. The concrete nonzero value is not stable across client/plugin revisions.
    /// </summary>
    public static bool IsLineModeActive(byte mode) => mode != 0;

    /// <summary>TinyMT32 as used by PoE2's ritual line, bit-exact with Atlas2's reversed client model.</summary>
    public static class TinyMt32
    {
        private const uint Mat1 = 0x8F7011EE;
        private const uint Mat2 = 0xFC78FF1F;
        private const uint Tmat = 0x3793FDFF;

        public sealed class State
        {
            internal readonly uint[] Words;

            internal State(uint[] words) => Words = words;
        }

        /// <summary>Create the post-seed, post-eight-step state used by the game's next random draw.</summary>
        public static State Seed(uint w0, uint w1, uint w2, uint w3)
        {
            uint[] s = [0x40336052u, 0xCFA3723Cu, 0x3CAC5F71u, 0x3793FDFFu];
            uint[] words = [w0, w1, w2, w3];
            var r = 1;

            for (var i = 0; i < 4; i++)
            {
                var a = (r + 1) & 3;
                var b = r & 3;
                var c = (r + 3) & 3;
                var x = s[a] ^ s[c] ^ s[b];
                var h = ((x >> 27) ^ x) * 0x19660Du;
                s[a] += h;
                var h2 = h + words[i] + (uint)r;
                s[(r + 2) & 3] += h2;
                s[b] = h2;
                r = a;
            }

            for (var k = 0; k < 3; k++)
            {
                var a = (r + 1) & 3;
                var b = r & 3;
                var c = (r + 3) & 3;
                var x = s[a] ^ s[c] ^ s[b];
                var h = ((x >> 27) ^ x) * 0x19660Du;
                var h2 = h + (uint)r;
                s[a] += h;
                s[(r + 2) & 3] += h2;
                s[b] = h2;
                r = a;
            }

            for (var k = 0; k < 4; k++)
            {
                var a = (r + 1) & 3;
                var b = r & 3;
                var c = (r + 3) & 3;
                var x = s[c] + s[a] + s[b];
                x = ((x >> 27) ^ x) * 0x5D588B65u;
                var y = x - (uint)r;
                s[a] ^= x;
                s[(r + 2) & 3] ^= y;
                s[b] = y;
                r = a;
            }

            for (var k = 0; k < 8; k++)
                NextState(s);
            return new State(s);
        }

        /// <summary>Seed and return the first value the game would draw.</summary>
        public static uint SeedAndJump(uint w0, uint w1, uint w2, uint w3)
            => Draw(Seed(w0, w1, w2, w3));

        public static uint Draw(State state)
        {
            var s = state.Words;
            var oldS1 = s[1];
            var oldS2 = s[2];
            var x = (s[0] & 0x7FFFFFFFu) ^ s[1] ^ s[2];
            var t = s[3] ^ (s[3] << 1);
            x = (x >> 1) ^ x ^ t;
            var mag = (x & 1) != 0 ? uint.MaxValue : 0u;
            var newS2 = (mag & Mat2) ^ (x << 10) ^ t;
            s[0] = oldS1;
            s[1] = (mag & Mat1) ^ oldS2;
            s[2] = newS2;
            s[3] = x;
            var v = (newS2 >> 8) + oldS1;
            var temperMask = (v & 1) != 0 ? uint.MaxValue : 0u;
            return (temperMask & Tmat) ^ v ^ x;
        }

        public static uint RandBelow(State state, uint n)
        {
            if (n <= 1) return 0;
            const uint max = uint.MaxValue;
            while (true)
            {
                var r = Draw(state);
                if (max / n <= r / n && max % n != n - 1)
                    continue;
                return r % n;
            }
        }

        private static void NextState(uint[] s)
        {
            var x = (s[0] & 0x7FFFFFFFu) ^ s[1] ^ s[2];
            var t = s[3] ^ (s[3] << 1);
            x = (x >> 1) ^ x ^ t;
            var mag = (x & 1) != 0 ? uint.MaxValue : 0u;
            var oldS1 = s[1];
            var oldS2 = s[2];
            s[0] = oldS1;
            s[1] = (mag & Mat1) ^ oldS2;
            s[2] = (mag & Mat2) ^ (x << 10) ^ t;
            s[3] = x;
        }
    }

    /// <summary>
    /// Keep only rows enabled by the live Atlas stat set. Atlas2 accepts both the TSV id and the
    /// binary's zero-based id, so both <c>Cond</c> and <c>Cond - 1</c> are checked.
    /// </summary>
    public static IReadOnlyList<RitualMod> FilterPool(IReadOnlyDictionary<int, int>? stats)
    {
        if (stats is null || stats.Count == 0)
            return Mods.Where(m => m.Weight > 0 && m.Cond == 0).ToArray();
        return Mods.Where(m => m.Weight > 0
            && (m.Cond == 0 || stats.ContainsKey(m.Cond) || stats.ContainsKey(m.Cond - 1)))
            .ToArray();
    }

    public static int StatValue(IReadOnlyDictionary<int, int>? stats, int id)
    {
        if (stats is null) return 0;
        if (stats.TryGetValue(id, out var direct)) return direct;
        return stats.TryGetValue(id + 1, out var tsv) ? tsv : 0;
    }

    /// <summary>Compatibility overload; callers with live memory should pass the filtered pool and chance.</summary>
    public static (string First, string Second) PredictMods(uint lineId, uint committedCount, uint candIdx)
        => PredictMods(lineId, committedCount, candIdx, FilterPool(null), 0);

    public static (string First, string Second) PredictMods(
        uint lineId,
        uint committedCount,
        uint candIdx,
        IReadOnlyList<RitualMod> pool,
        int secondChance)
    {
        if (pool.Count == 0) return ("", "");
        var first = PredictModPass(lineId, committedCount, candIdx, 0, pool, 0);
        if (string.IsNullOrEmpty(first.Text))
            return ("", "");
        if (!PredictSecondModFlip(lineId, committedCount, candIdx, secondChance))
            return (first.Text, "");
        var second = PredictModPass(lineId, committedCount, candIdx, 1, pool, first.Stat);
        return (first.Text, second.Text ?? "");
    }

    private static bool PredictSecondModFlip(uint lineId, uint committedCount, uint candIdx, int chance)
    {
        if (chance <= 0) return false;
        if (chance >= 100) return true;
        var state = TinyMt32.Seed(lineId, committedCount, candIdx, SecondModCoinSalt);
        return TinyMt32.RandBelow(state, 100) < (uint)chance;
    }

    /// <summary>
    /// One weighted reservoir pass. The RNG advances once per eligible row; replacing this with a
    /// single cumulative roll changes every result even when the weights are identical.
    /// </summary>
    private static RitualMod PredictModPass(
        uint lineId,
        uint committedCount,
        uint candIdx,
        uint modCount,
        IReadOnlyList<RitualMod> pool,
        int grantedStat)
    {
        var state = TinyMt32.Seed(lineId, committedCount, candIdx, modCount);
        long total = 0;
        RitualMod selected = default;
        foreach (var row in pool)
        {
            if (row.Weight <= 0 || (grantedStat != 0 && row.Stat == grantedStat))
                continue;
            total += row.Weight;
            if (total > uint.MaxValue)
                throw new InvalidOperationException("Ritual reservoir weight exceeds UInt32 range.");
            if (TinyMt32.RandBelow(state, (uint)total) < (uint)row.Weight)
                selected = row;
        }
        return selected;
    }

    private static IReadOnlyList<RitualMod> LoadPool()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n =>
                n.Contains("atlas2_ritualmods", StringComparison.OrdinalIgnoreCase));
            if (name is null) return Array.Empty<RitualMod>();
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return Array.Empty<RitualMod>();
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("rows", out var rows)) return Array.Empty<RitualMod>();
            var list = new List<RitualMod>();
            foreach (var row in rows.EnumerateArray())
            {
                list.Add(new RitualMod(
                    row.TryGetProperty("row", out var index) ? index.GetInt32() : list.Count,
                    row.TryGetProperty("w", out var weight) ? weight.GetInt32() : 0,
                    row.TryGetProperty("cond", out var condition) ? condition.GetInt32() : 0,
                    row.TryGetProperty("stat", out var stat) ? stat.GetInt32() : 0,
                    row.TryGetProperty("text", out var text) ? text.GetString() ?? "" : ""));
            }
            return list;
        }
        catch
        {
            return Array.Empty<RitualMod>();
        }
    }
}
