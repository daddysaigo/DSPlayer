using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NewPCRPlayer.Models;
using NewPCRPlayer.Services.Bbs;

namespace NewPCRPlayer;

public partial class SettingsWindow : Window
{
    private static readonly string[] CommonFonts =
    {
        "Meiryo UI",
        "Yu Gothic UI",
        "Yu Gothic",
        "MS UI Gothic",
        "ＭＳ Ｐゴシック",
        "ＭＳ ゴシック",
        "メイリオ",
        "Segoe UI",
        "Consolas",
        "Cascadia Mono",
    };

    private static readonly string[] WeightChoices =
    {
        "Light",
        "Normal",
        "Medium",
        "SemiBold",
        "Bold",
    };

    private static readonly string[] ColorPresets =
    {
        "#888888",
        "#666666",
        "#AAAAAA",
        "#111111",
        "#000000",
        "#333333",
        "#0066CC",
        "#CC0000",
    };

    public AppSettings Settings { get; }

    private readonly Action? _onLiveApply;
    private readonly DispatcherTimer _liveDebounce;
    private bool _suppressLive;

    public SettingsWindow(AppSettings settings, Action? onLiveApply = null)
    {
        InitializeComponent();
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onLiveApply = onLiveApply;

        _liveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _liveDebounce.Tick += (_, _) =>
        {
            _liveDebounce.Stop();
            PushAndLiveApply();
        };

        FillFontCombo(HeaderFontFamilyBox);
        FillFontCombo(BodyFontFamilyBox);
        FillWeightCombo(HeaderWeightBox);
        FillWeightCombo(BodyWeightBox);
        FillColorCombo(HeaderColorBox);
        FillColorCombo(BodyColorBox);

        _suppressLive = true;
        try
        {
            UiThemeBox.Items.Add("Classic — 従来ダーク");
            UiThemeBox.Items.Add("Grok — 暖色クローム");
            UiThemeBox.SelectedIndex =
                settings.UiTheme.Equals("Classic", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

            CommentThemeBox.Items.Add("Classic — 平面リスト");
            CommentThemeBox.Items.Add("Grok — カード風");
            CommentThemeBox.SelectedIndex =
                settings.CommentListTheme.Equals("Classic", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

            try
            {
                var t = Themes.UiTheme.FromId(settings.UiTheme);
                Background = new SolidColorBrush(t.SettingsWindowBg);
            }
            catch { /* ignore */ }

            HeaderFontFamilyBox.Text = settings.CommentHeaderFontFamily;
            HeaderFontSizeBox.Text = settings.CommentHeaderFontSize.ToString("0.#");
            SelectOrAdd(HeaderWeightBox, settings.CommentHeaderFontWeight);
            HeaderColorBox.Text = settings.CommentHeaderColor;

            BodyFontFamilyBox.Text = settings.CommentBodyFontFamily;
            BodyFontSizeBox.Text = settings.CommentBodyFontSize.ToString("0.#");
            SelectOrAdd(BodyWeightBox, settings.CommentBodyFontWeight);
            BodyColorBox.Text = settings.CommentBodyColor;

            ChkShowNumber.IsChecked = settings.ShowResNumber;
            ChkShowName.IsChecked = settings.ShowResName;
            ChkShowDate.IsChecked = settings.ShowResDate;
            IntervalBox.Text = settings.BbsIntervalSeconds.ToString();
            ChkMessageNormalize.IsChecked = settings.MessageNormalize;
        }
        finally
        {
            _suppressLive = false;
        }

        WireLiveCombo(HeaderFontFamilyBox);
        WireLiveCombo(HeaderWeightBox);
        WireLiveCombo(HeaderColorBox);
        WireLiveCombo(BodyFontFamilyBox);
        WireLiveCombo(BodyWeightBox);
        WireLiveCombo(BodyColorBox);
        WireLiveCombo(UiThemeBox);
        WireLiveCombo(CommentThemeBox);
        WireLiveText(HeaderFontSizeBox);
        WireLiveText(BodyFontSizeBox);
        WireLiveText(IntervalBox);

        ChkShowNumber.Checked += (_, _) => OnAnyChanged();
        ChkShowNumber.Unchecked += (_, _) => OnAnyChanged();
        ChkShowName.Checked += (_, _) => OnAnyChanged();
        ChkShowName.Unchecked += (_, _) => OnAnyChanged();
        ChkShowDate.Checked += (_, _) => OnAnyChanged();
        ChkShowDate.Unchecked += (_, _) => OnAnyChanged();
        ChkMessageNormalize.Checked += (_, _) => OnAnyChanged();
        ChkMessageNormalize.Unchecked += (_, _) => OnAnyChanged();

        UpdatePreview();
    }

    private void WireLiveCombo(System.Windows.Controls.ComboBox box)
    {
        box.SelectionChanged += (_, _) => OnAnyChanged();
        box.LostFocus += (_, _) => OnAnyChanged();
    }

    private void WireLiveText(System.Windows.Controls.TextBox box)
    {
        box.TextChanged += (_, _) => OnAnyChanged();
        box.LostFocus += (_, _) => OnAnyChanged();
    }

    private void OnAnyChanged()
    {
        if (_suppressLive) return;
        UpdatePreview();
        _liveDebounce.Stop();
        _liveDebounce.Start();
    }

    private void PushAndLiveApply()
    {
        if (_suppressLive) return;
        try
        {
            WriteFieldsToSettings();
            Settings.Sanitize();
            try
            {
                var t = Themes.UiTheme.FromId(Settings.UiTheme);
                Background = new SolidColorBrush(t.SettingsWindowBg);
            }
            catch { /* ignore */ }
            _onLiveApply?.Invoke();
        }
        catch
        {
            // ignore partial invalid input while typing
        }
    }

    private void WriteFieldsToSettings()
    {
        Settings.UiTheme = UiThemeBox.SelectedIndex == 0 ? "Classic" : "Grok";
        Settings.CommentListTheme = CommentThemeBox.SelectedIndex == 0 ? "Classic" : "Grok";

        Settings.CommentHeaderFontFamily = string.IsNullOrWhiteSpace(HeaderFontFamilyBox.Text)
            ? "Meiryo UI" : HeaderFontFamilyBox.Text.Trim();
        Settings.CommentBodyFontFamily = string.IsNullOrWhiteSpace(BodyFontFamilyBox.Text)
            ? "Meiryo UI" : BodyFontFamilyBox.Text.Trim();
        if (double.TryParse(HeaderFontSizeBox.Text?.Trim(), out var hs))
            Settings.CommentHeaderFontSize = hs;
        if (double.TryParse(BodyFontSizeBox.Text?.Trim(), out var bs))
            Settings.CommentBodyFontSize = bs;

        Settings.CommentHeaderFontWeight =
            (HeaderWeightBox.SelectedItem as string) ?? HeaderWeightBox.Text ?? "Normal";
        Settings.CommentBodyFontWeight =
            (BodyWeightBox.SelectedItem as string) ?? BodyWeightBox.Text ?? "Normal";
        Settings.CommentHeaderColor = string.IsNullOrWhiteSpace(HeaderColorBox.Text)
            ? "#888888" : HeaderColorBox.Text.Trim();
        Settings.CommentBodyColor = string.IsNullOrWhiteSpace(BodyColorBox.Text)
            ? "#111111" : BodyColorBox.Text.Trim();

        Settings.ShowResNumber = ChkShowNumber.IsChecked == true;
        Settings.ShowResName = ChkShowName.IsChecked == true;
        Settings.ShowResDate = ChkShowDate.IsChecked == true;
        if (int.TryParse(IntervalBox.Text?.Trim(), out var sec))
            Settings.BbsIntervalSeconds = sec;
        Settings.MessageNormalize = ChkMessageNormalize.IsChecked == true;
    }

    private static void FillFontCombo(System.Windows.Controls.ComboBox box)
    {
        foreach (var f in CommonFonts)
            box.Items.Add(f);
        try
        {
            foreach (var ff in Fonts.SystemFontFamilies
                         .Select(f => f.Source)
                         .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                         .Take(80))
            {
                if (!box.Items.Contains(ff))
                    box.Items.Add(ff);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void FillWeightCombo(System.Windows.Controls.ComboBox box)
    {
        foreach (var w in WeightChoices)
            box.Items.Add(w);
    }

    private static void FillColorCombo(System.Windows.Controls.ComboBox box)
    {
        foreach (var c in ColorPresets)
            box.Items.Add(c);
    }

    private static void SelectOrAdd(System.Windows.Controls.ComboBox box, string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? "Normal" : value.Trim();
        if (!box.Items.Contains(v))
            box.Items.Add(v);
        box.SelectedItem = v;
        box.Text = v;
    }

    private void UpdatePreview()
    {
        try
        {
            var tmp = new AppSettings
            {
                ShowResNumber = ChkShowNumber.IsChecked == true,
                ShowResName = ChkShowName.IsChecked == true,
                ShowResDate = ChkShowDate.IsChecked == true,
            };
            var sample = new BbsPost
            {
                Number = 123,
                Name = "名前",
                DateId = "2026/01/01 12:00:00",
            };
            HeaderPreview.Text = tmp.FormatResHeader(sample);
            if (string.IsNullOrEmpty(HeaderPreview.Text))
                HeaderPreview.Text = "（表示項目なし）";

            var hf = string.IsNullOrWhiteSpace(HeaderFontFamilyBox.Text) ? "Meiryo UI" : HeaderFontFamilyBox.Text.Trim();
            HeaderPreview.FontFamily = new System.Windows.Media.FontFamily(hf);
            if (double.TryParse(HeaderFontSizeBox.Text?.Trim(), out var hs) && hs is >= 8 and <= 36)
                HeaderPreview.FontSize = hs;
            HeaderPreview.FontWeight = ParseWeight(HeaderWeightBox.Text ?? HeaderWeightBox.SelectedItem as string);
            HeaderPreview.Foreground = ParseBrush(HeaderColorBox.Text, "#888888");

            var bf = string.IsNullOrWhiteSpace(BodyFontFamilyBox.Text) ? "Meiryo UI" : BodyFontFamilyBox.Text.Trim();
            BodyPreview.FontFamily = new System.Windows.Media.FontFamily(bf);
            if (double.TryParse(BodyFontSizeBox.Text?.Trim(), out var bs) && bs is >= 8 and <= 36)
                BodyPreview.FontSize = bs;
            BodyPreview.FontWeight = ParseWeight(BodyWeightBox.Text ?? BodyWeightBox.SelectedItem as string);
            BodyPreview.Foreground = ParseBrush(BodyColorBox.Text, "#111111");
        }
        catch
        {
            // ignore
        }
    }

    private static FontWeight ParseWeight(string? name) =>
        (name ?? "").Trim().ToLowerInvariant() switch
        {
            "light" => FontWeights.Light,
            "medium" => FontWeights.Medium,
            "semibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            _ => FontWeights.Normal,
        };

    private static System.Windows.Media.Brush ParseBrush(string? hex, string fallback)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim())!;
            var b = new SolidColorBrush(c);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
        catch
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback)!;
            var b = new SolidColorBrush(c);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _liveDebounce.Stop();
        WriteFieldsToSettings();
        Settings.Sanitize();
        _onLiveApply?.Invoke();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _liveDebounce.Stop();
        DialogResult = false;
    }
}
