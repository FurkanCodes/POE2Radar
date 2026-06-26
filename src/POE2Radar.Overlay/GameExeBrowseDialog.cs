using System.Runtime.InteropServices;
using System.Text;

namespace POE2Radar.Overlay;

/// <summary>Win32 GetOpenFileName wrapper — no WinForms dependency.</summary>
internal static class GameExeBrowseDialog
{
    private const int MaxPath = 260;

    internal static string? PickExistingExe(string? initialDirectory = null)
    {
        var file = new StringBuilder(MaxPath);
        var filter = BuildFilter(
            ("PoE2 client", "PathOfExileSteam.exe;PathOfExile.exe;PathOfExileEGS.exe;PathOfExile_x64.exe;PathOfExile_KG.exe"),
            ("Executables", "*.exe"),
            ("All files", "*.*"));

        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            lpstrFilter = filter,
            lpstrFile = file,
            nMaxFile = MaxPath,
            lpstrTitle = "Select Path of Exile 2 executable",
            Flags = OfnFlags.OFN_FILEMUSTEXIST | OfnFlags.OFN_PATHMUSTEXIST | OfnFlags.OFN_NOCHANGEDIR,
            lpstrInitialDir = string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory,
        };

        return GetOpenFileName(ref ofn) ? file.ToString() : null;
    }

    private static string BuildFilter(params (string Label, string Pattern)[] items)
    {
        var sb = new StringBuilder();
        foreach (var (label, pattern) in items)
        {
            sb.Append(label).Append('\0').Append(pattern).Append('\0');
        }
        return sb.ToString();
    }

    [Flags]
    private enum OfnFlags : uint
    {
        OFN_FILEMUSTEXIST = 0x00001000,
        OFN_PATHMUSTEXIST = 0x00000800,
        OFN_NOCHANGEDIR = 0x00000008,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public OfnFlags Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);
}
