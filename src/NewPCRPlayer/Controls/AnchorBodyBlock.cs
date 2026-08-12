using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NewPCRPlayer.Controls;

/// <summary>
/// Comment body TextBlock that turns &gt;&gt;N / ＞＞N into clickable anchors.
/// Raises a bubbling <see cref="AnchorClickEvent"/> with the target res number.
/// </summary>
public sealed class AnchorBodyBlock : TextBlock
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
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x00, 0xCC));
    private static readonly System.Windows.Media.Brush BodyBrush = System.Windows.Media.Brushes.Black;

    static AnchorBodyBlock()
    {
        if (AnchorBrush.CanFreeze) AnchorBrush.Freeze();
    }

    public AnchorBodyBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        Foreground = BodyBrush;
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

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnchorBodyBlock block)
            block.RebuildInlines(e.NewValue as string);
    }

    private void RebuildInlines(string? text)
    {
        Inlines.Clear();
        if (string.IsNullOrEmpty(text))
            return;

        var matches = AnchorRegex.Matches(text);
        if (matches.Count == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        var idx = 0;
        foreach (Match m in matches)
        {
            if (m.Index > idx)
                Inlines.Add(new Run(text[idx..m.Index]));

            var numStr = m.Groups[2].Value;
            if (!int.TryParse(numStr, out var num) || num <= 0)
            {
                Inlines.Add(new Run(m.Value));
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
                };
                // Store target in Tag
                link.Tag = num;
                link.Click += Link_Click;
                Inlines.Add(link);
            }

            idx = m.Index + m.Length;
        }

        if (idx < text.Length)
            Inlines.Add(new Run(text[idx..]));
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Hyperlink { Tag: int num })
        {
            RaiseEvent(new AnchorClickEventArgs(AnchorClickEvent, this, num));
        }
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
