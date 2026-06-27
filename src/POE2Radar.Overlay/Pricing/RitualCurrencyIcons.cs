// Source-port of RitualHelper GPLv3 behavior; see RitualHelper.GPLv3.LICENSE.txt in this folder.
namespace POE2Radar.Overlay.Pricing;

internal static class RitualCurrencyIcons
{
    private static readonly (string FileName, string Url)[] DefaultIcons =
    [
        ("divine.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lNb2RWYWx1ZXMiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/2986e220b3/CurrencyModValues.png"),
        ("exalted.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/ad7c366789/CurrencyAddModToRare.png"),
        ("chaos.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lEdWxhdGUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/8b3e0a3f2c/ChaosOrb.png"),
    ];

    private static readonly HttpClient Http = CreateHttpClient();
    private static int _downloadStarted;
    private static string _baseDir = "";

    public static string TexturesDir => Path.Combine(_baseDir, "Textures");

    public static void Initialize(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(TexturesDir);
        if (Interlocked.Exchange(ref _downloadStarted, 1) == 0)
            _ = Task.Run(EnsureIconFilesAsync);
    }

    public static string PathFor(string fileName)
        => Path.Combine(TexturesDir, fileName);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "POE2Radar-RitualHelper-Port");
        return client;
    }

    private static async Task EnsureIconFilesAsync()
    {
        foreach (var (fileName, url) in DefaultIcons)
        {
            var path = PathFor(fileName);
            if (File.Exists(path)) continue;

            try
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch
            {
                // The label still draws as text; icons appear after the next successful download.
            }
        }
    }
}
