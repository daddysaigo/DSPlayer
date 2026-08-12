using System.IO;
using System.Text;
using System.Text.Json;
using NewPCRPlayer.Services.Bbs;

namespace NewPCRPlayer.Models;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string HandleName { get; set; } = "";
    public string Mail { get; set; } = "sage";

    public double CommentPanelWidth { get; set; } = 340;
    public bool CommentPanelVisible { get; set; } = true;

    public string? PcrBrowserPath { get; set; }

    public int BbsIntervalSeconds { get; set; } = 7;
    public string BbsUserAgent { get; set; } = "Monazilla/1.00 (NewPCRPlayer/1.00)";
    public bool MessageNormalize { get; set; } = true;

    // レス情報行（番号・名前・日時）
    public string CommentHeaderFontFamily { get; set; } = "Meiryo UI";
    public double CommentHeaderFontSize { get; set; } = 11;
    public bool ShowResNumber { get; set; } = true;
    public bool ShowResName { get; set; } = true;
    public bool ShowResDate { get; set; } = true;

    // 本文
    public string CommentBodyFontFamily { get; set; } = "Meiryo UI";
    public double CommentBodyFontSize { get; set; } = 13;

    /// <summary>Legacy single font (migrated on load).</summary>
    public string? CommentFontFamily { get; set; }
    public double? CommentFontSize { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewPCRPlayer",
            "settings.json");

    /// <summary>Format header line from toggles (番号 / 名前 / 日時).</summary>
    public string FormatResHeader(BbsPost post)
    {
        var parts = new List<string>();
        if (ShowResNumber)
            parts.Add(post.Number.ToString());
        if (ShowResName && !string.IsNullOrWhiteSpace(post.Name))
            parts.Add(post.Name);
        if (ShowResDate && !string.IsNullOrWhiteSpace(post.DateId))
            parts.Add(post.DateId);

        if (parts.Count == 0)
            return "";

        // Classic style when all three: "1 ：name：date"
        if (ShowResNumber && ShowResName && ShowResDate)
            return $"{post.Number} ：{post.Name}：{post.DateId}";

        return string.Join("  ", parts);
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new AppSettings();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            s.MigrateLegacyFont();
            s.Sanitize();
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Sanitize();
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    private void MigrateLegacyFont()
    {
        if (!string.IsNullOrWhiteSpace(CommentFontFamily))
        {
            if (string.IsNullOrWhiteSpace(CommentHeaderFontFamily) || CommentHeaderFontFamily == "Meiryo UI")
                CommentHeaderFontFamily = CommentFontFamily;
            if (string.IsNullOrWhiteSpace(CommentBodyFontFamily) || CommentBodyFontFamily == "Meiryo UI")
                CommentBodyFontFamily = CommentFontFamily;
        }
        if (CommentFontSize is >= 8 and <= 36)
        {
            if (CommentBodyFontSize is 13)
                CommentBodyFontSize = CommentFontSize.Value;
            if (CommentHeaderFontSize is 11)
                CommentHeaderFontSize = Math.Max(8, CommentFontSize.Value - 2);
        }
    }

    public void Sanitize()
    {
        if (BbsIntervalSeconds < 3) BbsIntervalSeconds = 3;
        if (BbsIntervalSeconds > 120) BbsIntervalSeconds = 120;
        if (string.IsNullOrWhiteSpace(BbsUserAgent))
            BbsUserAgent = "Monazilla/1.00 (NewPCRPlayer/1.00)";
        if (CommentPanelWidth < 120) CommentPanelWidth = 120;
        if (CommentPanelWidth > 900) CommentPanelWidth = 900;
        if (string.IsNullOrWhiteSpace(CommentHeaderFontFamily))
            CommentHeaderFontFamily = "Meiryo UI";
        if (string.IsNullOrWhiteSpace(CommentBodyFontFamily))
            CommentBodyFontFamily = "Meiryo UI";
        CommentHeaderFontSize = Math.Clamp(CommentHeaderFontSize, 8, 36);
        CommentBodyFontSize = Math.Clamp(CommentBodyFontSize, 8, 36);
        // At least one header field on, otherwise nothing shows
        if (!ShowResNumber && !ShowResName && !ShowResDate)
            ShowResNumber = true;
    }

    public bool HasWindowPlacement =>
        WindowLeft is not null && WindowTop is not null &&
        WindowWidth is > 200 && WindowHeight is > 150;
}
