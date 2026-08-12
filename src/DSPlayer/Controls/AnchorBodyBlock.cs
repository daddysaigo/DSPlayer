using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DSPlayer.Controls;

/// <summary>
/// Selectable comment body (read-only <see cref="RichTextBox"/>) with clickable &gt;&gt;N anchors.
/// Raises a bubbling <see cref="AnchorClickEvent"/> with the target res number.
/// </summary>
public sealed class AnchorBodyBlock : System.Windows.Controls.RichTextBox
{
    private static readonly Regex AnchorRegex = new(
        @"(>>|＞＞|>)(\d{1,4})",
        RegexOptions.Compiled);

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(
            nameof(Body),
            typeof(string),
            typeof(AnchorBodyBlock),
            new PropertyMetadata(null, OnBodyChanged));

    public static readonly RoutedEvent AnchorClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(AnchorClick),
            RoutingStrategy.Bubble,
            typeof(EventHandler<AnchorClickEventArgs>),
            typeof(AnchorBodyBlock));

    private static readonly System.Windows.Media.Brush AnchorBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x55, 0xCC));

    static AnchorBodyBlock()
    {
        if (AnchorBrush.CanFreeze) AnchorBrush.Freeze();
    }

    public AnchorBodyBlock()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true; // hyperlinks clickable while read-only
        IsUndoEnabled = false;
        AcceptsTab = false;
        BorderThickness = new Thickness(0);
        Background = System.Windows.Media.Brushes.Transparent;
        Padding = new Thickness(0);
        Margin = new Thickness(0);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        // Allow mouse selection without looking like an edit field
        CaretBrush = System.Windows.Media.Brushes.Transparent;
        SelectionBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0x4A, 0x90, 0xD9));
        FocusVisualStyle = null;
        Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };
    }

    public string? Body
    {
        get => (string?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public event EventHandler<AnchorClickEventArgs> AnchorClick
    {
        add => AddHandler(AnchorClickEvent, value);
        remove => RemoveHandler(AnchorClickEvent, value);
    }

    /// <summary>Currently selected text, if any.</summary>
    public string SelectedText
    {
        get
        {
            try { return Selection?.Text ?? ""; }
            catch { return ""; }
        }
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnchorBodyBlock block)
            block.RebuildDocument(e.NewValue as string);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Re-apply when template-driven font/brush changes
        if (e.Property == FontFamilyProperty ||
            e.Property == FontSizeProperty ||
            e.Property == FontWeightProperty ||
            e.Property == ForegroundProperty)
        {
            ApplyTypographyToDocument();
        }
    }

    private void RebuildDocument(string? text)
    {
        var doc = Document ?? new FlowDocument();
        doc.Blocks.Clear();
        doc.PagePadding = new Thickness(0);
        doc.TextAlignment = TextAlignment.Left;

        var para = new Paragraph
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };

        if (!string.IsNullOrEmpty(text))
        {
            var matches = AnchorRegex.Matches(text);
            if (matches.Count == 0)
            {
                para.Inlines.Add(new Run(text));
            }
            else
            {
                var idx = 0;
                foreach (Match m in matches)
                {
                    if (m.Index > idx)
                        para.Inlines.Add(new Run(text[idx..m.Index]));

                    var numStr = m.Groups[2].Value;
                    if (!int.TryParse(numStr, out var num) || num <= 0)
                    {
                        para.Inlines.Add(new Run(m.Value));
                    }
                    else
                    {
                        var link = new Hyperlink(new Run(m.Value))
                        {
                            Foreground = AnchorBrush,
                            TextDecorations = null,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            NavigateUri = null,
                            Focusable = false,
                            ToolTip = "レス " + num + " へ移動",
                            Tag = num,
                        };
                        link.Click += Link_Click;
                        para.Inlines.Add(link);
                    }

                    idx = m.Index + m.Length;
                }

                if (idx < text.Length)
                    para.Inlines.Add(new Run(text[idx..]));
            }
        }

        doc.Blocks.Add(para);
        Document = doc;
        ApplyTypographyToDocument();
    }

    private void ApplyTypographyToDocument()
    {
        if (Document is null) return;
        try
        {
            Document.FontFamily = FontFamily;
            Document.FontSize = FontSize;
            Document.FontWeight = FontWeight;
            Document.Foreground = Foreground;
            Document.PagePadding = new Thickness(0);
            Document.TextAlignment = TextAlignment.Left;
            // Tight chat-style body (~1.2×). Auto/NaN uses font default; avoid large gaps.
            var lineH = Math.Max(FontSize * 1.2, FontSize + 2);
            Document.LineHeight = lineH;

            foreach (var block in Document.Blocks)
            {
                block.FontFamily = FontFamily;
                block.FontSize = FontSize;
                block.FontWeight = FontWeight;
                block.Foreground = Foreground;
                block.Margin = new Thickness(0);
                block.LineHeight = lineH;
                if (block is Paragraph p)
                {
                    p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    p.LineHeight = lineH;
                }
            }
        }
        catch
        {
            // ignore layout races
        }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Hyperlink { Tag: int num })
            RaiseEvent(new AnchorClickEventArgs(AnchorClickEvent, this, num));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Keep focus for selection; stop ListBox from treating this as item-select only
        Focus();
        base.OnPreviewMouseLeftButtonDown(e);
    }
}

public sealed class AnchorClickEventArgs : RoutedEventArgs
{
    public AnchorClickEventArgs(RoutedEvent routedEvent, object source, int resNumber)
        : base(routedEvent, source)
    {
        ResNumber = resNumber;
    }

    public int ResNumber { get; }
}
