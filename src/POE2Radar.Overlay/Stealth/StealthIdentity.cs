using System.Security.Cryptography;
using System.Text;

namespace POE2Radar.Overlay.Stealth;

/// <summary>Session-scoped random Win32 window title (CTO registers class name = title).</summary>
internal static class StealthIdentity
{
    private static readonly Lazy<string> WindowTitleLazy = new(() => GenerateWindowTitle());

    /// <summary>Random alphanumeric title used for both Win32 class and window name.</summary>
    public static string WindowTitle => WindowTitleLazy.Value;

    public static string GenerateWindowTitle(int length = 12)
    {
        length = Math.Clamp(length, 8, 24);
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Hardlink basename: 12 lowercase hex chars + .exe.</summary>
    public static string GenerateHardlinkFileName()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(16);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        sb.Append(".exe");
        return sb.ToString();
    }

    /// <summary>True when <paramref name="fileName"/> matches the hardlink naming pattern.</summary>
    public static bool IsHardlinkFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = fileName[..^4];
        if (stem.Length != 12) return false;
        foreach (var c in stem)
        {
            var ok = (c is >= '0' and <= '9') || (c is >= 'a' and <= 'f') || (c is >= 'A' and <= 'F');
            if (!ok) return false;
        }
        return true;
    }
}
