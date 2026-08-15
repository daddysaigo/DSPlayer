using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;

namespace DSPlayer.Themes;

/// <summary>
/// App chrome skin (write bar, status, splitter, caption). Independent of comment list theme.
/// Each entry is a distinct mood — not a mild recolor of the same dark gray.
/// </summary>
public sealed class UiTheme
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Rounded accent write button vs flat bordered Classic button.</summary>
    public bool UseAccentButton { get; init; }

    public required MediaColor WindowBg { get; init; }
    public required MediaColor WriteBarBg { get; init; }
    public required MediaColor WriteBarBorder { get; init; }
    public required MediaColor WriteBarMuted { get; init; }
    public required MediaColor InputBg { get; init; }
    public required MediaColor InputFg { get; init; }
    public required MediaColor InputBorder { get; init; }
    public required MediaColor ButtonBg { get; init; }
    public required MediaColor ButtonFg { get; init; }
    public required MediaColor ButtonBorder { get; init; }
    public required MediaColor StatusBg { get; init; }
    public required MediaColor StatusFg { get; init; }
    public required MediaColor StatusMuted { get; init; }
    public required MediaColor Splitter { get; init; }

    public required MediaColor CommentPanelBg { get; init; }
    public required MediaColor CommentPanelBorder { get; init; }

    public required byte ChromeBarR { get; init; }
    public required byte ChromeBarG { get; init; }
    public required byte ChromeBarB { get; init; }
    public required byte ChromeHoverR { get; init; }
    public required byte ChromeHoverG { get; init; }
    public required byte ChromeHoverB { get; init; }
    public required byte ChromePressR { get; init; }
    public required byte ChromePressG { get; init; }
    public required byte ChromePressB { get; init; }
    public required byte ChromeTextR { get; init; }
    public required byte ChromeTextG { get; init; }
    public required byte ChromeTextB { get; init; }

    public required MediaColor SettingsWindowBg { get; init; }
    public required MediaColor SettingsLabel { get; init; }
    public required MediaColor SettingsMuted { get; init; }
    public required MediaColor SettingsFieldBg { get; init; }
    public required MediaColor SettingsFieldFg { get; init; }

    /// <summary>PCR-like utilitarian dark gray.</summary>
    public static UiTheme Classic { get; } = new()
    {
        Id = "Classic",
        DisplayName = "Classic — PCRダーク",
        UseAccentButton = false,
        WindowBg = Rgb(0x1A, 0x1A, 0x1A),
        WriteBarBg = Rgb(0x1E, 0x1E, 0x1E),
        WriteBarBorder = Rgb(0x2A, 0x2A, 0x2A),
        WriteBarMuted = Rgb(0xAA, 0xAA, 0xAA),
        InputBg = Rgb(0x2A, 0x2A, 0x2A),
        InputFg = Rgb(0xE8, 0xE8, 0xE8),
        InputBorder = Rgb(0x44, 0x44, 0x44),
        ButtonBg = Rgb(0x3A, 0x3A, 0x3A),
        ButtonFg = Rgb(0xE8, 0xE8, 0xE8),
        ButtonBorder = Rgb(0x55, 0x55, 0x55),
        StatusBg = Rgb(0x25, 0x25, 0x25),
        StatusFg = Rgb(0xE8, 0xE8, 0xE8),
        StatusMuted = Rgb(0xCC, 0xCC, 0xCC),
        Splitter = Rgb(0x3A, 0x3A, 0x3A),
        CommentPanelBg = Rgb(0xF0, 0xF0, 0xF0),
        CommentPanelBorder = Rgb(0xD0, 0xD0, 0xD0),
        ChromeBarR = 0x1A, ChromeBarG = 0x1A, ChromeBarB = 0x1A,
        ChromeHoverR = 0x3A, ChromeHoverG = 0x3A, ChromeHoverB = 0x3A,
        ChromePressR = 0x4A, ChromePressG = 0x4A, ChromePressB = 0x4A,
        ChromeTextR = 0xE0, ChromeTextG = 0xE0, ChromeTextB = 0xE0,
        SettingsWindowBg = Rgb(0x1E, 0x1E, 0x1E),
        SettingsLabel = Rgb(0xCC, 0xCC, 0xCC),
        SettingsMuted = Rgb(0x88, 0x88, 0x88),
        SettingsFieldBg = Rgb(0x2A, 0x2A, 0x2A),
        SettingsFieldFg = Rgb(0xE8, 0xE8, 0xE8),
    };

    /// <summary>Warm charcoal + amber — signature Grok mood.</summary>
    public static UiTheme Grok { get; } = new()
    {
        Id = "Grok",
        DisplayName = "Grok — 暖色アンバー",
        UseAccentButton = true,
        WindowBg = Rgb(0x2C, 0x28, 0x24),
        WriteBarBg = Rgb(0x36, 0x31, 0x2C),
        WriteBarBorder = Rgb(0x4A, 0x43, 0x3C),
        WriteBarMuted = Rgb(0xB8, 0xAE, 0xA2),
        InputBg = Rgb(0x45, 0x3E, 0x36),
        InputFg = Rgb(0xF5, 0xF0, 0xE8),
        InputBorder = Rgb(0x6A, 0x60, 0x54),
        ButtonBg = Rgb(0xE0, 0xA8, 0x4A),
        ButtonFg = Rgb(0x2C, 0x24, 0x18),
        ButtonBorder = Rgb(0xC4, 0x90, 0x3A),
        StatusBg = Rgb(0x32, 0x2D, 0x28),
        StatusFg = Rgb(0xE8, 0xE2, 0xD8),
        StatusMuted = Rgb(0xB0, 0xA8, 0x9C),
        Splitter = Rgb(0x4A, 0x43, 0x3C),
        CommentPanelBg = Rgb(0xF3, 0xEF, 0xE8),
        CommentPanelBorder = Rgb(0xD8, 0xD2, 0xC8),
        ChromeBarR = 0x36, ChromeBarG = 0x31, ChromeBarB = 0x2C,
        ChromeHoverR = 0x52, ChromeHoverG = 0x4A, ChromeHoverB = 0x42,
        ChromePressR = 0x62, ChromePressG = 0x58, ChromePressB = 0x4E,
        ChromeTextR = 0xF0, ChromeTextG = 0xEA, ChromeTextB = 0xE2,
        SettingsWindowBg = Rgb(0x2C, 0x28, 0x24),
        SettingsLabel = Rgb(0xE0, 0xD8, 0xCC),
        SettingsMuted = Rgb(0xA0, 0x96, 0x8A),
        SettingsFieldBg = Rgb(0x45, 0x3E, 0x36),
        SettingsFieldFg = Rgb(0xF5, 0xF0, 0xE8),
    };

    /// <summary>Cyber neon: void black, hot pink button, cyan chrome edges.</summary>
    public static UiTheme Neon { get; } = new()
    {
        Id = "Neon",
        DisplayName = "Neon — サイバー",
        UseAccentButton = true,
        WindowBg = Rgb(0x08, 0x06, 0x12),
        WriteBarBg = Rgb(0x12, 0x0C, 0x1E),
        WriteBarBorder = Rgb(0x7C, 0x3A, 0xED),
        WriteBarMuted = Rgb(0xC4, 0xB5, 0xFD),
        InputBg = Rgb(0x1A, 0x10, 0x2E),
        InputFg = Rgb(0xF5, 0xF3, 0xFF),
        InputBorder = Rgb(0xA7, 0x8B, 0xFA),
        ButtonBg = Rgb(0xF4, 0x2F, 0xA6),
        ButtonFg = Rgb(0xFF, 0xFF, 0xFF),
        ButtonBorder = Rgb(0xDB, 0x27, 0x7A),
        StatusBg = Rgb(0x0C, 0x08, 0x16),
        StatusFg = Rgb(0xE9, 0xD5, 0xFF),
        StatusMuted = Rgb(0x67, 0xE8, 0xF9),
        Splitter = Rgb(0x6D, 0x28, 0xD9),
        CommentPanelBg = Rgb(0x0F, 0x0A, 0x1A),
        CommentPanelBorder = Rgb(0x7C, 0x3A, 0xED),
        ChromeBarR = 0x12, ChromeBarG = 0x0C, ChromeBarB = 0x1E,
        ChromeHoverR = 0x2E, ChromeHoverG = 0x10, ChromeHoverB = 0x4A,
        ChromePressR = 0x4C, ChromePressG = 0x1D, ChromePressB = 0x70,
        ChromeTextR = 0xF5, ChromeTextG = 0xF3, ChromeTextB = 0xFF,
        SettingsWindowBg = Rgb(0x08, 0x06, 0x12),
        SettingsLabel = Rgb(0xE9, 0xD5, 0xFF),
        SettingsMuted = Rgb(0xA7, 0x8B, 0xFA),
        SettingsFieldBg = Rgb(0x1A, 0x10, 0x2E),
        SettingsFieldFg = Rgb(0xF5, 0xF3, 0xFF),
    };

    /// <summary>Soft sakura / blush pink chrome.</summary>
    public static UiTheme Sakura { get; } = new()
    {
        Id = "Sakura",
        DisplayName = "Sakura — 桜ピンク",
        UseAccentButton = true,
        WindowBg = Rgb(0x2A, 0x1E, 0x24),
        WriteBarBg = Rgb(0x3A, 0x28, 0x32),
        WriteBarBorder = Rgb(0x8B, 0x45, 0x6A),
        WriteBarMuted = Rgb(0xF0, 0xC0, 0xD4),
        InputBg = Rgb(0x4A, 0x32, 0x40),
        InputFg = Rgb(0xFF, 0xF0, 0xF5),
        InputBorder = Rgb(0xE8, 0x7A, 0xA8),
        ButtonBg = Rgb(0xF4, 0x72, 0xB6),
        ButtonFg = Rgb(0x3B, 0x0A, 0x2A),
        ButtonBorder = Rgb(0xDB, 0x27, 0x7A),
        StatusBg = Rgb(0x32, 0x22, 0x2C),
        StatusFg = Rgb(0xFD, 0xE4, 0xEE),
        StatusMuted = Rgb(0xF9, 0xA8, 0xD4),
        Splitter = Rgb(0x9D, 0x4E, 0x6F),
        CommentPanelBg = Rgb(0xFF, 0xF1, 0xF5),
        CommentPanelBorder = Rgb(0xFB, 0xC4, 0xD8),
        ChromeBarR = 0x3A, ChromeBarG = 0x28, ChromeBarB = 0x32,
        ChromeHoverR = 0x5A, ChromeHoverG = 0x38, ChromeHoverB = 0x4A,
        ChromePressR = 0x6E, ChromePressG = 0x44, ChromePressB = 0x5A,
        ChromeTextR = 0xFF, ChromeTextG = 0xF0, ChromeTextB = 0xF5,
        SettingsWindowBg = Rgb(0x2A, 0x1E, 0x24),
        SettingsLabel = Rgb(0xFD, 0xE4, 0xEE),
        SettingsMuted = Rgb(0xF0, 0xA8, 0xC8),
        SettingsFieldBg = Rgb(0x4A, 0x32, 0x40),
        SettingsFieldFg = Rgb(0xFF, 0xF0, 0xF5),
    };

    /// <summary>Phosphor terminal — pure black + matrix green.</summary>
    public static UiTheme Terminal { get; } = new()
    {
        Id = "Terminal",
        DisplayName = "Terminal — 緑蛍光",
        UseAccentButton = true,
        WindowBg = Rgb(0x00, 0x00, 0x00),
        WriteBarBg = Rgb(0x05, 0x0A, 0x05),
        WriteBarBorder = Rgb(0x00, 0x66, 0x22),
        WriteBarMuted = Rgb(0x4A, 0xDE, 0x80),
        InputBg = Rgb(0x0A, 0x14, 0x0A),
        InputFg = Rgb(0x86, 0xEF, 0xAC),
        InputBorder = Rgb(0x16, 0xA3, 0x4A),
        ButtonBg = Rgb(0x22, 0xC5, 0x5E),
        ButtonFg = Rgb(0x05, 0x1A, 0x0A),
        ButtonBorder = Rgb(0x16, 0xA3, 0x4A),
        StatusBg = Rgb(0x02, 0x08, 0x02),
        StatusFg = Rgb(0x86, 0xEF, 0xAC),
        StatusMuted = Rgb(0x4A, 0xDE, 0x80),
        Splitter = Rgb(0x14, 0x5A, 0x2A),
        CommentPanelBg = Rgb(0x00, 0x00, 0x00),
        CommentPanelBorder = Rgb(0x16, 0xA3, 0x4A),
        ChromeBarR = 0x05, ChromeBarG = 0x0A, ChromeBarB = 0x05,
        ChromeHoverR = 0x0A, ChromeHoverG = 0x22, ChromeHoverB = 0x12,
        ChromePressR = 0x0E, ChromePressG = 0x32, ChromePressB = 0x18,
        ChromeTextR = 0x86, ChromeTextG = 0xEF, ChromeTextB = 0xAC,
        SettingsWindowBg = Rgb(0x00, 0x00, 0x00),
        SettingsLabel = Rgb(0x86, 0xEF, 0xAC),
        SettingsMuted = Rgb(0x4A, 0xDE, 0x80),
        SettingsFieldBg = Rgb(0x0A, 0x14, 0x0A),
        SettingsFieldFg = Rgb(0x86, 0xEF, 0xAC),
    };

    /// <summary>Ember / live broadcast: charcoal + lava orange.</summary>
    public static UiTheme Ember { get; } = new()
    {
        Id = "Ember",
        DisplayName = "Ember — 炎オレンジ",
        UseAccentButton = true,
        WindowBg = Rgb(0x1A, 0x0C, 0x08),
        WriteBarBg = Rgb(0x28, 0x12, 0x0A),
        WriteBarBorder = Rgb(0x9A, 0x34, 0x12),
        WriteBarMuted = Rgb(0xFD, 0xBA, 0x74),
        InputBg = Rgb(0x3A, 0x1A, 0x0E),
        InputFg = Rgb(0xFF, 0xF7, 0xED),
        InputBorder = Rgb(0xEA, 0x58, 0x0C),
        ButtonBg = Rgb(0xF9, 0x73, 0x16),
        ButtonFg = Rgb(0x2A, 0x0C, 0x00),
        ButtonBorder = Rgb(0xC2, 0x41, 0x0C),
        StatusBg = Rgb(0x1E, 0x0E, 0x08),
        StatusFg = Rgb(0xFF, 0xED, 0xD5),
        StatusMuted = Rgb(0xFB, 0x92, 0x3C),
        Splitter = Rgb(0x7C, 0x2D, 0x12),
        CommentPanelBg = Rgb(0xFF, 0xF7, 0xED),
        CommentPanelBorder = Rgb(0xFD, 0xBA, 0x74),
        ChromeBarR = 0x28, ChromeBarG = 0x12, ChromeBarB = 0x0A,
        ChromeHoverR = 0x4A, ChromeHoverG = 0x1C, ChromeHoverB = 0x0C,
        ChromePressR = 0x62, ChromePressG = 0x24, ChromePressB = 0x0E,
        ChromeTextR = 0xFF, ChromeTextG = 0xF7, ChromeTextB = 0xED,
        SettingsWindowBg = Rgb(0x1A, 0x0C, 0x08),
        SettingsLabel = Rgb(0xFF, 0xED, 0xD5),
        SettingsMuted = Rgb(0xFD, 0xBA, 0x74),
        SettingsFieldBg = Rgb(0x3A, 0x1A, 0x0E),
        SettingsFieldFg = Rgb(0xFF, 0xF7, 0xED),
    };

    public static IReadOnlyList<UiTheme> All { get; } =
        new[] { Classic, Grok, Neon, Sakura, Terminal, Ember };

    public static UiTheme FromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Grok;
        // Legacy ids from previous build
        var key = id.Trim();
        if (key.Equals("Ocean", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Slate", StringComparison.OrdinalIgnoreCase))
            return Neon;
        if (key.Equals("Forest", StringComparison.OrdinalIgnoreCase))
            return Terminal;
        foreach (var t in All)
        {
            if (string.Equals(t.Id, key, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return Grok;
    }

    public static string NormalizeId(string? id) => FromId(id).Id;

    public MediaBrush Brush(MediaColor c)
    {
        var b = new MediaBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    private static MediaColor Rgb(byte r, byte g, byte b) =>
        MediaColor.FromRgb(r, g, b);
}
