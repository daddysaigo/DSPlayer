using System.Windows;
using MediaColor = System.Windows.Media.Color;

namespace DSPlayer.Themes;

public enum CommentLayoutKind
{
    /// <summary>PCR-like flat rows.</summary>
    Flat,
    /// <summary>Card with res# badge (Grok / Neon / Sakura / Sticky / Bubble).</summary>
    Card,
}

/// <summary>
/// Comment panel + list skin. Independent of chrome <see cref="UiTheme"/>.
/// Layouts are Flat or Card; each skin has its own geometry (radius, accent, padding).
/// </summary>
public sealed class CommentListTheme
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required CommentLayoutKind Layout { get; init; }

    public required MediaColor PanelBg { get; init; }
    public required MediaColor PanelBorder { get; init; }

    public required Thickness ListPadding { get; init; }
    public required Thickness ItemPadding { get; init; }
    public required Thickness ItemMargin { get; init; }

    // Flat
    public required MediaColor RowBorder { get; init; }
    public required MediaColor RowHover { get; init; }
    public required MediaColor RowNewBg { get; init; }
    public required MediaColor RowNewBorder { get; init; }

    // Card shell
    public required MediaColor CardBg { get; init; }
    public required MediaColor CardBorder { get; init; }
    public required MediaColor CardHoverBorder { get; init; }
    public required MediaColor CardNewBg { get; init; }
    public required MediaColor CardNewBorder { get; init; }
    public required MediaColor CardNewAccent { get; init; }
    /// <summary>Always-visible left stripe (Transparent = only on IsNew).</summary>
    public required MediaColor CardAccentIdle { get; init; }
    public required double CardCornerRadius { get; init; }
    public required Thickness CardInnerPadding { get; init; }
    public required MediaColor BadgeBg { get; init; }
    public required MediaColor BadgeFg { get; init; }

    public string? SuggestedBodyColor { get; init; }
    public string? SuggestedHeaderColor { get; init; }
    public bool IsDark { get; init; }

    public System.Windows.Controls.SelectionMode SelectionMode =>
        Layout == CommentLayoutKind.Card
            ? System.Windows.Controls.SelectionMode.Single
            : System.Windows.Controls.SelectionMode.Extended;

    /// <summary>PCR-like flat light list — functional baseline.</summary>
    public static CommentListTheme Classic { get; } = new()
    {
        Id = "Classic",
        DisplayName = "Classic — 平面リスト",
        Layout = CommentLayoutKind.Flat,
        PanelBg = Rgb(0xF0, 0xF0, 0xF0),
        PanelBorder = Rgb(0xD0, 0xD0, 0xD0),
        ListPadding = new Thickness(4, 0, 4, 0),
        ItemPadding = new Thickness(6, 8, 6, 8),
        ItemMargin = new Thickness(0, 0, 0, 2),
        RowBorder = Rgb(0xD0, 0xD0, 0xD0),
        RowHover = Rgb(0xE8, 0xE8, 0xE8),
        RowNewBg = Rgb(0xB8, 0xD4, 0xF0),
        RowNewBorder = Rgb(0x4A, 0x90, 0xD9),
        CardBg = Rgb(0xFF, 0xFF, 0xFF),
        CardBorder = Rgb(0xE0, 0xE0, 0xE0),
        CardHoverBorder = Rgb(0xC8, 0xC8, 0xC8),
        CardNewBg = Rgb(0xE8, 0xF2, 0xFC),
        CardNewBorder = Rgb(0xA8, 0xC8, 0xE8),
        CardNewAccent = Rgb(0x4A, 0x90, 0xD9),
        CardAccentIdle = Rgb(0x00, 0x00, 0x00), // unused (A=0 via Transparent setter)
        CardCornerRadius = 0,
        CardInnerPadding = new Thickness(10, 8, 10, 10),
        BadgeBg = Rgb(0xE8, 0xE8, 0xE8),
        BadgeFg = Rgb(0x55, 0x55, 0x55),
        SuggestedBodyColor = "#111111",
        SuggestedHeaderColor = "#888888",
        IsDark = false,
    };

    /// <summary>Signature warm paper cards + amber new accent.</summary>
    public static CommentListTheme Grok { get; } = new()
    {
        Id = "Grok",
        DisplayName = "Grok — 紙カード",
        Layout = CommentLayoutKind.Card,
        PanelBg = Rgb(0xF3, 0xEF, 0xE8),
        PanelBorder = Rgb(0xD8, 0xD2, 0xC8),
        ListPadding = new Thickness(0, 8, 0, 8),
        ItemPadding = new Thickness(0),
        ItemMargin = new Thickness(8, 0, 8, 8),
        RowBorder = Rgb(0xD8, 0xD2, 0xC8),
        RowHover = Rgb(0xEB, 0xE6, 0xDC),
        RowNewBg = Rgb(0xFF, 0xF6, 0xE8),
        RowNewBorder = Rgb(0xE0, 0xA8, 0x4A),
        CardBg = Rgb(0xFF, 0xFC, 0xF8),
        CardBorder = Rgb(0xE4, 0xDF, 0xD6),
        CardHoverBorder = Rgb(0xD5, 0xCF, 0xC4),
        CardNewBg = Rgb(0xFF, 0xF6, 0xE8),
        CardNewBorder = Rgb(0xE8, 0xD4, 0xA8),
        CardNewAccent = Rgb(0xE0, 0xA8, 0x4A),
        CardAccentIdle = Transparent,
        CardCornerRadius = 8,
        CardInnerPadding = new Thickness(10, 8, 10, 10),
        BadgeBg = Rgb(0xEE, 0xE8, 0xDF),
        BadgeFg = Rgb(0x6B, 0x65, 0x60),
        SuggestedBodyColor = "#111111",
        SuggestedHeaderColor = "#888888",
        IsDark = false,
    };

    /// <summary>Void panel + glass neon cards, always-on cyan rail.</summary>
    public static CommentListTheme Neon { get; } = new()
    {
        Id = "Neon",
        DisplayName = "Neon — ネオンカード",
        Layout = CommentLayoutKind.Card,
        IsDark = true,
        PanelBg = Rgb(0x0A, 0x06, 0x14),
        PanelBorder = Rgb(0x7C, 0x3A, 0xED),
        ListPadding = new Thickness(0, 10, 0, 10),
        ItemPadding = new Thickness(0),
        ItemMargin = new Thickness(10, 0, 10, 10),
        RowBorder = Rgb(0x4C, 0x1D, 0x95),
        RowHover = Rgb(0x1A, 0x10, 0x2E),
        RowNewBg = Rgb(0x2E, 0x10, 0x4A),
        RowNewBorder = Rgb(0xF4, 0x2F, 0xA6),
        CardBg = Rgb(0x14, 0x0C, 0x24),
        CardBorder = Rgb(0xA7, 0x8B, 0xFA),
        CardHoverBorder = Rgb(0xC4, 0xB5, 0xFD),
        CardNewBg = Rgb(0x2A, 0x10, 0x3A),
        CardNewBorder = Rgb(0xF4, 0x72, 0xB6),
        CardNewAccent = Rgb(0xF4, 0x2F, 0xA6),
        CardAccentIdle = Rgb(0x22, 0xD3, 0xEE), // cyan rail always
        CardCornerRadius = 12,
        CardInnerPadding = new Thickness(12, 10, 12, 12),
        BadgeBg = Rgb(0x4C, 0x1D, 0x95),
        BadgeFg = Rgb(0xE9, 0xD5, 0xFF),
        SuggestedBodyColor = "#F5F3FF",
        SuggestedHeaderColor = "#A78BFA",
    };

    /// <summary>Blush pink cards on soft rose panel.</summary>
    public static CommentListTheme Sakura { get; } = new()
    {
        Id = "Sakura",
        DisplayName = "Sakura — 桜カード",
        Layout = CommentLayoutKind.Card,
        PanelBg = Rgb(0xFF, 0xF1, 0xF5),
        PanelBorder = Rgb(0xFB, 0xC4, 0xD8),
        ListPadding = new Thickness(0, 8, 0, 8),
        ItemPadding = new Thickness(0),
        ItemMargin = new Thickness(8, 0, 8, 8),
        RowBorder = Rgb(0xFB, 0xC4, 0xD8),
        RowHover = Rgb(0xFD, 0xE4, 0xEE),
        RowNewBg = Rgb(0xFC, 0xE7, 0xF3),
        RowNewBorder = Rgb(0xF4, 0x72, 0xB6),
        CardBg = Rgb(0xFF, 0xFB, 0xFC),
        CardBorder = Rgb(0xF9, 0xA8, 0xD4),
        CardHoverBorder = Rgb(0xF4, 0x72, 0xB6),
        CardNewBg = Rgb(0xFD, 0xE4, 0xEE),
        CardNewBorder = Rgb(0xEC, 0x48, 0x99),
        CardNewAccent = Rgb(0xDB, 0x27, 0x7A),
        CardAccentIdle = Rgb(0xFB, 0xC4, 0xD8), // soft always-on blush stripe
        CardCornerRadius = 16,
        CardInnerPadding = new Thickness(12, 10, 12, 12),
        BadgeBg = Rgb(0xFC, 0xE7, 0xF3),
        BadgeFg = Rgb(0x9D, 0x17, 0x4D),
        SuggestedBodyColor = "#3B0A2A",
        SuggestedHeaderColor = "#BE185D",
        IsDark = false,
    };

    /// <summary>Yellow sticky notes on cork board.</summary>
    public static CommentListTheme Sticky { get; } = new()
    {
        Id = "Sticky",
        DisplayName = "Sticky — 付箋",
        Layout = CommentLayoutKind.Card,
        PanelBg = Rgb(0xC4, 0xA4, 0x6C), // cork
        PanelBorder = Rgb(0x8B, 0x6B, 0x3A),
        ListPadding = new Thickness(0, 10, 0, 10),
        ItemPadding = new Thickness(0),
        ItemMargin = new Thickness(12, 0, 12, 12),
        RowBorder = Rgb(0x8B, 0x6B, 0x3A),
        RowHover = Rgb(0xD4, 0xB4, 0x7C),
        RowNewBg = Rgb(0xFE, 0xF0, 0x8A),
        RowNewBorder = Rgb(0xE0, 0x8A, 0x20),
        CardBg = Rgb(0xFE, 0xF9, 0xC3), // sticky yellow
        CardBorder = Rgb(0xF5, 0xD0, 0x4E),
        CardHoverBorder = Rgb(0xEA, 0xB3, 0x08),
        CardNewBg = Rgb(0xFE, 0xF0, 0x8A),
        CardNewBorder = Rgb(0xF5, 0x9E, 0x0B),
        CardNewAccent = Rgb(0xDC, 0x26, 0x26), // red pin accent
        CardAccentIdle = Transparent,
        CardCornerRadius = 2, // almost square, note-like
        CardInnerPadding = new Thickness(12, 12, 12, 14),
        BadgeBg = Rgb(0xFE, 0xF0, 0x8A),
        BadgeFg = Rgb(0x85, 0x4D, 0x0E),
        SuggestedBodyColor = "#422006",
        SuggestedHeaderColor = "#A16207",
        IsDark = false,
    };

    /// <summary>Chat bubbles — large radius, always-on accent rail.</summary>
    public static CommentListTheme Bubble { get; } = new()
    {
        Id = "Bubble",
        DisplayName = "Bubble — 吹き出し",
        Layout = CommentLayoutKind.Card,
        PanelBg = Rgb(0xE8, 0xF0, 0xFE),
        PanelBorder = Rgb(0x90, 0xCA, 0xF9),
        ListPadding = new Thickness(0, 10, 0, 10),
        ItemPadding = new Thickness(0),
        ItemMargin = new Thickness(14, 0, 28, 10), // asymmetric = speech-bubble feel
        RowBorder = Rgb(0x90, 0xCA, 0xF9),
        RowHover = Rgb(0xDB, 0xEA, 0xFE),
        RowNewBg = Rgb(0xBF, 0xDB, 0xFE),
        RowNewBorder = Rgb(0x25, 0x63, 0xEB),
        CardBg = Rgb(0xFF, 0xFF, 0xFF),
        CardBorder = Rgb(0xBB, 0xDE, 0xFB),
        CardHoverBorder = Rgb(0x64, 0xB5, 0xF6),
        CardNewBg = Rgb(0xE3, 0xF2, 0xFD),
        CardNewBorder = Rgb(0x42, 0xA5, 0xF5),
        CardNewAccent = Rgb(0x1D, 0x4E, 0xD8),
        CardAccentIdle = Rgb(0x42, 0xA5, 0xF5),
        CardCornerRadius = 20,
        CardInnerPadding = new Thickness(14, 10, 14, 12),
        BadgeBg = Rgb(0xDB, 0xEA, 0xFE),
        BadgeFg = Rgb(0x1E, 0x40, 0xAF),
        SuggestedBodyColor = "#0F172A",
        SuggestedHeaderColor = "#2563EB",
        IsDark = false,
    };

    /// <summary>Matrix terminal feed — dense green-on-black flat rows.</summary>
    public static CommentListTheme Terminal { get; } = new()
    {
        Id = "Terminal",
        DisplayName = "Terminal — 端末ログ",
        Layout = CommentLayoutKind.Flat,
        IsDark = true,
        PanelBg = Rgb(0x00, 0x00, 0x00),
        PanelBorder = Rgb(0x16, 0xA3, 0x4A),
        ListPadding = new Thickness(2, 0, 2, 0),
        ItemPadding = new Thickness(6, 5, 6, 5),
        ItemMargin = new Thickness(0, 0, 0, 0),
        RowBorder = Rgb(0x14, 0x5A, 0x2A),
        RowHover = Rgb(0x05, 0x14, 0x0A),
        RowNewBg = Rgb(0x05, 0x2E, 0x16),
        RowNewBorder = Rgb(0x4A, 0xDE, 0x80),
        CardBg = Rgb(0x05, 0x0A, 0x05),
        CardBorder = Rgb(0x16, 0xA3, 0x4A),
        CardHoverBorder = Rgb(0x22, 0xC5, 0x5E),
        CardNewBg = Rgb(0x05, 0x2E, 0x16),
        CardNewBorder = Rgb(0x4A, 0xDE, 0x80),
        CardNewAccent = Rgb(0x86, 0xEF, 0xAC),
        CardAccentIdle = Transparent,
        CardCornerRadius = 0,
        CardInnerPadding = new Thickness(8, 6, 8, 8),
        BadgeBg = Rgb(0x14, 0x5A, 0x2A),
        BadgeFg = Rgb(0x86, 0xEF, 0xAC),
        SuggestedBodyColor = "#86EFAC",
        SuggestedHeaderColor = "#4ADE80",
    };

    private static MediaColor Transparent => MediaColor.FromArgb(0, 0, 0, 0);

    public static IReadOnlyList<CommentListTheme> All { get; } =
        new[] { Classic, Grok, Neon, Sakura, Sticky, Bubble, Terminal };

    public static CommentListTheme FromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Grok;
        var key = id.Trim();
        // Legacy
        if (key.Equals("Compact", StringComparison.OrdinalIgnoreCase))
            return Classic;
        if (key.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            return Terminal;
        if (key.Equals("Night", StringComparison.OrdinalIgnoreCase))
            return Neon;
        foreach (var t in All)
        {
            if (string.Equals(t.Id, key, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return Grok;
    }

    public static string NormalizeId(string? id) => FromId(id).Id;

    private static MediaColor Rgb(byte r, byte g, byte b) =>
        MediaColor.FromRgb(r, g, b);
}
