using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DSPlayer.Services;

namespace DSPlayer.Controls;

/// <summary>
/// Fits a loaded comment image into a column. Plays light GIFs. Click bubbles
/// <see cref="PreviewClickEvent"/> so the host can show a lightbox.
/// </summary>
public sealed class CommentImageView : System.Windows.Controls.Image
{
    public static readonly RoutedEvent PreviewClickEvent = EventManager.RegisterRoutedEvent(
        nameof(PreviewClick),
        RoutingStrategy.Bubble,
        typeof(EventHandler<CommentImageClickEventArgs>),
        typeof(CommentImageView));

    private CancellationTokenSource? _loadCts;
    private DispatcherTimer? _gifTimer;
    private LoadedCommentImage? _loaded;
    private int _gifIndex;
    private string _url = "";
    private bool _loadOnLoaded;

    public CommentImageView()
    {
        Stretch = System.Windows.Media.Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = System.Windows.Input.Cursors.Hand;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler<CommentImageClickEventArgs> PreviewClick
    {
        add => AddHandler(PreviewClickEvent, value);
        remove => RemoveHandler(PreviewClickEvent, value);
    }

    public string? SourceUrl => _loaded?.SourceUrl ?? (_url.Length == 0 ? null : _url);
    public LoadedCommentImage? LoadedImage => _loaded;

    /// <summary>Queue a network load when this enters the visual tree (inline comments).</summary>
    public void BeginLoad(string url, double columnWidth)
    {
        StopGif();
        Source = null;
        _loaded = null;
        Width = double.NaN;
        Height = double.NaN;
        _url = url;
        ToolTip = "クリックで拡大";
        Visibility = Visibility.Collapsed;
        MaxHeight = CommentImageLoader.DisplayMaxHeight;
        SetColumnWidth(columnWidth);
        _loadOnLoaded = true;
        if (IsLoaded)
            StartLoad();
    }

    /// <summary>Show an already-decoded image (lightbox). Animates if the payload is a light GIF.</summary>
    public void ShowLoaded(LoadedCommentImage image, double maxWidth, double maxHeight)
    {
        _loadOnLoaded = false;
        _url = image.SourceUrl;
        _loaded = image;
        ToolTip = null;
        StretchDirection = StretchDirection.Both;
        Cursor = System.Windows.Input.Cursors.Arrow;
        SetColumnWidth(maxWidth);
        MaxHeight = Math.Max(32, maxHeight);
        Visibility = Visibility.Visible;
        Play(image);
    }

    public void Clear()
    {
        _loadOnLoaded = false;
        try { _loadCts?.Cancel(); } catch { /* ignore */ }
        StopGif();
        Source = null;
        _loaded = null;
        Width = double.NaN;
        Height = double.NaN;
        Visibility = Visibility.Collapsed;
    }

    public void SetColumnWidth(double width)
    {
        MaxWidth = Math.Max(32, width);
        Width = double.NaN;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadOnLoaded)
            StartLoad();
        else if (_loaded is { IsAnimated: true })
            Play(_loaded);
    }

    private void StartLoad()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _ = RequestAsync(_url, _loadCts.Token);
    }

    private async Task RequestAsync(string url, CancellationToken token)
    {
        LoadedCommentImage? img;
        try
        {
            img = await CommentImageLoader.GetAsync(url).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (token.IsCancellationRequested || img is null)
            return;

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                _loaded = img;
                Visibility = Visibility.Visible;
                Play(img);
            });
        }
        catch
        {
            // control gone
        }
    }

    private void Play(LoadedCommentImage img)
    {
        StopGif();
        LockLayoutSize(img.Preview);
        Source = img.Preview;
        if (!img.IsAnimated || img.Frames is null || img.DelaysMs is null)
            return;

        _gifIndex = 0;
        _gifTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(img.DelaysMs[0]) };
        _gifTimer.Tick += (_, _) =>
        {
            if (_loaded?.Frames is not { Count: > 1 } frames || _loaded.DelaysMs is null)
                return;
            _gifIndex = (_gifIndex + 1) % frames.Count;
            Source = frames[_gifIndex];
            _gifTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(20, _loaded.DelaysMs[_gifIndex % _loaded.DelaysMs.Count]));
        };
        _gifTimer.Start();
    }

    private void LockLayoutSize(BitmapSource src)
    {
        var maxW = MaxWidth;
        if (double.IsNaN(maxW) || maxW <= 1)
            maxW = src.PixelWidth;
        var maxH = MaxHeight;
        if (double.IsNaN(maxH) || maxH <= 1)
            maxH = src.PixelHeight;
        var ar = src.PixelWidth / (double)Math.Max(1, src.PixelHeight);
        // The popup deliberately uses StretchDirection.Both. Its window is sized to
        // the native image (or a reduced large-image fit), so fill that exact area.
        var w = StretchDirection == StretchDirection.Both
            ? maxW
            : Math.Min(maxW, src.PixelWidth);
        var h = w / ar;
        if (h > maxH)
        {
            h = maxH;
            w = h * ar;
        }

        Width = Math.Max(1, w);
        Height = Math.Max(1, h);
    }

    private void StopGif()
    {
        try { _gifTimer?.Stop(); } catch { /* ignore */ }
        _gifTimer = null;
        _gifIndex = 0;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { _loadCts?.Cancel(); } catch { /* ignore */ }
        StopGif();
    }
}

public sealed class CommentImageClickEventArgs : RoutedEventArgs
{
    public CommentImageClickEventArgs(RoutedEvent routedEvent, object source, LoadedCommentImage image)
        : base(routedEvent, source)
    {
        Image = image;
    }

    public LoadedCommentImage Image { get; }
}
