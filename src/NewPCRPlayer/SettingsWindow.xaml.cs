using System.Windows;
using System.Windows.Media;
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

    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        FillFontCombo(HeaderFontFamilyBox);
        FillFontCombo(BodyFontFamilyBox);

        HeaderFontFamilyBox.Text = settings.CommentHeaderFontFamily;
        HeaderFontSizeBox.Text = settings.CommentHeaderFontSize.ToString("0.#");
        BodyFontFamilyBox.Text = settings.CommentBodyFontFamily;
        BodyFontSizeBox.Text = settings.CommentBodyFontSize.ToString("0.#");
        ChkShowNumber.IsChecked = settings.ShowResNumber;
        ChkShowName.IsChecked = settings.ShowResName;
        ChkShowDate.IsChecked = settings.ShowResDate;
        IntervalBox.Text = settings.BbsIntervalSeconds.ToString();
        ChkMessageNormalize.IsChecked = settings.MessageNormalize;

        HeaderFontFamilyBox.SelectionChanged += (_, _) => UpdatePreview();
        HeaderFontFamilyBox.LostFocus += (_, _) => UpdatePreview();
        HeaderFontSizeBox.TextChanged += (_, _) => UpdatePreview();
        BodyFontFamilyBox.SelectionChanged += (_, _) => UpdatePreview();
        BodyFontFamilyBox.LostFocus += (_, _) => UpdatePreview();
        BodyFontSizeBox.TextChanged += (_, _) => UpdatePreview();
        ChkShowNumber.Checked += (_, _) => UpdatePreview();
        ChkShowNumber.Unchecked += (_, _) => UpdatePreview();
        ChkShowName.Checked += (_, _) => UpdatePreview();
        ChkShowName.Unchecked += (_, _) => UpdatePreview();
        ChkShowDate.Checked += (_, _) => UpdatePreview();
        ChkShowDate.Unchecked += (_, _) => UpdatePreview();
        UpdatePreview();
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

            var bf = string.IsNullOrWhiteSpace(BodyFontFamilyBox.Text) ? "Meiryo UI" : BodyFontFamilyBox.Text.Trim();
            BodyPreview.FontFamily = new System.Windows.Media.FontFamily(bf);
            if (double.TryParse(BodyFontSizeBox.Text?.Trim(), out var bs) && bs is >= 8 and <= 36)
                BodyPreview.FontSize = bs;
        }
        catch
        {
            // ignore
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Settings.CommentHeaderFontFamily = string.IsNullOrWhiteSpace(HeaderFontFamilyBox.Text)
            ? "Meiryo UI" : HeaderFontFamilyBox.Text.Trim();
        Settings.CommentBodyFontFamily = string.IsNullOrWhiteSpace(BodyFontFamilyBox.Text)
            ? "Meiryo UI" : BodyFontFamilyBox.Text.Trim();
        if (double.TryParse(HeaderFontSizeBox.Text?.Trim(), out var hs))
            Settings.CommentHeaderFontSize = hs;
        if (double.TryParse(BodyFontSizeBox.Text?.Trim(), out var bs))
            Settings.CommentBodyFontSize = bs;
        Settings.ShowResNumber = ChkShowNumber.IsChecked == true;
        Settings.ShowResName = ChkShowName.IsChecked == true;
        Settings.ShowResDate = ChkShowDate.IsChecked == true;
        if (int.TryParse(IntervalBox.Text?.Trim(), out var sec))
            Settings.BbsIntervalSeconds = sec;
        Settings.MessageNormalize = ChkMessageNormalize.IsChecked == true;
        Settings.Sanitize();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
