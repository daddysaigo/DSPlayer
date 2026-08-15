using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DSPlayer.Services.Bbs;

namespace DSPlayer.Controls;

/// <summary>
/// Comment info line: res# and ID as classic blue underlined links.
/// </summary>
public sealed class CommentHeaderBlock : TextBlock
{
    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.Register(nameof(Number), typeof(int), typeof(CommentHeaderBlock),
            new PropertyMetadata(0, OnAnyChanged));

    public static readonly DependencyProperty NameTextProperty =
        DependencyProperty.Register(nameof(NameText), typeof(string), typeof(CommentHeaderBlock),
            new PropertyMetadata(null, OnAnyChanged));

    public static readonly DependencyProperty DateIdProperty =
        DependencyProperty.Register(nameof(DateId), typeof(string), typeof(CommentHeaderBlock),
            new PropertyMetadata(null, OnAnyChanged));

    public static readonly DependencyProperty ShowNumberProperty =
        DependencyProperty.Register(nameof(ShowNumber), typeof(bool), typeof(CommentHeaderBlock),
            new PropertyMetadata(true, OnAnyChanged));

    public static readonly DependencyProperty ShowNameProperty =
        DependencyProperty.Register(nameof(ShowName), typeof(bool), typeof(CommentHeaderBlock),
            new PropertyMetadata(true, OnAnyChanged));

    public static readonly DependencyProperty ShowDateProperty =
        DependencyProperty.Register(nameof(ShowDate), typeof(bool), typeof(CommentHeaderBlock),
            new PropertyMetadata(true, OnAnyChanged));

    public static readonly DependencyProperty IdCountProperty =
        DependencyProperty.Register(nameof(IdCount), typeof(int), typeof(CommentHeaderBlock),
            new PropertyMetadata(0, OnAnyChanged));

    public static readonly RoutedEvent LinkClickEvent = EventManager.RegisterRoutedEvent(
        nameof(LinkClick), RoutingStrategy.Bubble, typeof(EventHandler<HeaderLinkEventArgs>), typeof(CommentHeaderBlock));

    public static readonly RoutedEvent LinkHoverEvent = EventManager.RegisterRoutedEvent(
        nameof(LinkHover), RoutingStrategy.Bubble, typeof(EventHandler<HeaderLinkEventArgs>), typeof(CommentHeaderBlock));

    public static readonly RoutedEvent LinkLeaveEvent = EventManager.RegisterRoutedEvent(
        nameof(LinkLeave), RoutingStrategy.Bubble, typeof(EventHandler<HeaderLinkEventArgs>), typeof(CommentHeaderBlock));

    private static readonly SolidColorBrush LinkBrush = CreateLinkBrush();

    public CommentHeaderBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        Focusable = false;
    }

    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public string? NameText
    {
        get => (string?)GetValue(NameTextProperty);
        set => SetValue(NameTextProperty, value);
    }

    public string? DateId
    {
        get => (string?)GetValue(DateIdProperty);
        set => SetValue(DateIdProperty, value);
    }

    public bool ShowNumber
    {
        get => (bool)GetValue(ShowNumberProperty);
        set => SetValue(ShowNumberProperty, value);
    }

    public bool ShowName
    {
        get => (bool)GetValue(ShowNameProperty);
        set => SetValue(ShowNameProperty, value);
    }

    public bool ShowDate
    {
        get => (bool)GetValue(ShowDateProperty);
        set => SetValue(ShowDateProperty, value);
    }

    public int IdCount
    {
        get => (int)GetValue(IdCountProperty);
        set => SetValue(IdCountProperty, value);
    }

    public event EventHandler<HeaderLinkEventArgs> LinkClick
    {
        add => AddHandler(LinkClickEvent, value);
        remove => RemoveHandler(LinkClickEvent, value);
    }

    public event EventHandler<HeaderLinkEventArgs> LinkHover
    {
        add => AddHandler(LinkHoverEvent, value);
        remove => RemoveHandler(LinkHoverEvent, value);
    }

    public event EventHandler<HeaderLinkEventArgs> LinkLeave
    {
        add => AddHandler(LinkLeaveEvent, value);
        remove => RemoveHandler(LinkLeaveEvent, value);
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommentHeaderBlock b)
            b.Rebuild();
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var first = true;

        if (ShowNumber && Number > 0)
        {
            first = false;
            Inlines.Add(MakeLink(
                Number.ToString(),
                HeaderLinkKind.ResNumber,
                Number,
                posterId: null,
                "クリックで >>" + Number + " を引用"));
        }

        if (ShowName && !string.IsNullOrWhiteSpace(NameText))
        {
            if (!first) Inlines.Add(new Run(" ："));
            first = false;
            Inlines.Add(new Run(NameText));
        }

        var datePart = BbsPosterId.DateWithoutId(DateId);
        var posterId = BbsPosterId.Extract(DateId);

        if (ShowDate && !string.IsNullOrWhiteSpace(datePart))
        {
            if (!first) Inlines.Add(new Run("："));
            first = false;
            Inlines.Add(new Run(datePart));
        }

        if (!string.IsNullOrEmpty(posterId))
        {
            if (!first) Inlines.Add(new Run(" "));
            var count = Math.Max(1, IdCount);
            var (color, bold) = BbsIdCountStyle.ForCount(count);
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            var idLabel = posterId + "(" + count + ")";
            Inlines.Add(new Run("ID:") { Foreground = brush, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
            Inlines.Add(MakeLink(
                idLabel,
                HeaderLinkKind.PosterId,
                Number,
                posterId,
                "ID:" + posterId + "  " + count + "レス",
                brush,
                bold));
        }
    }

    private Hyperlink MakeLink(
        string text,
        HeaderLinkKind kind,
        int resNumber,
        string? posterId,
        string tip,
        SolidColorBrush? brush = null,
        bool bold = false)
    {
        var link = new Hyperlink(new Run(text)
        {
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        })
        {
            Foreground = brush ?? LinkBrush,
            TextDecorations = System.Windows.TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            ToolTip = tip,
            Tag = kind,
        };
        link.Click += (_, e) =>
        {
            e.Handled = true;
            RaiseEvent(new HeaderLinkEventArgs(LinkClickEvent, this, kind, resNumber, posterId));
        };
        link.MouseEnter += (_, _) =>
            RaiseEvent(new HeaderLinkEventArgs(LinkHoverEvent, this, kind, resNumber, posterId));
        link.MouseLeave += (_, _) =>
            RaiseEvent(new HeaderLinkEventArgs(LinkLeaveEvent, this, kind, resNumber, posterId));
        return link;
    }

    private static SolidColorBrush CreateLinkBrush()
    {
        var b = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x55, 0xCC));
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}

public enum HeaderLinkKind
{
    ResNumber,
    PosterId,
}

public sealed class HeaderLinkEventArgs : RoutedEventArgs
{
    public HeaderLinkEventArgs(
        RoutedEvent routedEvent,
        object source,
        HeaderLinkKind kind,
        int resNumber,
        string? posterId)
        : base(routedEvent, source)
    {
        Kind = kind;
        ResNumber = resNumber;
        PosterId = posterId;
    }

    public HeaderLinkKind Kind { get; }
    public int ResNumber { get; }
    public string? PosterId { get; }
}
