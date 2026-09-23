using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DSPlayer.Services;
using DSPlayer.Services.Bbs;
using ThemeUi = DSPlayer.Themes.UiTheme;
using ThemeComments = DSPlayer.Themes.CommentListTheme;

namespace DSPlayer.Models;

public sealed class AppSettings
{
    private JsonObject? _savedDocument;
    private string? _storagePath;
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
    /// App chrome skin (write bar, status, splitter, caption). Independent of comments.
    /// See <c>Themes.UiTheme.All</c>: Classic, Grok, Neon, Sakura, Terminal, Ember.
    /// </summary>
    public string UiTheme { get; set; } = "Grok";

    /// <summary>
    /// Comment list skin (layout + palette). Independent of <see cref="UiTheme"/>.
    /// See <c>Themes.CommentListTheme.All</c>: Classic, Grok, Neon, Sakura, Sticky, Bubble, Terminal.
    /// </summary>
    public string CommentListTheme { get; set; } = "Grok";

    /// <summary>Status-bar heat meter chrome. <c>Heat</c> (default) or <c>Simple</c> (original).</summary>
    public string MomentumStyle { get; set; } = "Heat";

    public string? PcrBrowserPath { get; set; }

    public int BbsIntervalSeconds { get; set; } = 7;
    public string BbsUserAgent { get; set; } = "Monazilla/1.00 (DSPlayer/1.00)";
    public bool MessageNormalize { get; set; } = true;
    /// <summary>Fetch jpg/png/gif URLs and show them inside the comment column.</summary>
    public bool EmbedCommentImages { get; set; } = true;

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

    public bool WindowSnapEnabled { get; set; } = true;
    public bool WindowSnapToWindows { get; set; } = true;
    public int WindowSnapDistance { get; set; } = 12;
    public bool AlwaysOnTop { get; set; }
    public bool SaveWindowPlacement { get; set; } = true;
    public bool SaveVolume { get; set; }
    public double SavedVolume { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public static string AppDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DSPlayer");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    /// <summary>Pre-rename app data folder (auto-migrated once).</summary>
    public static string LegacyAppDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewPCRPlayer");

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

    public static AppSettings Load() => Load(SettingsPath);

    internal static AppSettings Load(string path)
    {
        try
        {
            if (path == SettingsPath) MigrateLegacyAppDataIfNeeded();
            var s = AtomicJsonFile.Read(path).Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
            s.MigrateLegacyFont();
            s.Sanitize();
            s._storagePath = path;
            s._savedDocument = s.CommonDocument();
            return s;
        }
        catch
        {
            var fallback = new AppSettings { _storagePath = path };
            fallback._savedDocument = fallback.CommonDocument();
            return fallback;
        }
    }

    /// <summary>Copy %LOCALAPPDATA%\NewPCRPlayer → DSPlayer once if target is empty.</summary>
    private static void MigrateLegacyAppDataIfNeeded()
    {
        try
        {
            if (File.Exists(SettingsPath)) return;
            var legacySettings = Path.Combine(LegacyAppDataDir, "settings.json");
            if (!File.Exists(legacySettings)) return;

            Directory.CreateDirectory(AppDataDir);
            File.Copy(legacySettings, SettingsPath, overwrite: false);
            var legacyLog = Path.Combine(LegacyAppDataDir, "player.log");
            var newLog = Path.Combine(AppDataDir, "player.log");
            if (File.Exists(legacyLog) && !File.Exists(newLog))
                File.Copy(legacyLog, newLog, overwrite: false);
        }
        catch
        {
            // ignore migration failures
        }
    }

    public void Save()
    {
        Sanitize();
        var current = CommonDocument();
        var baseline = _savedDocument ?? new AppSettings().CommonDocument();
        AtomicJsonFile.Update(_storagePath ?? SettingsPath, latest => ApplyChanges(latest, baseline, current));
        _savedDocument = current;
    }

    /// <summary>Refresh untouched fields before opening settings; retain any local unsaved edits.</summary>
    public void RefreshFromDisk()
    {
        var latest = Load(_storagePath ?? SettingsPath);
        var merged = latest.CommonDocument();
        ApplyChanges(merged, _savedDocument ?? new AppSettings().CommonDocument(), CommonDocument());
        var settings = merged.Deserialize<AppSettings>(JsonOptions);
        if (settings is not null)
        {
            CopyOver(settings, this);
            _savedDocument = latest.CommonDocument();
        }
    }

    private JsonObject CommonDocument()
    {
        var document = JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
        return document;
    }

    private static void ApplyChanges(JsonObject latest, JsonObject baseline, JsonObject current)
    {
        foreach (var field in current)
        {
            if (!JsonNode.DeepEquals(field.Value, baseline[field.Key]))
                latest[field.Key] = field.Value?.DeepClone();
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
        to.MomentumStyle = from.MomentumStyle;
        to.PcrBrowserPath = from.PcrBrowserPath;
        to.BbsIntervalSeconds = from.BbsIntervalSeconds;
        to.BbsUserAgent = from.BbsUserAgent;
        to.MessageNormalize = from.MessageNormalize;
        to.EmbedCommentImages = from.EmbedCommentImages;
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
        to.WindowSnapEnabled = from.WindowSnapEnabled;
        to.WindowSnapToWindows = from.WindowSnapToWindows;
        to.WindowSnapDistance = from.WindowSnapDistance;
        to.AlwaysOnTop = from.AlwaysOnTop;
        to.SaveWindowPlacement = from.SaveWindowPlacement;
        to.SaveVolume = from.SaveVolume;
        to.SavedVolume = from.SavedVolume;
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
        if (BbsIntervalSeconds < 5) BbsIntervalSeconds = 5;
        if (BbsIntervalSeconds > 120) BbsIntervalSeconds = 120;
        if (string.IsNullOrWhiteSpace(BbsUserAgent))
            BbsUserAgent = "Monazilla/1.00 (DSPlayer/1.00)";
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

        UiTheme = ThemeUi.NormalizeId(UiTheme);
        CommentListTheme = ThemeComments.NormalizeId(CommentListTheme);
        MomentumStyle = ThreadMomentum.NormalizeStyle(MomentumStyle);
        WindowSnapDistance = WindowSnapDistance switch { <= 8 => 8, >= 18 => 18, _ => 12 };
        SavedVolume = Math.Clamp(SavedVolume, 0, 100);
    }

    [JsonIgnore]
    public bool HasWindowPlacement =>
        WindowLeft is double left && double.IsFinite(left) &&
        WindowTop is double top && double.IsFinite(top) &&
        WindowWidth is double width && double.IsFinite(width) && width > 200 &&
        WindowHeight is double height && double.IsFinite(height) && height > 150;

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
