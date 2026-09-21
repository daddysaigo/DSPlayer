using System.Diagnostics;
using System.IO;

namespace DSPlayer.Services;

/// <summary>
/// Launches the stock PCRBrowser.exe shipped with PCRPlayer.
/// Usage: PCRBrowser.exe "掲示板URL/スレッドURL"
/// </summary>
public static class PcrBrowserLauncher
{
    public static string? FindExecutable(string? configuredPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath);

        // Same folder as our exe (if user copies PCRBrowser next to DSPlayer)
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

        // PeCaRecorder is commonly installed under Program Files (x86)\Peercast,
        // but versioned folder names vary. Search only this small conventional root.
        try
        {
            var peercastRoot = Path.Combine(pf86, "Peercast");
            if (Directory.Exists(peercastRoot))
            {
                var found = Directory.EnumerateFiles(peercastRoot, "PCRBrowser.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found is not null)
                    return found;
            }
        }
        catch
        {
            // A denied or malformed installation tree simply means not found.
        }

        return null;
    }

    public static void Open(string boardOrThreadUrl, string? configuredPath = null)
    {
        if (string.IsNullOrWhiteSpace(boardOrThreadUrl))
            throw new ArgumentException("URL が空です。", nameof(boardOrThreadUrl));

        var exe = FindExecutable(configuredPath)
            ?? throw new FileNotFoundException(
                "PCRBrowser.exe が見つかりません。設定からBBSブラウザの実行ファイルを指定してください。");

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
