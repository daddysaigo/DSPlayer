using System.Diagnostics;
using System.IO;

namespace NewPCRPlayer.Services;

/// <summary>
/// Launches the stock PCRBrowser.exe shipped with PCRPlayer.
/// Usage: PCRBrowser.exe "掲示板URL/スレッドURL"
/// </summary>
public static class PcrBrowserLauncher
{
    public static readonly string DefaultPath =
        @"C:\Program Files (x86)\Peercast\PeCaRecorder_v091.7_20210914\PeCaRecorder_v091\PCRPlayer\PCRBrowser.exe";

    public static string? FindExecutable(string? configuredPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath);

        candidates.Add(DefaultPath);

        // Same folder as our exe (if user copies PCRBrowser next to NewPCRPlayer)
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "PCRBrowser.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "PCRPlayer", "PCRBrowser.exe"));

        // Sibling of PeCaRecorder common installs
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf86, "Peercast", "PCRPlayer", "PCRBrowser.exe"));

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }

    public static void Open(string boardOrThreadUrl, string? configuredPath = null)
    {
        if (string.IsNullOrWhiteSpace(boardOrThreadUrl))
            throw new ArgumentException("URL が空です。", nameof(boardOrThreadUrl));

        var exe = FindExecutable(configuredPath)
            ?? throw new FileNotFoundException(
                "PCRBrowser.exe が見つかりません。PCRPlayer 同梱のパスを確認してください。\n" + DefaultPath);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "\"" + boardOrThreadUrl.Trim() + "\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
        };
        Process.Start(psi);
    }
}
