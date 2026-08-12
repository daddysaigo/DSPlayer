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

    /// <summary>
    /// App chrome skin (write bar, status, splitter, caption chrome). Independent of comments.
    /// <c>Classic</c> | <c>Grok</c>
    /// </summary>
    public string UiTheme { get; set; } = "Grok";

    /// <summary>
    /// Comment list layout only. <c>Classic</c> = flat rows | <c>Grok</c> = cards.
    /// Independent of <see cref="UiTheme"/>.
    /// </summary>
    public string CommentListTheme { get; set; } = "Grok";

    public string? PcrBrowserPath { get; set; }

    public int BbsIntervalSeconds { get; set; } = 7;
    public string BbsUserAgent { get; set; } = "Monazilla/1.00 (NewPCRPlayer/1.00)";
    public bool MessageNormalize { get; set; } = true;

    // レス情報行（番号・名前・日時）— meta; kept small so body dominates
    public string CommentHeaderFontFamily { get; set; } = "Meiryo UI";
    public double CommentHeaderFontSize { get; set; } = 10;
    /// <summary>Normal / Light / Medium / SemiBold / Bold</summary>
    public string CommentHeaderFontWeight { get; set; } = "Normal";
    /// <summary>#RRGGBB or #AARRGGBB</summary>
    public string CommentHeaderColor { get; set; } = "#888888";
    public bool ShowResNumber { get; set; } = true;
    public bool ShowResName { get; set; } = true;
    public bool ShowResDate { get; set; } = true;

    // 本文 — primary scan target
    public string CommentBodyFontFamily { get; set; } = "Meiryo UI";
    public double CommentBodyFontSize { get; set; } = 14;
    public string CommentBodyFontWeight { get; set; } = "Normal";
    public string CommentBodyColor { get; set; } = "#111111";

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
    /// <param name="includeNumber">False when the UI shows a separate res# badge (Grok theme).</param>
    public string FormatResHeader(BbsPost post, bool includeNumber = true)
    {
        var showNum = includeNumber && ShowResNumber;
        var parts = new List<string>();
        if (showNum)
            parts.Add(post.Number.ToString());
        if (ShowResName && !string.IsNullOrWhiteSpace(post.Name))
            parts.Add(post.Name);
        if (ShowResDate && !string.IsNullOrWhiteSpace(post.DateId))
            parts.Add(post.DateId);

        if (parts.Count == 0)
            return "";

        // Classic style when all three: "1 ：name：date"
        if (showNum && ShowResName && ShowResDate)
            return $"{post.Number} ：{post.Name}：{post.DateId}";

        // Name + date without number: "name：date" / "name  date"
        if (!showNum && ShowResName && ShowResDate &&
            !string.IsNullOrWhiteSpace(post.Name) && !string.IsNullOrWhiteSpace(post.DateId))
            return $"{post.Name}：{post.DateId}";

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

    /// <summary>JSON snapshot for settings dialog Cancel-revert.</summary>
    public static string SerializeSnapshot(AppSettings s)
    {
        try
        {
            s.Sanitize();
            return JsonSerializer.Serialize(s, JsonOptions);
        }
        catch
        {
            return "{}";
        }
    }

    /// <summary>Copy snapshot fields into <paramref name="target"/> (same instance kept by MainWindow).</summary>
    public static void RestoreSnapshot(AppSettings target, string json)
    {
        try
        {
            var src = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (src is null) return;
            src.Sanitize();
            CopyOver(src, target);
            target.Sanitize();
        }
        catch
        {
            // ignore
        }
    }

    private static void CopyOver(AppSettings from, AppSettings to)
    {
        to.HandleName = from.HandleName;
        to.Mail = from.Mail;
        to.CommentPanelWidth = from.CommentPanelWidth;
        to.CommentPanelVisible = from.CommentPanelVisible;
        to.UiTheme = from.UiTheme;
        to.CommentListTheme = from.CommentListTheme;
        to.PcrBrowserPath = from.PcrBrowserPath;
        to.BbsIntervalSeconds = from.BbsIntervalSeconds;
        to.BbsUserAgent = from.BbsUserAgent;
        to.MessageNormalize = from.MessageNormalize;
        to.CommentHeaderFontFamily = from.CommentHeaderFontFamily;
        to.CommentHeaderFontSize = from.CommentHeaderFontSize;
        to.CommentHeaderFontWeight = from.CommentHeaderFontWeight;
        to.CommentHeaderColor = from.CommentHeaderColor;
        to.ShowResNumber = from.ShowResNumber;
        to.ShowResName = from.ShowResName;
        to.ShowResDate = from.ShowResDate;
        to.CommentBodyFontFamily = from.CommentBodyFontFamily;
        to.CommentBodyFontSize = from.CommentBodyFontSize;
        to.CommentBodyFontWeight = from.CommentBodyFontWeight;
        to.CommentBodyColor = from.CommentBodyColor;
        to.CommentFontFamily = from.CommentFontFamily;
        to.CommentFontSize = from.CommentFontSize;
        to.WindowLeft = from.WindowLeft;
        to.WindowTop = from.WindowTop;
        to.WindowWidth = from.WindowWidth;
        to.WindowHeight = from.WindowHeight;
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
        CommentHeaderFontWeight = NormalizeFontWeight(CommentHeaderFontWeight, "Normal");
        CommentBodyFontWeight = NormalizeFontWeight(CommentBodyFontWeight, "Normal");
        CommentHeaderColor = NormalizeHexColor(CommentHeaderColor, "#888888");
        CommentBodyColor = NormalizeHexColor(CommentBodyColor, "#111111");
        // At least one header field on, otherwise nothing shows
        if (!ShowResNumber && !ShowResName && !ShowResDate)
            ShowResNumber = true;

        UiTheme = NormalizeThemeId(UiTheme, "Grok");
        CommentListTheme = NormalizeThemeId(CommentListTheme, "Grok");
    }

    private static string NormalizeThemeId(string? value, string fallback) =>
        (value ?? "").Trim().Equals("Classic", StringComparison.OrdinalIgnoreCase)
            ? "Classic"
            : (string.IsNullOrWhiteSpace(value) ? fallback : "Grok");

    public bool HasWindowPlacement =>
        WindowLeft is not null && WindowTop is not null &&
        WindowWidth is > 200 && WindowHeight is > 150;

    private static string NormalizeFontWeight(string? value, string fallback)
    {
        var v = (value ?? "").Trim();
        return v.ToLowerInvariant() switch
        {
            "thin" or "ultralight" or "extralight" or "light" => "Light",
            "normal" or "regular" => "Normal",
            "medium" => "Medium",
            "semibold" or "demibold" => "SemiBold",
            "bold" => "Bold",
            "extrabold" or "ultrabold" or "black" or "heavy" => "Bold",
            _ => fallback,
        };
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return fallback;
        if (v[0] != '#') v = "#" + v;
        // #RGB / #RRGGBB / #AARRGGBB
        if (v.Length is not (4 or 7 or 9))
            return fallback;
        for (var i = 1; i < v.Length; i++)
        {
            var c = v[i];
            var hex = (c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F');
            if (!hex) return fallback;
        }
        return v.ToUpperInvariant();
    }
}
