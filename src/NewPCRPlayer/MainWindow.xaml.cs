using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NewPCRPlayer.Models;
using NewPCRPlayer.Services;
using NewPCRPlayer.Services.Bbs;
using NewPCRPlayer.Services.Mpv;
using NewPCRPlayer.Services.PeerCast;
using WinForms = System.Windows.Forms;

namespace NewPCRPlayer;

public partial class MainWindow : Window
{
    private readonly LaunchArgs _launchArgs;
    private readonly AppSettings _settings;
    private MpvPlayerHost? _player;
    private bool _playerReady;
    private readonly string _logPath;
    private CancellationTokenSource? _retryCts;
    private int _loadAttempts;
    private const int MaxLoadAttempts = 12;

    private readonly ObservableCollection<CommentItem> _comments = new();
    /// <summary>
    /// Max rows kept in the ListBox (PCRPlayer-like: only recent res matter for live chat).
    /// Full thread is still polled; UI trims the head. Prevents multi-second freezes at start.
    /// </summary>
    private const int MaxDisplayedComments = 120;
    private int _threadResCount;
    private BbsPoller? _bbsPoller;
    private readonly BbsWriter _bbsWriter;
    private readonly PeerCastXmlClient _pcsXml = new();
    private bool _stickToBottom = true;
    private string? _contactUrl;
    private BbsThreadRef? _writeThread;
    private string? _threadTitle;
    private bool _writing;
    private bool _commentVisible = true;
    private bool _threadComboSyncing;

    private PeerCastChannelInfo? _channelInfo;
    private DispatcherTimer? _statsTimer;
    private DispatcherTimer? _channelTimer;
    private DateTime _playStartedUtc = DateTime.UtcNow;
    private bool _isFullscreen;
    private double _preFullscreenWidth;
    private double _preFullscreenHeight;
    private double _preFullscreenLeft;
    private double _preFullscreenTop;
    private bool _userScrollingComments;

    private WindowSizingHook? _sizingHook;
    private double _videoAspect = 16.0 / 9.0;
    private bool _initialAspectApplied;
    private DispatcherTimer? _pointerTimer;
    private bool _lbuttonWasDown;
    /// <summary>True while settings dialog is showing (prevent re-entry).</summary>
    private bool _settingsDialogOpen;
    private const double VideoGripPx = 14; // invisible hit zone inside video edges

    // Video drag: only after move threshold so double-click / click aren't stolen
    private int _pendingDragHt;
    private System.Drawing.Point _pendingDragOrigin;
    private bool _videoDragActive;
    private const int VideoDragThresholdPx = 5;

    // Fullscreen: hide write/status bars; show on bottom hover
    private DispatcherTimer? _fsChromeTimer;
    private bool _fsChromeVisible = true;
    private bool _fsChromePinned;

    // Write-box multi-line: freeze content row (video+comments) in pixels; only window grows down
    private bool _contentRowFrozen;
    private double _frozenContentHeight;

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int VK_LBUTTON = 0x01;
    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    public MainWindow() : this(LaunchArgs.Parse(Array.Empty<string>()))
    {
    }

    public MainWindow(LaunchArgs launchArgs)
    {
        _launchArgs = launchArgs ?? LaunchArgs.Parse(Array.Empty<string>());
        _settings = AppSettings.Load();
        _bbsWriter = new BbsWriter(userAgent: _settings.BbsUserAgent);
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewPCRPlayer",
            "player.log");

        InitializeComponent();
        CommentList.ItemsSource = _comments;
        ApplyCommentPanelSettings();
        ApplyCommentFont();
        ApplySavedWindowPlacement();
        RefreshStatusBar();

        PlayerPanel.MouseWheel += PlayerPanel_MouseWheel;
        PlayerPanel.MouseDoubleClick += (_, _) => ToggleFullscreen();
        PlayerPanel.MouseUp += PlayerPanel_MouseUp;
        // Drag/resize: pointer poll (mpv wid steals MouseDown on the video surface)

        // ContextMenu sits over the video; pointer poll must ignore it (WindowFromPoint also covers this).
        if (ContextMenu is not null)
        {
            ContextMenu.Opened += (_, _) =>
            {
                _lbuttonWasDown = false;
                try { Mouse.OverrideCursor = null; } catch { /* ignore */ }
            };
            ContextMenu.Closed += (_, _) => { _lbuttonWasDown = false; };
        }

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            CopySelectedComments();
            e.Handled = true;
        }));

        WriteLog("---- start ----");
        WriteLog("raw args: " + string.Join(" | ", _launchArgs.RawArgs));
        WriteLog("channel: " + (_launchArgs.ChannelName ?? "(null)"));
        WriteLog("contact: " + (_launchArgs.ContactUrl ?? "(null)"));
        WriteLog("channelId: " + (_launchArgs.ChannelId ?? "(null)"));
        WriteLog("bbs interval=" + _settings.BbsIntervalSeconds + "s ua=" + _settings.BbsUserAgent);

        Loaded += MainWindow_Loaded;
    }

    private void ApplySavedWindowPlacement()
    {
        if (!_settings.HasWindowPlacement)
            return;

        try
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft!.Value;
            Top = _settings.WindowTop!.Value;
            Width = _settings.WindowWidth!.Value;
            Height = _settings.WindowHeight!.Value;
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveWindowPlacement()
    {
        if (_isFullscreen) return;
        if (WindowState != WindowState.Normal) return;
        if (Width < 200 || Height < 150) return;

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
    }

    /*
     * Interaction (no visual frame around video):
     *  - ResizeBorderThickness=0 → BBS / write / info edges do NOT resize the window.
     *  - Pointer poll over VIDEO rect only: edge(14px)→HT* resize, center→HTCAPTION drag.
     *  - WM_SIZING keeps VIDEO area aspect during resize (never SizeChanged Width/Height).
     *  - On first file-loaded, snap window once to video AR.
     */
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _sizingHook = new WindowSizingHook(this)
        {
            VideoAspect = _videoAspect,
            Enabled = true,
            GetVideoRectDip = () => Rect.Empty, // no NCHITTEST grips; poll handles it
            GetSideChromeDip = MeasureSideChromeDip,
            GetBottomChromeDip = MeasureBottomChromeDip,
            GetTopChromeDip = () => 0,
        };
        _sizingHook.Attach();
        WriteLog("WM_SIZING hook attached (video-area AR); video-edge resize via pointer poll");
    }

    private double MeasureSideChromeDip()
    {
        double side = 2; // outer border
        if (_commentVisible)
        {
            var commentW = CommentColumn.Width.IsAbsolute
                ? CommentColumn.Width.Value
                : (CommentColumn.ActualWidth > 0 ? CommentColumn.ActualWidth : _settings.CommentPanelWidth);
            if (commentW < 1) commentW = 340;
            var split = SplitterColumn.ActualWidth > 0 ? SplitterColumn.ActualWidth : 4;
            side += commentW + split;
        }
        return side;
    }

    private double MeasureBottomChromeDip()
    {
        var writeH = WriteBar.ActualHeight > 0 ? WriteBar.ActualHeight : 36;
        var infoH = InfoBar.ActualHeight > 0 ? InfoBar.ActualHeight : 28;
        return writeH + infoH + 2;
    }

    /// <summary>Video host rect in screen pixels (for hit-testing under mpv).</summary>
    private Rect GetVideoScreenRectPx()
    {
        try
        {
            if (VideoHost.ActualWidth < 1 || VideoHost.ActualHeight < 1)
                return Rect.Empty;
            var tl = VideoHost.PointToScreen(new System.Windows.Point(0, 0));
            var br = VideoHost.PointToScreen(new System.Windows.Point(VideoHost.ActualWidth, VideoHost.ActualHeight));
            return new Rect(tl, br);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    /// <summary>
    /// HT* if on video edge (14px), HTCAPTION if video center, 0 if outside video
    /// (BBS / bars → no resize/drag from this path).
    /// </summary>
    private static int HitTestVideo(System.Drawing.Point screen, Rect videoPx)
    {
        if (videoPx.IsEmpty)
            return 0;
        if (screen.X < videoPx.Left || screen.X > videoPx.Right ||
            screen.Y < videoPx.Top || screen.Y > videoPx.Bottom)
            return 0;

        var g = VideoGripPx;
        var left = screen.X - videoPx.Left <= g;
        var right = videoPx.Right - screen.X <= g;
        var top = screen.Y - videoPx.Top <= g;
        var bottom = videoPx.Bottom - screen.Y <= g;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return HTCAPTION;
    }

    private static System.Windows.Input.Cursor? CursorForHit(int ht) => ht switch
    {
        HTLEFT or HTRIGHT => System.Windows.Input.Cursors.SizeWE,
        HTTOP or HTBOTTOM => System.Windows.Input.Cursors.SizeNS,
        HTTOPLEFT or HTBOTTOMRIGHT => System.Windows.Input.Cursors.SizeNWSE,
        HTTOPRIGHT or HTBOTTOMLEFT => System.Windows.Input.Cursors.SizeNESW,
        HTCAPTION => System.Windows.Input.Cursors.Arrow,
        _ => null,
    };

    private void StartPointerInteractionTimer()
    {
        _pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pointerTimer.Tick += (_, _) => PointerInteractionTick();
        _pointerTimer.Start();
    }

    private void PointerInteractionTick()
    {
        if (!IsVisible || WindowState == WindowState.Minimized ||
            _settingsDialogOpen || (ContextMenu?.IsOpen == true) || ThreadPopup.IsOpen)
        {
            ClearPointerCursorState();
            return;
        }

        var screen = WinForms.Control.MousePosition;
        UpdateFullscreenChromeFromPointer(screen);

        // Only interact when the top-level window under the cursor is THIS player.
        // Context menus, settings dialogs, and other popups have their own HWNDs —
        // treating their screen coords as "video" steals left-clicks.
        if (!IsScreenPointOverOurTopLevel(screen.X, screen.Y))
        {
            ClearPointerCursorState();
            return;
        }

        var video = GetVideoScreenRectPx();
        var ht = (_isFullscreen || WindowState == WindowState.Maximized)
            ? 0
            : HitTestVideo(screen, video);

        var cur = CursorForHit(ht);
        if (ht != 0)
            Mouse.OverrideCursor = cur ?? System.Windows.Input.Cursors.Arrow;
        else if (Mouse.OverrideCursor is not null)
            Mouse.OverrideCursor = null;

        var down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        var rising = down && !_lbuttonWasDown;
        var falling = !down && _lbuttonWasDown;
        _lbuttonWasDown = down;

        if (falling)
        {
            _pendingDragHt = 0;
            _videoDragActive = false;
            return;
        }

        if (_isFullscreen || WindowState == WindowState.Maximized)
            return;

        // Press: remember hit, do NOT start drag yet (preserve double-click)
        if (rising && ht != 0)
        {
            _pendingDragHt = ht;
            _pendingDragOrigin = screen;
            _videoDragActive = false;
            return;
        }

        // Move while pressed past threshold → start window drag / resize
        if (down && _pendingDragHt != 0 && !_videoDragActive)
        {
            var dx = screen.X - _pendingDragOrigin.X;
            var dy = screen.Y - _pendingDragOrigin.Y;
            if (dx * dx + dy * dy < VideoDragThresholdPx * VideoDragThresholdPx)
                return;

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                _videoDragActive = true;
                var dragHt = _pendingDragHt;
                _pendingDragHt = 0;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)dragHt, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                WriteLog("pointer interact: " + ex.Message);
            }
        }
    }

    private void ClearPointerCursorState()
    {
        if (Mouse.OverrideCursor is not null)
            Mouse.OverrideCursor = null;
        _lbuttonWasDown = false;
        _pendingDragHt = 0;
        _videoDragActive = false;
    }

    /// <summary>
    /// True when the root owner of the HWND under (x,y) is this window
    /// (includes mpv/WinForms children). False for menus, dialogs, other apps.
    /// </summary>
    private bool IsScreenPointOverOurTopLevel(int screenX, int screenY)
    {
        try
        {
            var our = new WindowInteropHelper(this).Handle;
            if (our == IntPtr.Zero) return false;

            var under = WindowFromPoint(new POINT { X = screenX, Y = screenY });
            if (under == IntPtr.Zero) return false;

            var root = GetAncestor(under, GA_ROOT);
            if (root == IntPtr.Zero) root = under;
            return root == our;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>One-shot window size fit to video AR after playback starts (not SizeChanged loop).</summary>
    private void ApplyInitialAspectLayout()
    {
        if (_initialAspectApplied || _isFullscreen || WindowState != WindowState.Normal)
            return;

        var size = _player?.GetVideoSize();
        if (size is not { W: > 0, H: > 0 })
            return;

        var ar = (double)size.Value.W / size.Value.H;
        if (ar is < 0.2 or > 5)
            return;

        _videoAspect = ar;
        if (_sizingHook is not null)
            _sizingHook.VideoAspect = ar;

        var side = MeasureSideChromeDip();
        var bottom = MeasureBottomChromeDip();

        // Keep current video-column width (or a sensible minimum), derive height from AR
        var videoW = Math.Max(480, ActualWidth - side);
        var videoH = videoW / ar;
        var winW = videoW + side;
        var winH = videoH + bottom;

        // Clamp to work area
        var work = SystemParameters.WorkArea;
        if (winW > work.Width * 0.96)
        {
            winW = work.Width * 0.96;
            videoW = winW - side;
            videoH = videoW / ar;
            winH = videoH + bottom;
        }
        if (winH > work.Height * 0.96)
        {
            winH = work.Height * 0.96;
            videoH = winH - bottom;
            videoW = videoH * ar;
            winW = videoW + side;
        }

        Width = winW;
        Height = winH;
        _initialAspectApplied = true;
        WriteLog($"initial aspect layout {size.Value.W}x{size.Value.H} ar={ar:0.###} → window {winW:0}x{winH:0}");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            InitializePlayer();
            if (_launchArgs.HasStream)
                BeginPlaybackWithRetry();
            else
            {
                StatusText.Text = "ストリームURLがありません";
                WriteLog("no stream url parsed");
            }

            UpdateVolumeText();
            StartTimers();
            StartPointerInteractionTimer();
            await StartBbsAsync().ConfigureAwait(true);
            await RefreshChannelInfoAsync().ConfigureAwait(true);
            RefreshStatusBar();
        }
        catch (Exception ex)
        {
            WriteLog("init error: " + ex);
            StatusText.Text = "初期化エラー: " + ex.Message;
            System.Windows.MessageBox.Show(this, ex.Message + "\n\nログ: " + _logPath,
                "NewPCRPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartTimers()
    {
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) =>
        {
            UpdateVideoAspectFromMpv();
            RefreshStatusBar();
        };
        _statsTimer.Start();

        _channelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _channelTimer.Tick += async (_, _) =>
        {
            await RefreshChannelInfoAsync().ConfigureAwait(true);
            RefreshStatusBar();
        };
        _channelTimer.Start();
    }

    private void UpdateVideoAspectFromMpv()
    {
        var size = _player?.GetVideoSize();
        if (size is not { W: > 0, H: > 0 })
            return;
        var ar = (double)size.Value.W / size.Value.H;
        if (ar is < 0.2 or > 5)
            return;
        if (Math.Abs(ar - _videoAspect) < 0.001)
            return;
        _videoAspect = ar;
        if (_sizingHook is not null)
            _sizingHook.VideoAspect = ar;
    }

    private async Task RefreshChannelInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(_launchArgs.ChannelId))
            return;
        try
        {
            var baseUrl = TryGetPeerCastBase(_launchArgs.PlaybackUrl ?? _launchArgs.StreamUrl);
            var info = await _pcsXml.GetChannelAsync(_launchArgs.ChannelId!, baseUrl).ConfigureAwait(true);
            if (info is not null)
            {
                _channelInfo = info;
                if (string.IsNullOrWhiteSpace(_contactUrl) && !string.IsNullOrWhiteSpace(info.ContactUrl))
                    _contactUrl = info.ContactUrl;
            }
        }
        catch (Exception ex)
        {
            WriteLog("viewxml: " + ex.Message);
        }
    }

    private static string? TryGetPeerCastBase(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl)) return null;
        try { return new Uri(streamUrl).GetLeftPart(UriPartial.Authority); }
        catch { return null; }
    }

    private void ApplyCommentPanelSettings()
    {
        _commentVisible = _settings.CommentPanelVisible;
        MenuCommentVisible.IsChecked = _commentVisible;
        if (_settings.CommentPanelWidth > 120)
            CommentColumn.Width = new GridLength(_settings.CommentPanelWidth);
        SetCommentVisible(_commentVisible, persist: false);
    }

    private void SetCommentVisible(bool visible, bool persist = true)
    {
        _commentVisible = visible;
        MenuCommentVisible.IsChecked = visible;
        if (visible)
        {
            var w = _settings.CommentPanelWidth > 120 ? _settings.CommentPanelWidth : 340;
            CommentColumn.Width = new GridLength(w);
            CommentColumn.MinWidth = 160;
            SplitterColumn.Width = new GridLength(4);
            CommentPanel.Visibility = Visibility.Visible;
            CommentSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (CommentColumn.Width.IsAbsolute && CommentColumn.Width.Value > 120)
                _settings.CommentPanelWidth = CommentColumn.Width.Value;
            CommentColumn.MinWidth = 0;
            CommentColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            CommentPanel.Visibility = Visibility.Collapsed;
            CommentSplitter.Visibility = Visibility.Collapsed;
        }

        if (persist)
        {
            _settings.CommentPanelVisible = visible;
            _settings.Save();
        }
    }

    private void CommentSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (CommentColumn.Width.IsAbsolute && CommentColumn.Width.Value > 120)
        {
            _settings.CommentPanelWidth = CommentColumn.Width.Value;
            _settings.Save();
        }
    }

    private void RefreshStatusBar()
    {
        var left = BuildLeftStatusParts();
        StatusText.Text = left.Count > 0 ? string.Join("  |  ", left) : "NewPCRPlayer";
        var right = BuildRightStatusParts();
        RightStatsText.Text = right.Count > 0 ? string.Join("  ", right) : "";
    }

    /// <summary>
    /// Status text from channel connection info (not user-toggled).
    /// Same idea as original PCRPlayer status/channel fields.
    /// </summary>
    private List<string> BuildLeftStatusParts()
    {
        var parts = new List<string>();
        var ch = _channelInfo;
        var name = FirstNonEmpty(ch?.Name, _launchArgs.ChannelName);
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        var type = FirstNonEmpty(ch?.Type);
        if (!string.IsNullOrWhiteSpace(type)) parts.Add(type!);
        var genre = FirstNonEmpty(ch?.Genre);
        if (!string.IsNullOrWhiteSpace(genre)) parts.Add(genre!);
        var desc = FirstNonEmpty(ch?.Desc);
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add(desc!);
        var comment = FirstNonEmpty(ch?.Comment);
        if (!string.IsNullOrWhiteSpace(comment)) parts.Add(comment!);
        if (ch is not null && ch.Listeners >= 0) parts.Add(ch.Listeners + " listeners");
        if (ch is not null && ch.BitrateKbps > 0) parts.Add(ch.BitrateKbps + "kbps");
        var fps = _player?.GetFps();
        if (fps is > 0.1)
            parts.Add(fps.Value.ToString("0.#", CultureInfo.InvariantCulture) + "fps");
        return parts;
    }

    private List<string> BuildRightStatusParts()
    {
        var parts = new List<string>();
        var size = _player?.GetVideoSize();
        if (size is { W: > 0, H: > 0 })
            parts.Add(size.Value.W + "x" + size.Value.H);
        var pos = _player?.GetTimePos();
        var elapsed = pos is >= 0 ? TimeSpan.FromSeconds(pos.Value) : DateTime.UtcNow - _playStartedUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        parts.Add(FormatDuration(elapsed));
        return parts;
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    // --- BBS ---

    private async Task StartBbsAsync()
    {
        _contactUrl = _launchArgs.ContactUrl ?? _channelInfo?.ContactUrl;
        if (string.IsNullOrWhiteSpace(_contactUrl) && !string.IsNullOrWhiteSpace(_launchArgs.ChannelId))
        {
            try
            {
                _contactUrl = await new PeerCastApiClient()
                    .TryGetContactUrlAsync(_launchArgs.ChannelId!).ConfigureAwait(true);
            }
            catch { /* ignore */ }
        }

        if (string.IsNullOrWhiteSpace(_contactUrl))
        {
            BoardTitleText.Text = "掲示板URLなし";
            SetWriteEnabled(false);
            return;
        }

        var thread = BbsThreadRef.TryParse(_contactUrl);
        if (thread is null || !thread.CanFetch)
        {
            BoardTitleText.Text = "掲示板を解釈できません";
            SetWriteEnabled(false);
            return;
        }

        WriteLog("bbs kind=" + thread.Kind);
        BoardTitleText.Text = thread.Board ?? "掲示板";
        SetWriteEnabled(thread.CanWrite || thread.Kind is BbsBoardKind.Shitaraba or BbsBoardKind.TwochStyle);
        _writeThread = thread.CanWrite ? thread : null;

        _bbsPoller?.Dispose();
        var bbsClient = new BbsClient(
            userAgent: _settings.BbsUserAgent,
            normalizeMessages: _settings.MessageNormalize);
        var intervalSec = Math.Clamp(_settings.BbsIntervalSeconds, 3, 120);
        _bbsPoller = new BbsPoller(bbsClient, TimeSpan.FromSeconds(intervalSec));
        _bbsPoller.Updated += BbsPoller_Updated;
        _bbsPoller.Error += BbsPoller_Error;
        _bbsPoller.SubjectsUpdated += BbsPoller_SubjectsUpdated;
        _bbsPoller.ThreadAutoAdvanced += BbsPoller_ThreadAutoAdvanced;
        _bbsPoller.Start(thread);
    }

    private void SetWriteEnabled(bool enabled)
    {
        WriteBox.IsEnabled = enabled;
        WriteButton.IsEnabled = enabled;
    }

    private void BbsPoller_SubjectsUpdated(object? sender, IReadOnlyList<BbsSubjectEntry> subjects)
    {
        Dispatcher.InvokeAsync(() => PopulateThreadCombo(subjects));
    }

    private void BbsPoller_ThreadAutoAdvanced(object? sender, string newThreadId)
    {
        Dispatcher.InvokeAsync(() =>
        {
            WriteLog("bbs auto-advance → " + newThreadId);
            _comments.Clear();
            // combo will refresh via SubjectsUpdated / next Updated
            if (_bbsPoller?.Subjects is { Count: > 0 } list)
                PopulateThreadCombo(list, selectId: newThreadId);
        });
    }

    private void PopulateThreadCombo(IReadOnlyList<BbsSubjectEntry> subjects, string? selectId = null)
    {
        _threadComboSyncing = true;
        try
        {
            var currentId = selectId
                            ?? _bbsPoller?.ResolvedThread?.ThreadId
                            ?? _bbsPoller?.Client.StickyThreadId;

            ThreadList.Items.Clear();
            ThreadComboItem? selected = null;
            foreach (var s in subjects.Take(80))
            {
                var full = s.ResCount >= 1000;
                var mark = full ? " [満]" : "";
                var label = $"{s.Title} ({s.ResCount}){mark}";
                var item = new ThreadComboItem(s.ThreadId, label, s.ResCount, full);
                ThreadList.Items.Add(item);
                if (currentId is not null &&
                    s.ThreadId.Equals(currentId, StringComparison.Ordinal))
                    selected = item;
            }

            if (selected is not null)
            {
                ThreadList.SelectedItem = selected;
                BoardTitleText.Text = selected.Label;
            }
            else if (ThreadList.Items.Count > 0 && ThreadList.Items[0] is ThreadComboItem first)
            {
                ThreadList.SelectedItem = first;
                BoardTitleText.Text = first.Label;
            }
        }
        finally
        {
            _threadComboSyncing = false;
        }
    }

    private void BoardTitleText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenThreadPopup();
    }

    private void OpenThreadPopup()
    {
        _ = _bbsPoller?.RefreshSubjectsAsync();
        if (ThreadList.Items.Count == 0)
            return;

        ThreadPopup.IsOpen = true;
        ThreadList.Focus();
        if (ThreadList.SelectedItem is not null)
            ThreadList.ScrollIntoView(ThreadList.SelectedItem);
    }

    private void ThreadList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Single click selects and closes (simple UX)
        if (ThreadList.SelectedItem is ThreadComboItem)
        {
            // Selection may not have updated yet on Preview — defer
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (ThreadList.SelectedItem is ThreadComboItem item)
                    ApplyThreadSelection(item);
            }));
        }
    }

    private void ThreadList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ThreadList.SelectedItem is ThreadComboItem item)
            ApplyThreadSelection(item);
    }

    private void ThreadList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ThreadList.SelectedItem is ThreadComboItem item)
        {
            e.Handled = true;
            ApplyThreadSelection(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ThreadPopup.IsOpen = false;
        }
    }

    private void ApplyThreadSelection(ThreadComboItem item)
    {
        ThreadPopup.IsOpen = false;
        BoardTitleText.Text = item.Label;

        if (_threadComboSyncing || _bbsPoller is null)
            return;

        var current = _bbsPoller.ResolvedThread?.ThreadId ?? _bbsPoller.Client.StickyThreadId;
        if (string.Equals(current, item.Id, StringComparison.Ordinal))
            return;

        WriteLog("bbs switch thread → " + item.Id);
        _comments.Clear();
        _bbsPoller.SwitchThread(item.Id);
    }

    private void Menu_SelectThread_Click(object sender, RoutedEventArgs e)
    {
        OpenThreadPopup();
    }

    private void BbsPoller_Updated(object? sender, BbsUpdatedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ApplyCommentUpdate(e);
            _threadResCount = e.AllPosts.Count;

            if (!string.IsNullOrWhiteSpace(e.ThreadTitle))
                _threadTitle = e.ThreadTitle;

            if (e.ResolvedThread is not null)
            {
                _writeThread = e.ResolvedThread;
                SetWriteEnabled(e.ResolvedThread.CanWrite);
                if (!_threadComboSyncing && e.ResolvedThread.ThreadId is { } tid)
                {
                    for (var i = 0; i < ThreadList.Items.Count; i++)
                    {
                        if (ThreadList.Items[i] is ThreadComboItem it &&
                            it.Id.Equals(tid, StringComparison.Ordinal))
                        {
                            _threadComboSyncing = true;
                            ThreadList.SelectedItem = it;
                            if (!string.IsNullOrWhiteSpace(e.ThreadTitle))
                                BoardTitleText.Text = e.ThreadTitle + "  (" + e.AllPosts.Count + ")";
                            else
                                BoardTitleText.Text = it.Label;
                            _threadComboSyncing = false;
                            break;
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(e.ThreadTitle))
            {
                BoardTitleText.Text = e.ThreadTitle + "  (" + e.AllPosts.Count + ")";
            }

            // New res → always jump to bottom (live chat)
            if (e.NewPosts.Count > 0 || e.IsFirstLoad)
            {
                _stickToBottom = true;
                _userScrollingComments = false;
                ScheduleScrollCommentsToEnd();
            }
            else if (_stickToBottom && !_userScrollingComments)
            {
                ScheduleScrollCommentsToEnd();
            }

            if (e.IsFirstLoad)
                WriteLog("bbs loaded " + e.AllPosts.Count + " posts, show last " + _comments.Count);
            else if (e.NewPosts.Count > 0)
                WriteLog("bbs +" + e.NewPosts.Count + " (ui " + _comments.Count + ")");
        });
    }

    /// <summary>
    /// Keep only the latest MaxDisplayedComments in the ListBox.
    /// Full DAT can be ~1000 lines; materializing every row freezes WPF for seconds.
    /// </summary>
    private void ApplyCommentUpdate(BbsUpdatedEventArgs e)
    {
        // First open / empty UI / thread replaced → show only the tail (latest)
        if (e.IsFirstLoad || _comments.Count == 0)
        {
            RebuildCommentsFromTail(e.AllPosts, highlightNew: false);
            return;
        }

        if (e.AllPosts.Count == 0)
        {
            _comments.Clear();
            return;
        }

        // Thread switch or reset (res count dropped, or numbering jumped back)
        var lastUi = _comments[^1].Number;
        var lastAll = e.AllPosts[^1].Number;
        if (lastAll < lastUi || e.AllPosts.Count < _threadResCount && e.AllPosts.Count < lastUi)
        {
            RebuildCommentsFromTail(e.AllPosts, highlightNew: false);
            return;
        }

        if (e.NewPosts.Count == 0)
            return;

        foreach (var c in _comments)
            c.IsNew = false;

        foreach (var p in e.NewPosts)
            _comments.Add(new CommentItem(p, _settings, isNew: true));

        TrimCommentsHead();
    }

    private void RebuildCommentsFromTail(IReadOnlyList<BbsPost> all, bool highlightNew)
    {
        _comments.Clear();
        if (all.Count == 0) return;

        var start = Math.Max(0, all.Count - MaxDisplayedComments);
        for (var i = start; i < all.Count; i++)
            _comments.Add(new CommentItem(all[i], _settings, isNew: highlightNew));
    }

    private void RefreshAllCommentHeaders()
    {
        foreach (var c in _comments)
            c.RefreshHeader(_settings);
    }

    private void TrimCommentsHead()
    {
        while (_comments.Count > MaxDisplayedComments)
            _comments.RemoveAt(0);
    }

    private void BbsPoller_Error(object? sender, Exception e)
    {
        Dispatcher.InvokeAsync(() => WriteLog("bbs error: " + e.Message));
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e) => await TryWriteAsync();

    private async void WriteBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            await TryWriteAsync();
        }
    }

    /// <summary>
    /// Grow write box only on explicit newlines. Single-line stays 28px.
    /// Content row (video+comments) is pixel-frozen; only the window grows downward.
    /// </summary>
    private void WriteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWriteBoxHeight();
    }

    private void UpdateWriteBoxHeight()
    {
        var text = WriteBox.Text ?? "";
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lines++;
        }

        var target = lines <= 1
            ? 28.0
            : Math.Clamp(10.0 + lines * 18.0, 46.0, 120.0);

        var prev = WriteBox.Height;
        if (Math.Abs(prev - target) > 0.5)
            ApplyWriteBoxHeightChange(prev, target);

        WriteButton.Height = 28;
    }

    /// <summary>
    /// True isolation: freeze ContentRow to a fixed pixel height for the whole multi-line
    /// session, then change only Window.Height and WriteBox.Height. No unfreeze mid-typing
    /// (that was the "ガクガク" / plop). Unfreeze only when back to 1 line.
    /// </summary>
    private void ApplyWriteBoxHeightChange(double prevHeight, double targetHeight)
    {
        var delta = targetHeight - prevHeight;
        if (Math.Abs(delta) < 0.5) return;

        if (_isFullscreen || WindowState != WindowState.Normal)
        {
            WriteBox.Height = targetHeight;
            return;
        }

        var hookWas = _sizingHook?.Enabled ?? false;
        if (_sizingHook is not null)
            _sizingHook.Enabled = false;

        try
        {
            // Freeze content (video+comments) once — keep for all subsequent Enter presses
            if (!_contentRowFrozen)
            {
                var h = MainSplitGrid.ActualHeight;
                if (h < 1)
                    h = ContentRow.ActualHeight;
                if (h < 1)
                {
                    WriteBox.Height = targetHeight;
                    return;
                }

                _frozenContentHeight = h;
                ContentRow.Height = new GridLength(_frozenContentHeight, GridUnitType.Pixel);
                _contentRowFrozen = true;
            }
            else
            {
                // Re-assert freeze (guards against other layout code)
                ContentRow.Height = new GridLength(_frozenContentHeight, GridUnitType.Pixel);
            }

            // Set write box + window together; content stays pixel-fixed
            WriteBox.Height = targetHeight;

            var work = SystemParameters.WorkArea;
            var newWindowH = Height + delta;
            if (delta > 0)
                newWindowH = Math.Min(newWindowH, Math.Max(MinHeight, work.Bottom - Top));
            else
                newWindowH = Math.Max(MinHeight, newWindowH);

            if (Math.Abs(newWindowH - Height) >= 0.5)
                Height = newWindowH;

            // Keep content frozen at original size after window change
            ContentRow.Height = new GridLength(_frozenContentHeight, GridUnitType.Pixel);

            // Back to single line → allow normal * layout again
            if (targetHeight <= 28.5)
                UnfreezeContentRow();
        }
        catch (Exception ex)
        {
            WriteLog("writebox layout: " + ex.Message);
            try { WriteBox.Height = targetHeight; } catch { /* ignore */ }
        }
        finally
        {
            if (_sizingHook is not null)
                _sizingHook.Enabled = hookWas;
        }
    }

    private void UnfreezeContentRow()
    {
        if (!_contentRowFrozen) return;
        try
        {
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
            MainSplitGrid.ClearValue(FrameworkElement.HeightProperty);
        }
        catch { /* ignore */ }
        _contentRowFrozen = false;
        _frozenContentHeight = 0;
    }

    private void ClearWriteBox()
    {
        void DoClear()
        {
            WriteBox.IsUndoEnabled = false;
            WriteBox.Text = string.Empty;
            WriteBox.CaretIndex = 0;
            WriteBox.SelectionLength = 0;
            var prev = WriteBox.Height;
            if (Math.Abs(prev - 28) > 0.5)
                ApplyWriteBoxHeightChange(prev, 28);
            else
            {
                WriteBox.Height = 28;
                UnfreezeContentRow();
            }
            WriteButton.Height = 28;
            WriteBox.IsUndoEnabled = true;
        }

        if (Dispatcher.CheckAccess()) DoClear();
        else Dispatcher.Invoke(DoClear);

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (!string.IsNullOrEmpty(WriteBox.Text))
                DoClear();
        }));
    }

    private async Task TryWriteAsync()
    {
        if (_writing) return;
        var thread = _writeThread ?? _bbsPoller?.ResolvedThread;
        if (thread is null || !thread.CanWrite) return;

        var body = (WriteBox.Text ?? "").Trim();
        if (body.Length == 0) return;

        ClearWriteBox();
        // Name/mail are not in settings UI; always sage anonymous post for now.
        var name = "";
        var mail = "sage";

        _writing = true;
        WriteButton.IsEnabled = false;
        WriteLog("bbs write begin len=" + body.Length);

        try
        {
            var result = await _bbsWriter.PostAsync(thread, name, mail, body).ConfigureAwait(true);
            WriteLog("bbs write: " + result.Success + " " + result.Message);

            if (result.Success)
            {
                ClearWriteBox();
                _stickToBottom = true;
                await Task.Delay(800).ConfigureAwait(true);
                _bbsPoller?.RequestRefresh();
            }
            else if (result.NeedsConfirmRetry)
            {
                await Task.Delay(400).ConfigureAwait(true);
                var retry = await _bbsWriter.PostAsync(thread, name, mail, body).ConfigureAwait(true);
                WriteLog("bbs write retry: " + retry.Success + " " + retry.Message);
                if (retry.Success)
                {
                    ClearWriteBox();
                    _stickToBottom = true;
                    await Task.Delay(800).ConfigureAwait(true);
                    _bbsPoller?.RequestRefresh();
                }
                else
                {
                    WriteBox.Text = body;
                    WriteBox.CaretIndex = WriteBox.Text.Length;
                }
            }
            else
            {
                WriteBox.Text = body;
                WriteBox.CaretIndex = WriteBox.Text.Length;
            }
        }
        catch (Exception ex)
        {
            WriteLog("bbs write error: " + ex.Message);
            WriteBox.Text = body;
            WriteBox.CaretIndex = WriteBox.Text.Length;
        }
        finally
        {
            _writing = false;
            WriteButton.IsEnabled = _writeThread?.CanWrite == true ||
                                    _bbsPoller?.ResolvedThread?.CanWrite == true;
        }
    }

    private void CommentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer sv) return;

        // Viewport resized while stuck to bottom → keep latest pinned (grow/shrink from top)
        if (e.ViewportHeightChange != 0 && _stickToBottom && !_userScrollingComments)
        {
            ScrollCommentsToEndCore();
            return;
        }

        if (sv.ScrollableHeight <= 0)
        {
            _stickToBottom = true;
            _userScrollingComments = false;
            return;
        }

        // Near bottom = stick (pixel tolerance)
        _stickToBottom = sv.ScrollableHeight - sv.VerticalOffset < 40;
        _userScrollingComments = !_stickToBottom;
    }

    private void CommentList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;
        if (!_stickToBottom || _userScrollingComments) return;
        ScheduleScrollCommentsToEnd();
    }

    private void ScheduleScrollCommentsToEnd()
    {
        // Virtualizing list needs layout pass(es) before ScrollIntoView works on last item
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScrollCommentsToEndCore));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ScrollCommentsToEndCore));
    }

    private void ScrollCommentsToEndCore()
    {
        if (CommentList.Items.Count == 0) return;
        try
        {
            var last = CommentList.Items[^1];
            CommentList.ScrollIntoView(last);
            var sv = FindDescendantScrollViewer(CommentList);
            if (sv is not null)
                sv.ScrollToEnd();
        }
        catch
        {
            // ignore layout races
        }
    }

    private void ScrollCommentsToEnd() => ScrollCommentsToEndCore();

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void CopySelectedComments()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox tb && tb.SelectionLength > 0)
        {
            try { System.Windows.Clipboard.SetText(tb.SelectedText); return; }
            catch { /* ignore */ }
        }

        if (CommentList.SelectedItems.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var item in CommentList.SelectedItems)
        {
            if (item is CommentItem c)
            {
                sb.AppendLine(c.DisplayHeader);
                sb.AppendLine(c.BodyText);
                sb.AppendLine();
            }
            else if (item is BbsPost p)
            {
                sb.AppendLine(p.DisplayHeader);
                sb.AppendLine(p.BodyText);
                sb.AppendLine();
            }
        }
        try { System.Windows.Clipboard.SetText(sb.ToString().TrimEnd()); }
        catch (Exception ex) { WriteLog("clipboard: " + ex.Message); }
    }

    // --- Player ---

    private void InitializePlayer()
    {
        if (_playerReady) return;
        var hwnd = PlayerPanel.Handle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("HWND を取得できませんでした。");

        WriteLog("hwnd=" + hwnd.ToInt64().ToString("X"));
        _player = new MpvPlayerHost();
        _player.Log += (_, msg) => WriteLog(msg);
        _player.FileLoaded += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            _retryCts?.Cancel();
            _playStartedUtc = DateTime.UtcNow;
            _initialAspectApplied = false;
            UpdateVideoAspectFromMpv();
            // Defer one frame so video-params are stable
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                UpdateVideoAspectFromMpv();
                ApplyInitialAspectLayout();
                // Retry shortly — some streams report size a bit later
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (!_initialAspectApplied)
                        ApplyInitialAspectLayout();
                }));
            }));
            RefreshStatusBar();
            WriteLog("file-loaded ok");
        });
        _player.EndFile += (_, args) => Dispatcher.InvokeAsync(() =>
        {
            WriteLog("end-file: " + args.Reason + " " + args.ErrorMessage);
            if (args.IsError && _loadAttempts < MaxLoadAttempts)
            {
                ScheduleRetry();
                return;
            }
            RefreshStatusBar();
        });

        _player.Initialize(hwnd);
        _playerReady = true;
    }

    private void BeginPlaybackWithRetry()
    {
        _retryCts?.Cancel();
        _retryCts = new CancellationTokenSource();
        _loadAttempts = 0;
        _initialAspectApplied = false;
        TryLoadOnce();
    }

    private void TryLoadOnce()
    {
        if (_player is null || !_launchArgs.HasStream) return;
        _loadAttempts++;
        var playUrl = ResolveAttemptUrl(_loadAttempts);
        WriteLog("loading attempt " + _loadAttempts + ": " + playUrl);
        try { _player.Load(playUrl); }
        catch (Exception ex)
        {
            WriteLog("load threw: " + ex.Message);
            if (_loadAttempts < MaxLoadAttempts) ScheduleRetry();
        }
    }

    private string ResolveAttemptUrl(int attempt)
    {
        var stream = _launchArgs.PlaybackUrl ?? _launchArgs.StreamUrl!;
        var original = _launchArgs.StreamUrl ?? stream;
        if (attempt >= 9 && !string.Equals(stream, original, StringComparison.OrdinalIgnoreCase))
            return original;
        return stream;
    }

    private void ScheduleRetry()
    {
        var cts = _retryCts;
        if (cts is null || cts.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Min(3000, 800 + _loadAttempts * 200), cts.Token);
                await Dispatcher.InvokeAsync(TryLoadOnce);
            }
            catch (TaskCanceledException) { }
        });
    }

    private void PlayerPanel_MouseWheel(object? sender, WinForms.MouseEventArgs e)
    {
        if (_player is null) return;
        _player.AdjustVolume(e.Delta / 120.0 * 5);
        UpdateVolumeText();
    }

    private void PlayerPanel_MouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        // Cancel pending drag so a click doesn't become a deferred move
        _pendingDragHt = 0;
        _videoDragActive = false;

        if (e.Button == WinForms.MouseButtons.Right && ContextMenu is not null)
        {
            ContextMenu.PlacementTarget = this;
            ContextMenu.IsOpen = true;
        }
    }

    private void UpdateVolumeText() =>
        VolumeText.Text = "音量 " + (_player?.Volume ?? 0).ToString("0");

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox { IsReadOnly: true } ||
                e.OriginalSource is ListBoxItem ||
                ReferenceEquals(Keyboard.FocusedElement, CommentList))
            {
                CopySelectedComments();
                e.Handled = true;
                return;
            }
        }

        if (e.OriginalSource is System.Windows.Controls.TextBox { IsReadOnly: false })
            return;

        switch (e.Key)
        {
            case Key.Space:
                _player?.TogglePause();
                e.Handled = true;
                break;
            case Key.Up:
                _player?.AdjustVolume(5);
                UpdateVolumeText();
                e.Handled = true;
                break;
            case Key.Down:
                _player?.AdjustVolume(-5);
                UpdateVolumeText();
                e.Handled = true;
                break;
            case Key.F11:
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape when _isFullscreen:
                ExitFullscreen();
                e.Handled = true;
                break;
            case Key.B when Keyboard.Modifiers == ModifierKeys.Control:
                OpenPcrBrowser();
                e.Handled = true;
                break;
        }
    }

    private void Menu_Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Menu_Pause_Click(object sender, RoutedEventArgs e) => _player?.TogglePause();
    private void Menu_CommentVisible_Click(object sender, RoutedEventArgs e) =>
        SetCommentVisible(MenuCommentVisible.IsChecked == true);
    private void Menu_ScrollBottom_Click(object sender, RoutedEventArgs e)
    {
        _stickToBottom = true;
        _userScrollingComments = false;
        ScrollCommentsToEnd();
    }
    private void Menu_CopyComment_Click(object sender, RoutedEventArgs e) => CopySelectedComments();
    private void Menu_OpenPcrBrowser_Click(object sender, RoutedEventArgs e) => OpenPcrBrowser();

    private void OpenPcrBrowser()
    {
        var url = _writeThread?.HtmlUrl ?? _contactUrl ?? _launchArgs.ContactUrl ?? _channelInfo?.ContactUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            System.Windows.MessageBox.Show(this, "掲示板 URL がありません。", "PCRBrowser",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            WriteLog("PCRBrowser: " + url);
            PcrBrowserLauncher.Open(url, _settings.PcrBrowserPath);
        }
        catch (Exception ex)
        {
            WriteLog("PCRBrowser fail: " + ex.Message);
            System.Windows.MessageBox.Show(this, ex.Message, "PCRBrowser",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Menu_Settings_Click(object sender, RoutedEventArgs e)
    {
        // Defer until after ContextMenu fully closes (same click must not race the menu).
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(OpenSettingsDialog));
    }

    private void OpenSettingsDialog()
    {
        if (_settingsDialogOpen)
        {
            WriteLog("settings: already open, ignore");
            return;
        }

        _settingsDialogOpen = true;
        ClearPointerCursorState();

        try
        {
            var dlg = new SettingsWindow(_settings)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
            };

            var ok = dlg.ShowDialog() == true;
            WriteLog("settings closed ok=" + ok);
            if (ok)
            {
                _settings.Save();
                ApplySettingsLive();
            }
        }
        catch (Exception ex)
        {
            WriteLog("settings dialog: " + ex);
            System.Windows.MessageBox.Show(
                "設定画面を開けませんでした:\n" + ex.Message,
                "NewPCRPlayer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _settingsDialogOpen = false;
            ClearPointerCursorState();
            // Don't Activate() aggressively — it can fight the next menu open.
        }
    }

    /// <summary>Apply settings that can take effect without full process restart.</summary>
    private void ApplySettingsLive()
    {
        ApplyCommentFont();
        RefreshAllCommentHeaders();
        RefreshStatusBar();
        WriteLog("settings applied: header=" + _settings.CommentHeaderFontFamily +
                 "/" + _settings.CommentHeaderFontSize +
                 " body=" + _settings.CommentBodyFontFamily +
                 "/" + _settings.CommentBodyFontSize +
                 " interval=" + _settings.BbsIntervalSeconds +
                 " normalize=" + _settings.MessageNormalize);

        // Restart BBS poller so interval / message normalize take effect immediately.
        if (!string.IsNullOrWhiteSpace(_contactUrl) || _launchArgs.HasContact)
        {
            _ = RestartBbsAfterSettingsAsync();
        }
    }

    private void ApplyCommentFont()
    {
        try
        {
            var headerFamily = new System.Windows.Media.FontFamily(
                string.IsNullOrWhiteSpace(_settings.CommentHeaderFontFamily)
                    ? "Meiryo UI"
                    : _settings.CommentHeaderFontFamily.Trim());
            var bodyFamily = new System.Windows.Media.FontFamily(
                string.IsNullOrWhiteSpace(_settings.CommentBodyFontFamily)
                    ? "Meiryo UI"
                    : _settings.CommentBodyFontFamily.Trim());
            var headerSize = Math.Clamp(_settings.CommentHeaderFontSize, 8, 36);
            var bodySize = Math.Clamp(_settings.CommentBodyFontSize, 8, 36);

            // DynamicResource keys used by comment ItemTemplate (A/B fonts)
            CommentList.Resources["HeaderFontFamily"] = headerFamily;
            CommentList.Resources["HeaderFontSize"] = headerSize;
            CommentList.Resources["BodyFontFamily"] = bodyFamily;
            CommentList.Resources["BodyFontSize"] = bodySize;

            CommentList.FontFamily = bodyFamily;
            CommentList.FontSize = bodySize;
            // WriteBox のフォント/高さロジックは触らない（改行で伸びる仕様を維持）
        }
        catch (Exception ex)
        {
            WriteLog("font apply: " + ex.Message);
        }
    }

    private async Task RestartBbsAfterSettingsAsync()
    {
        try
        {
            await StartBbsAsync().ConfigureAwait(true);
            WriteLog("bbs restarted after settings");
        }
        catch (Exception ex)
        {
            WriteLog("bbs restart after settings: " + ex.Message);
        }
    }

    private void Menu_OpenBoard_Click(object sender, RoutedEventArgs e)
    {
        var url = _writeThread?.HtmlUrl ?? _contactUrl ?? _launchArgs.ContactUrl ?? _channelInfo?.ContactUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { WriteLog("open board: " + ex.Message); }
    }

    private void Menu_OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch { /* ignore */ }
    }

    private void Menu_Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        // Multi-line write freeze is window-layout specific — clear before maximize
        UnfreezeContentRow();

        _preFullscreenWidth = Width;
        _preFullscreenHeight = Height;
        _preFullscreenLeft = Left;
        _preFullscreenTop = Top;
        if (_sizingHook is not null) _sizingHook.Enabled = false;
        WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
        SetFullscreenChromeVisible(false);
        StartFsChromeWatch();
    }

    private void ExitFullscreen()
    {
        StopFsChromeWatch();
        SetFullscreenChromeVisible(true);
        WindowState = WindowState.Normal;
        if (_preFullscreenWidth > 0) Width = _preFullscreenWidth;
        if (_preFullscreenHeight > 0) Height = _preFullscreenHeight;
        Left = _preFullscreenLeft;
        Top = _preFullscreenTop;
        _isFullscreen = false;
        if (_sizingHook is not null) _sizingHook.Enabled = true;
        // Re-sync write box vs window if multi-line text remains
        if (WriteBox.Height > 28.5)
            ApplyWriteBoxHeightChange(28, WriteBox.Height);
    }

    private void StartFsChromeWatch()
    {
        _fsChromeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _fsChromeTimer.Tick -= FsChromeTimer_Tick;
        _fsChromeTimer.Tick += FsChromeTimer_Tick;

        WriteBar.MouseEnter -= FsChrome_MouseEnter;
        WriteBar.MouseLeave -= FsChrome_MouseLeave;
        InfoBar.MouseEnter -= FsChrome_MouseEnter;
        InfoBar.MouseLeave -= FsChrome_MouseLeave;
        WriteBar.MouseEnter += FsChrome_MouseEnter;
        WriteBar.MouseLeave += FsChrome_MouseLeave;
        InfoBar.MouseEnter += FsChrome_MouseEnter;
        InfoBar.MouseLeave += FsChrome_MouseLeave;
    }

    private void StopFsChromeWatch()
    {
        try { _fsChromeTimer?.Stop(); } catch { /* ignore */ }
        WriteBar.MouseEnter -= FsChrome_MouseEnter;
        WriteBar.MouseLeave -= FsChrome_MouseLeave;
        InfoBar.MouseEnter -= FsChrome_MouseEnter;
        InfoBar.MouseLeave -= FsChrome_MouseLeave;
        _fsChromePinned = false;
    }

    private void FsChrome_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        _fsChromePinned = true;
        SetFullscreenChromeVisible(true);
        try { _fsChromeTimer?.Stop(); } catch { /* ignore */ }
    }

    private void FsChrome_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        _fsChromePinned = false;
        _fsChromeTimer?.Stop();
        _fsChromeTimer?.Start();
    }

    private void FsChromeTimer_Tick(object? sender, EventArgs e)
    {
        try { _fsChromeTimer?.Stop(); } catch { /* ignore */ }
        if (!_isFullscreen || _fsChromePinned) return;
        SetFullscreenChromeVisible(false);
    }

    private void SetFullscreenChromeVisible(bool visible)
    {
        var vis = visible ? Visibility.Visible : Visibility.Collapsed;
        WriteBar.Visibility = vis;
        InfoBar.Visibility = vis;
        _fsChromeVisible = visible;
    }

    /// <summary>Fullscreen: show write/status bars when pointer is near the bottom edge.</summary>
    private void UpdateFullscreenChromeFromPointer(System.Drawing.Point screen)
    {
        if (!_isFullscreen || _fsChromePinned) return;
        try
        {
            var tl = PointToScreen(new System.Windows.Point(0, 0));
            var br = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
            var bottomZone = br.Y - 72;
            if (screen.Y >= bottomZone && screen.X >= tl.X && screen.X <= br.X)
            {
                if (!_fsChromeVisible)
                    SetFullscreenChromeVisible(true);
                _fsChromeTimer?.Stop();
                _fsChromeTimer?.Start();
            }
        }
        catch
        {
            // ignore
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        WriteLog("---- close ----");
        try { _statsTimer?.Stop(); } catch { }
        try { _channelTimer?.Stop(); } catch { }
        try { _pointerTimer?.Stop(); } catch { }
        try { _fsChromeTimer?.Stop(); } catch { }
        try { Mouse.OverrideCursor = null; } catch { }
        try { _sizingHook?.Dispose(); } catch { }

        if (_commentVisible && CommentColumn.Width.IsAbsolute && CommentColumn.Width.Value > 120)
            _settings.CommentPanelWidth = CommentColumn.Width.Value;
        _settings.CommentPanelVisible = _commentVisible;
        SaveWindowPlacement();
        _settings.Save();

        try { _retryCts?.Cancel(); } catch { }
        try { _bbsPoller?.Dispose(); } catch { }
        try { _player?.Stop(); } catch { }
        try { _bbsWriter.Dispose(); } catch { }

        _player?.Dispose();
        _player = null;
        _bbsPoller = null;
        _retryCts?.Dispose();
    }

    private void WriteLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch { }
        Debug.WriteLine("[NewPCRPlayer] " + message);
    }

    private sealed class ThreadComboItem
    {
        private static readonly System.Windows.Media.Brush OpenBrush =
            Freeze(System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00));   // 未満 = 黒
        private static readonly System.Windows.Media.Brush FullBrush =
            Freeze(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));   // 満了 = 灰
        private static readonly System.Windows.Media.Brush NearFullBrush =
            Freeze(System.Windows.Media.Color.FromRgb(0x66, 0x44, 0x00)); // 800+ = 濃い茶

        public ThreadComboItem(string id, string label, int resCount, bool isFull)
        {
            Id = id;
            Label = label;
            ResCount = resCount;
            IsFull = isFull;
        }

        public string Id { get; }
        public string Label { get; }
        public int ResCount { get; }
        public bool IsFull { get; }

        public System.Windows.Media.Brush LabelBrush =>
            IsFull ? FullBrush :
            ResCount >= 800 ? NearFullBrush :
            OpenBrush;

        public FontWeight LabelWeight => IsFull ? FontWeights.Normal : FontWeights.SemiBold;

        public override string ToString() => Label;

        private static System.Windows.Media.Brush Freeze(System.Windows.Media.Color c)
        {
            var b = new SolidColorBrush(c);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
    }
}
