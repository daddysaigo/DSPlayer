using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DSPlayer.Services;
using DSPlayer.Services.Bbs;

namespace DSPlayer.Controls;

/// <summary>
/// Selectable comment body (read-only <see cref="RichTextBox"/>) with clickable &gt;&gt;N
/// anchors, http(s)/ttp(s) links, and optional inline images.
/// Raises a bubbling <see cref="AnchorClickEvent"/> with the target res number.
/// </summary>
public sealed class AnchorBodyBlock : System.Windows.Controls.RichTextBox
{
    private readonly List<CommentImageView> _images = new();

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

    public static readonly RoutedEvent ImageClickEvent =
        EventManager.RegisterRoutedEvent(
            "ImageClick",
            RoutingStrategy.Bubble,
            typeof(EventHandler<CommentImageClickEventArgs>),
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
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
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
            IsHyphenationEnabled = false,
        };
        TryApplyCompactTemplate();
        Loaded += (_, _) =>
        {
            ZeroDocumentInsets();
            ApplyPageWidth();
        };
    }

    private void TryApplyCompactTemplate()
    {
        try
        {
            const string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='RichTextBox'>" +
                "<Border Background='{TemplateBinding Background}' " +
                "BorderBrush='{TemplateBinding BorderBrush}' " +
                "BorderThickness='{TemplateBinding BorderThickness}' " +
                "Padding='0'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='0' Padding='0' " +
                "HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Disabled'/>" +
                "</Border></ControlTemplate>";
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }
        catch
        {
            // keep default template
        }
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

    public event EventHandler<CommentImageClickEventArgs> ImageClick
    {
        add => AddHandler(ImageClickEvent, value);
        remove => RemoveHandler(ImageClickEvent, value);
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
        _images.Clear();
        var doc = Document ?? new FlowDocument();
        doc.Blocks.Clear();
        doc.PagePadding = new Thickness(0);
        doc.TextAlignment = TextAlignment.Left;

        var pageW = ColumnWidth();
        Paragraph? para = null;

        foreach (var seg in CommentBodyParser.Parse(text))
        {
            switch (seg.Kind)
            {
                case CommentSegmentKind.Text:
                    EnsurePara(ref para).Inlines.Add(new Run(seg.Text));
                    break;
                case CommentSegmentKind.Anchor:
                    EnsurePara(ref para).Inlines.Add(MakeAnchorLink(seg));
                    break;
                case CommentSegmentKind.Url:
                    EnsurePara(ref para).Inlines.Add(MakeUrlLink(seg));
                    break;
                case CommentSegmentKind.Image:
                    EnsurePara(ref para).Inlines.Add(MakeUrlLink(seg));
                    if (CommentImageLoader.EmbedEnabled && !string.IsNullOrEmpty(seg.NavigateUrl))
                    {
                        FlushPara(doc, ref para);
                        var img = new CommentImageView();
                        img.BeginLoad(seg.NavigateUrl, pageW);
                        _images.Add(img);
                        var host = new Border
                        {
                            Child = img,
                            Margin = new Thickness(0, 4, 0, 6),
                            // Transparent still hit-tests; FlowDocument Image clicks don't bubble out.
                            Background = System.Windows.Media.Brushes.Transparent,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            SnapsToDevicePixels = true,
                        };
                        doc.Blocks.Add(new BlockUIContainer(host) { Margin = new Thickness(0) });
                    }
                    break;
            }
        }

        FlushPara(doc, ref para);
        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(CreateParagraph());

        Document = doc;
        ApplyPageWidth();
        ApplyTypographyToDocument();
    }

    private static Paragraph CreateParagraph() => new()
    {
        Margin = new Thickness(0),
        Padding = new Thickness(0),
        TextAlignment = TextAlignment.Left,
    };

    private static Paragraph EnsurePara(ref Paragraph? para) => para ??= CreateParagraph();

    private static void FlushPara(FlowDocument doc, ref Paragraph? para)
    {
        if (para is { Inlines.Count: > 0 })
            doc.Blocks.Add(para);
        para = null;
    }

    private Hyperlink MakeAnchorLink(CommentSegment seg)
    {
        var link = new Hyperlink(new Run(seg.Text))
        {
            Foreground = AnchorBrush,
            TextDecorations = System.Windows.TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand,
            NavigateUri = null,
            Focusable = false,
            ToolTip = "レス " + seg.ResNumber + " へ移動",
            Tag = seg.ResNumber,
        };
        link.Click += Link_Click;
        return link;
    }

    private Hyperlink MakeUrlLink(CommentSegment seg)
    {
        var href = seg.NavigateUrl ?? seg.Text;
        var link = new Hyperlink(new Run(seg.Text))
        {
            Foreground = AnchorBrush,
            TextDecorations = System.Windows.TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand,
            NavigateUri = null,
            Focusable = false,
            ToolTip = href,
            Tag = href,
        };
        link.Click += Url_Click;
        return link;
    }

    private double ColumnWidth() =>
        Math.Max(32, ActualWidth > 1 ? ActualWidth - 4 : 280);

    private void ApplyPageWidth()
    {
        var w = ColumnWidth();
        if (Document is not null)
            Document.PageWidth = w;
        foreach (var img in _images)
            img.SetColumnWidth(w);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged)
            ApplyPageWidth();
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
            ZeroDocumentInsets();
            // Tight chat-style body (~1.2×). Auto/NaN uses font default; avoid large gaps.
            var lineH = Math.Max(FontSize * 1.2, FontSize + 2);
            Document.LineHeight = lineH;

            foreach (var block in Document.Blocks)
            {
                block.Margin = new Thickness(0);
                block.Padding = new Thickness(0);
                if (block is BlockUIContainer)
                    continue;
                block.FontFamily = FontFamily;
                block.FontSize = FontSize;
                block.FontWeight = FontWeight;
                block.Foreground = Foreground;
                block.LineHeight = lineH;
                if (block is Paragraph p)
                {
                    p.TextIndent = 0;
                    p.Padding = new Thickness(0);
                    p.Margin = new Thickness(0);
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

    private void ZeroDocumentInsets()
    {
        if (Document is null) return;
        Document.PagePadding = new Thickness(0);
        Document.TextAlignment = TextAlignment.Left;
        Document.IsHyphenationEnabled = false;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Hyperlink { Tag: int num })
            RaiseEvent(new AnchorClickEventArgs(AnchorClickEvent, this, num));
    }

    private void Url_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Hyperlink { Tag: string href })
            CommentImageLoader.OpenInBrowser(href);
    }

    private bool _pendingImageClick;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var img = HitImageView(e);
        if (img is not null)
        {
            // Swallow Down so the RTB doesn't capture the mouse. Open on Up
            // so the same click cannot close a newly shown window.
            _pendingImageClick = true;
            e.Handled = true;
            return;
        }

        _pendingImageClick = false;
        // Keep focus for selection; stop ListBox from treating this as item-select only
        Focus();
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_pendingImageClick)
        {
            _pendingImageClick = false;
            var img = HitImageView(e);
            if (img is not null && RaiseImageClick(img.LoadedImage))
            {
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewMouseLeftButtonUp(e);
    }

    private CommentImageView? HitImageView(System.Windows.Input.MouseEventArgs e)
    {
        var img = FindImageView(e.OriginalSource as DependencyObject);
        if (img is not null)
            return img;
        try { return FindImageView(InputHitTest(e.GetPosition(this)) as DependencyObject); }
        catch { return null; }
    }

    internal bool RaiseImageClick(LoadedCommentImage? image)
    {
        if (image is null)
            return false;
        RaiseEvent(new CommentImageClickEventArgs(ImageClickEvent, this, image));
        return true;
    }

    private static CommentImageView? FindImageView(DependencyObject? start)
    {
        for (var d = start; d is not null;)
        {
            if (d is CommentImageView v)
                return v;
            if (d is Border { Child: CommentImageView img })
                return img;
            d = d is Visual
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
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
