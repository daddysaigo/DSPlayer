using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;

namespace DSPlayer.Themes;

/// <summary>
/// App-wide visual skin. <see cref="Classic"/> = original dark chrome + flat comments.
/// <see cref="Grok"/> = warm paper comments + softer chrome (switch anytime in settings).
/// </summary>
public sealed class UiTheme
{
    public required string Id { get; init; }
    public bool IsGrok => string.Equals(Id, "Grok", StringComparison.OrdinalIgnoreCase);

    // Window / chrome (WPF colors)
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

    // Comment panel (also driven by list theme styles)
    public required MediaColor CommentPanelBg { get; init; }
    public required MediaColor CommentPanelBorder { get; init; }

    // WinForms chrome (opaque RGB only)
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

    // Settings dialog
    public required MediaColor SettingsWindowBg { get; init; }
    public required MediaColor SettingsLabel { get; init; }
    public required MediaColor SettingsMuted { get; init; }
    public required MediaColor SettingsFieldBg { get; init; }
    public required MediaColor SettingsFieldFg { get; init; }

    public static UiTheme Classic { get; } = new()
    {
        Id = "Classic",
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

    /// <summary>Warm paper + soft charcoal chrome, matches Grok comment cards.</summary>
    public static UiTheme Grok { get; } = new()
    {
        Id = "Grok",
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

    public static UiTheme FromId(string? id) =>
        string.Equals(id, "Classic", StringComparison.OrdinalIgnoreCase) ? Classic : Grok;

    public MediaBrush Brush(MediaColor c)
    {
        var b = new MediaBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    private static MediaColor Rgb(byte r, byte g, byte b) =>
        MediaColor.FromRgb(r, g, b);
}
