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
using NewPCRPlayer.Controls;
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
    private bool _closing;
    private readonly string _logPath;
    private CancellationTokenSource? _retryCts;
    private int _loadAttempts;
    private int _reconnectGeneration;
    /// <summary>Soft cap for initial burst; live streams keep retrying beyond this with longer delay.</summary>
    private const int MaxLoadAttempts = 12;
    /// <summary>True while a scheduled retry delay is outstanding (stall watchdog must not pile on).</summary>
    private bool _reconnectPending;
    /// <summary>When true, end-file from our own stop must not schedule another retry.</summary>
    private bool _suppressEndFileRetry;
    private double? _lastProgressTimePos;
    private DateTime _lastPlaybackProgressUtc = DateTime.UtcNow;
    /// <summary>Seconds without packet/time progress before forcing reconnect (live).</summary>
    private const double StallReconnectSeconds = 8;
    /// <summary>True after at least one file-loaded (stall watchdog only for live that was playing).</summary>
    private bool _everStartedPlayback;

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

    // Fullscreen: float write/status over video (owned WinForms HWND) — show/hide never resizes layout
    private bool _fsChromeVisible;
    private bool _fsChromeFloating;
    private FsChromeOverlay? _fsChromeOverlay;

    // Write-box multi-line: freeze content row (video+comments) in pixels; only window grows down
    private bool _contentRowFrozen;
    private double _frozenContentHeight;

    /// <summary>WinForms min/max/close on the video panel (over mpv, not a top-level window).</summary>
    private VideoChromeOverlay? _videoChrome;

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
        // Bubbling >>N clicks from AnchorBodyBlock inside the item template
        CommentList.AddHandler(
            AnchorBodyBlock.AnchorClickEvent,
            new EventHandler<AnchorClickEventArgs>(CommentList_AnchorClick));
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
        StateChanged += (_, _) => UpdateMaximizeButtonGlyph();
    }

    private void EnsureVideoChromeOverlay()
    {
        if (_videoChrome is not null) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Handle not ready — retry after source init
            SourceInitialized += (_, _) =>
            {
                if (_videoChrome is null)
                    EnsureVideoChromeOverlay();
            };
            return;
        }

        _videoChrome = new VideoChromeOverlay();
        _videoChrome.MinimizeClick += (_, _) =>
        {
            WindowState = WindowState.Minimized;
        };
        _videoChrome.MaximizeClick += (_, _) =>
        {
            if (WindowState == WindowState.Maximized && !_isFullscreen)
                WindowState = WindowState.Normal;
            else if (_isFullscreen)
                ExitFullscreen();
            else
                WindowState = WindowState.Maximized;
            UpdateMaximizeButtonGlyph();
        };
        _videoChrome.CloseClick += (_, _) => Close();

        // Owned tool window: follows parent, paints ABOVE HwndHost/mpv (airspace fix)
        _videoChrome.Attach(new Win32WindowHandle(hwnd), PlayerPanel);
        UpdateMaximizeButtonGlyph();
        WriteLog("video chrome overlay attached (owned tool window over video)");
    }

    /// <summary>
    /// Returns true when pointer is in the video top-right chrome hot zone
    /// (or over the visible overlay). Resize hit-test must not run in this zone.
    /// </summary>
    private bool TryGetVideoChromeClientPoint(System.Drawing.Point screen, out System.Drawing.Point client)
    {
        client = default;
        if (_videoChrome is null || PlayerPanel.IsDisposed) return false;
        try
        {
            client = PlayerPanel.PointToClient(screen);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPointerInVideoChromeZone(System.Drawing.Point screen)
    {
        if (!TryGetVideoChromeClientPoint(screen, out var client))
            return false;
        var size = PlayerPanel.ClientSize;
        if (VideoChromeOverlay.IsInHotZone(client, size))
            return true;
        return _videoChrome!.Visible && _videoChrome.IsMouseOverChrome(client);
    }

    private void UpdateVideoChromeHover(System.Drawing.Point screen)
    {
        if (_videoChrome is null || PlayerPanel.IsDisposed) return;

        try
        {
            if (!TryGetVideoChromeClientPoint(screen, out var client))
            {
                _videoChrome.HideChrome();
                return;
            }

            var size = PlayerPanel.ClientSize;
            var overChrome = _videoChrome.Visible && _videoChrome.IsMouseOverChrome(client);
            var inHot = VideoChromeOverlay.IsInHotZone(client, size);

            if (inHot || overChrome)
            {
                _videoChrome.ShowChrome();
                _videoChrome.RaiseAboveOwner();
            }
            else
            {
                _videoChrome.HideChrome();
            }
        }
        catch
        {
            // ignore
        }
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
            GetTopChromeDip = () => 0, // overlay does not consume layout space
        };
        _sizingHook.Attach();
        WriteLog("WM_SIZING hook attached (video-area AR); video-edge resize via pointer poll");
    }

    private double MeasureSideChromeDip()
    {
        double side = 0;
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
        // FS float chrome overlays video — does not consume layout height
        if (_isFullscreen && _fsChromeFloating)
            return 0;

        var writeH = WriteBar.Visibility == Visibility.Visible
            ? (WriteBar.ActualHeight > 0 ? WriteBar.ActualHeight : 36)
            : 0;
        var infoH = InfoBar.Visibility == Visibility.Visible
            ? (InfoBar.ActualHeight > 0 ? InfoBar.ActualHeight : 28)
            : 0;
        return writeH + infoH;
    }

    private void UpdateMaximizeButtonGlyph()
    {
        var restored = WindowState == WindowState.Maximized || _isFullscreen;
        _videoChrome?.SetMaximizedGlyph(restored);
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
        UpdateVideoChromeHover(screen);

        // --- Chrome hot zone wins over resize / drag (本家: 右上は矢印＋ボタン) ---
        if (IsPointerInVideoChromeZone(screen))
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
            _pendingDragHt = 0;
            _videoDragActive = false;
            // Still track button state so we don't false-trigger drag later
            var downChrome = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            _lbuttonWasDown = downChrome;
            return;
        }

        // Only interact when the top-level window under the cursor is THIS player.
        if (!IsScreenPointOverOurTopLevel(screen.X, screen.Y))
        {
            ClearPointerCursorState();
            return;
        }

        var video = GetVideoScreenRectPx();
        // Fullscreen / maximized: no edge-resize path
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
        // ~1s like original PCRPlayer status refresh (bitrate / play time)
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) =>
        {
            if (_closing) return;
            UpdateVideoAspectFromMpv();
            CheckLivePlaybackHealth();
            RefreshStatusBar();
        };
        _statsTimer.Start();

        _channelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _channelTimer.Tick += async (_, _) =>
        {
            if (_closing) return;
            await RefreshChannelInfoAsync().ConfigureAwait(true);
            if (_closing) return;
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
    /// Status text: channel metadata + <b>actual received</b> bitrate (not channel.xml advertised rate).
    /// Bitrate refreshes every stats tick (~1s) so a frozen stream shows 0kbps quickly.
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

        // Received bitrate (packet flow), not channel.xml BitrateKbps
        if (_launchArgs.HasStream && _player is not null)
        {
            var recv = _player.GetReceivedBitrateKbps();
            if (recv is > 0)
                parts.Add(recv.Value + "kbps");
            else if (!_reconnectPending)
                parts.Add("0kbps");
            // while reconnecting, left status often already says 再接続 — skip noisy 0kbps
        }

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
            // FS float host sizes to content and sits on bottom — re-pin after multi-line grow
            if (_fsChromeFloating)
                _fsChromeOverlay?.SyncToMonitor();
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

    /// <summary>Phase 6b: >>N in comment body → jump to that res in the list.</summary>
    private void CommentList_AnchorClick(object? sender, AnchorClickEventArgs e)
    {
        e.Handled = true;
        JumpToResNumber(e.ResNumber);
    }

    private void JumpToResNumber(int resNumber)
    {
        if (resNumber <= 0 || _comments.Count == 0)
            return;

        CommentItem? target = null;
        foreach (var c in _comments)
        {
            if (c.Number == resNumber)
            {
                target = c;
                break;
            }
        }

        if (target is null)
        {
            // Not in the displayed tail (only latest N kept)
            WriteLog("anchor >>" + resNumber + " not in displayed comments");
            StatusText.Text = ">>" + resNumber + " は表示範囲外です";
            return;
        }

        try
        {
            _userScrollingComments = true;
            _stickToBottom = false;
            CommentList.SelectedItem = target;
            CommentList.ScrollIntoView(target);
            // Brief visual pulse via IsNew
            var prevNew = target.IsNew;
            target.IsNew = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try { CommentList.ScrollIntoView(target); } catch { /* ignore */ }
            }));
            _ = Task.Run(async () =>
            {
                await Task.Delay(1200).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    // Don't clear if a real new-post batch set it again
                    if (ReferenceEquals(CommentList.SelectedItem, target))
                        target.IsNew = prevNew;
                });
            });
            WriteLog("anchor jump >>" + resNumber);
        }
        catch (Exception ex)
        {
            WriteLog("anchor jump: " + ex.Message);
        }
    }

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
        _player.FileLoaded += OnPlayerFileLoaded;
        _player.EndFile += OnPlayerEndFile;

        _player.Initialize(hwnd);
        _playerReady = true;
        // Owned overlay (not child of wid panel — mpv D3D covers GDI siblings)
        EnsureVideoChromeOverlay();
        ScheduleVideoChromeZOrderBoost();
    }

    private void OnPlayerFileLoaded(object? sender, EventArgs e)
    {
        if (_closing) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_closing) return;
            // Successful play: reset burst counter so later drops reconnect cleanly
            _loadAttempts = 0;
            _reconnectPending = false;
            _everStartedPlayback = true;
            _reconnectGeneration++;
            try { _retryCts?.Cancel(); } catch { /* ignore */ }
            _retryCts = new CancellationTokenSource();
            _playStartedUtc = DateTime.UtcNow;
            _lastPlaybackProgressUtc = DateTime.UtcNow;
            _lastProgressTimePos = null;
            _initialAspectApplied = false;
            UpdateVideoAspectFromMpv();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_closing) return;
                UpdateVideoAspectFromMpv();
                ApplyInitialAspectLayout();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (!_closing && !_initialAspectApplied)
                        ApplyInitialAspectLayout();
                }));
            }));
            ScheduleVideoChromeZOrderBoost();
            StatusText.Text = "再生中";
            RefreshStatusBar();
            WriteLog("file-loaded ok");
        });
    }

    private void OnPlayerEndFile(object? sender, EndFileEventArgs args)
    {
        if (_closing) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_closing) return;
            WriteLog("end-file: " + args.Reason + " " + (args.ErrorMessage ?? ""));
            // quit = intentional teardown
            if (string.Equals(args.Reason, "quit", StringComparison.OrdinalIgnoreCase))
                return;
            // Our ForceReconnect stop — retry already scheduled
            if (_suppressEndFileRetry)
                return;

            // Live PeerCast: any natural end (eof/error/stop/redirect/…) → reconnect
            if (_launchArgs.HasStream)
            {
                StatusText.Text = "切断 — 再接続します…";
                ScheduleRetry(liveContinuous: true);
                return;
            }

            RefreshStatusBar();
        });
    }

    /// <summary>
    /// PeerCast often freezes the last frame without a clean end-file (half-open HTTP).
    /// If time-pos / received bitrate stop advancing, force app-level reconnect.
    /// </summary>
    private void CheckLivePlaybackHealth()
    {
        if (_closing || _player is null || !_launchArgs.HasStream) return;
        if (_reconnectPending || _suppressEndFileRetry) return;
        // Only watch stalls after we have actually played once (initial connect uses end-file / load errors)
        if (!_everStartedPlayback) return;

        try
        {
            // User pause: do not treat as disconnect
            var pauseProp = _player.GetProperty("pause");
            if (string.Equals(pauseProp, "yes", StringComparison.OrdinalIgnoreCase)
                && !_player.IsPausedForCache())
            {
                _lastPlaybackProgressUtc = DateTime.UtcNow;
                return;
            }

            var recv = _player.GetReceivedBitrateKbps();
            var timePos = _player.GetTimePos();
            var idle = _player.IsCoreIdle();
            var eof = _player.IsEofReached();
            var buffering = _player.IsPausedForCache();

            var progressed = false;
            if (recv is > 0)
                progressed = true;
            if (timePos is double t)
            {
                if (_lastProgressTimePos is double prev && t > prev + 0.05)
                    progressed = true;
                _lastProgressTimePos = t;
            }

            if (progressed)
            {
                _lastPlaybackProgressUtc = DateTime.UtcNow;
                return;
            }

            // Still buffering after connect — give it time (paused-for-cache is normal briefly)
            if (buffering && (DateTime.UtcNow - _lastPlaybackProgressUtc).TotalSeconds < StallReconnectSeconds)
                return;

            var stalledFor = (DateTime.UtcNow - _lastPlaybackProgressUtc).TotalSeconds;
            if (stalledFor < StallReconnectSeconds)
                return;

            // Idle/eof with no packets, or frozen last-frame with 0kbps for too long
            if (idle || eof || recv is null or 0)
            {
                WriteLog($"stall watchdog: idle={idle} eof={eof} recv={recv?.ToString() ?? "null"} " +
                         $"stalled={stalledFor:0.0}s → reconnect");
                ForceReconnect("受信停止 — 再接続します…");
            }
        }
        catch (Exception ex)
        {
            WriteLog("playback health: " + ex.Message);
        }
    }

    private void ForceReconnect(string statusMessage)
    {
        if (_closing || !_launchArgs.HasStream) return;
        if (_reconnectPending) return;

        StatusText.Text = statusMessage;
        _suppressEndFileRetry = true;
        try { _player?.StopCurrentFile(); }
        catch { /* ignore */ }
        finally { _suppressEndFileRetry = false; }

        // Reset progress clock so we don't immediately re-trigger
        _lastPlaybackProgressUtc = DateTime.UtcNow;
        ScheduleRetry(liveContinuous: true);
    }

    private void ScheduleVideoChromeZOrderBoost()
    {
        void Boost()
        {
            try
            {
                _videoChrome?.SyncToVideoPanel();
                if (_videoChrome is { Visible: true })
                    _videoChrome.RaiseAboveOwner();
            }
            catch { /* ignore */ }
        }

        Boost();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Boost);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Boost);
        _ = Task.Run(async () =>
        {
            foreach (var ms in new[] { 100, 300, 800, 1500, 3000 })
            {
                try { await Task.Delay(ms).ConfigureAwait(false); }
                catch { return; }
                try { await Dispatcher.InvokeAsync(Boost); }
                catch { /* ignore */ }
            }
        });
    }

    private void BeginPlaybackWithRetry()
    {
        if (_closing) return;
        try { _retryCts?.Cancel(); } catch { /* ignore */ }
        _retryCts?.Dispose();
        _retryCts = new CancellationTokenSource();
        _loadAttempts = 0;
        _reconnectPending = false;
        _reconnectGeneration++;
        _initialAspectApplied = false;
        _lastPlaybackProgressUtc = DateTime.UtcNow;
        _lastProgressTimePos = null;
        TryLoadOnce();
    }

    private void TryLoadOnce()
    {
        if (_closing || _player is null || !_launchArgs.HasStream) return;
        _loadAttempts++;
        _reconnectPending = false; // attempt in flight (stall watchdog uses generation / pending flag)
        _lastPlaybackProgressUtc = DateTime.UtcNow;
        _lastProgressTimePos = null;
        var playUrl = ResolveAttemptUrl(_loadAttempts);
        WriteLog("loading attempt " + _loadAttempts + ": " + playUrl);
        try
        {
            StatusText.Text = _loadAttempts <= 1 ? "接続中…" : "再接続中… (" + _loadAttempts + ")";
            // Replace current (dead) file and ensure unpaused
            _player.Load(playUrl, play: true);
        }
        catch (Exception ex)
        {
            WriteLog("load threw: " + ex.Message);
            ScheduleRetry(liveContinuous: true);
        }
    }

    private string ResolveAttemptUrl(int attempt)
    {
        var stream = _launchArgs.PlaybackUrl ?? _launchArgs.StreamUrl!;
        var original = _launchArgs.StreamUrl ?? stream;
        // Alternate stream/pls a few times in case one form fails
        if (attempt is >= 3 and <= 6 &&
            !string.Equals(stream, original, StringComparison.OrdinalIgnoreCase) &&
            attempt % 2 == 0)
            return original;
        return stream;
    }

    /// <param name="liveContinuous">
    /// When true, keep retrying past <see cref="MaxLoadAttempts"/> with longer backoff
    /// (PeerCast live drops are normal).
    /// </param>
    private void ScheduleRetry(bool liveContinuous = false)
    {
        if (_closing) return;

        // Always cancel previous delay so we don't stack multiple reconnects
        try { _retryCts?.Cancel(); } catch { /* ignore */ }
        try { _retryCts?.Dispose(); } catch { /* ignore */ }
        _retryCts = new CancellationTokenSource();

        var cts = _retryCts;
        var gen = _reconnectGeneration;
        var attempt = Math.Max(1, _loadAttempts);

        int delayMs;
        if (!liveContinuous && attempt >= MaxLoadAttempts)
        {
            _reconnectPending = false;
            StatusText.Text = "接続に失敗しました";
            WriteLog("retry stopped after " + attempt + " attempts");
            return;
        }

        // First retries quick (PCRPlayer-like); long-lived live drops back off up to 15s
        if (attempt <= MaxLoadAttempts)
            delayMs = Math.Min(3000, 400 + attempt * 200);
        else
            delayMs = Math.Min(15000, 3000 + (attempt - MaxLoadAttempts) * 1000);

        _reconnectPending = true;
        StatusText.Text = "再接続待機 " + (delayMs / 1000.0).ToString("0.#") + "s…";
        WriteLog("schedule retry in " + delayMs + "ms (attempt=" + attempt + ", gen=" + gen + ")");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
                if (_closing || gen != _reconnectGeneration) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_closing || gen != _reconnectGeneration) return;
                    TryLoadOnce();
                });
            }
            catch (OperationCanceledException)
            {
                // normal — newer retry or file-loaded cancelled us
            }
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

        // 本家寄り: 最大化の「前」にバーをレイアウトから外し、動画を先に全面化してから FS へ。
        // （最大化後に外すと「バー付きFS → 一瞬で消えて動画が伸びる」になる）
        _isFullscreen = true;
        AttachFsChromeFloat();
        SetFullscreenChromeVisible(false);
        try { RootLayoutGrid.UpdateLayout(); } catch { /* ignore */ }

        WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;

        // Maximize 後もフロートは非表示のまま（Show はしない）
        SetFullscreenChromeVisible(false);
        UpdateMaximizeButtonGlyph();
        WriteLog("fullscreen: chrome detached before maximize (no size pop)");
    }

    private void ExitFullscreen()
    {
        DetachFsChromeFloat();
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
        UpdateMaximizeButtonGlyph();
    }

    /// <summary>
    /// FS only: move WriteBar+InfoBar into owned WinForms overlay (above mpv airspace).
    /// Main grid bottom rows stay at height 0 for the whole FS session — hover never resizes video.
    /// </summary>
    private void AttachFsChromeFloat()
    {
        if (_fsChromeFloating) return;

        EnsureFsChromeOverlay();

        if (WriteBar.Parent is System.Windows.Controls.Panel fromPanel)
        {
            fromPanel.Children.Remove(WriteBar);
            fromPanel.Children.Remove(InfoBar);
        }

        WriteBar.ClearValue(Grid.RowProperty);
        InfoBar.ClearValue(Grid.RowProperty);

        var panel = _fsChromeOverlay!.Panel;
        panel.Children.Clear();
        panel.Children.Add(WriteBar);
        panel.Children.Add(InfoBar);

        // Bars live only in the float host; keep them Visible so measure works when shown
        WriteBar.Visibility = Visibility.Visible;
        InfoBar.Visibility = Visibility.Visible;

        // Zero layout rows — ContentRow (*) fills the whole client area for the FS session
        WriteBarRow.Height = new GridLength(0);
        InfoBarRow.Height = new GridLength(0);

        var owner = new WindowInteropHelper(this).EnsureHandle();
        // Attach without Show() — no flash of the write bar
        _fsChromeOverlay.Attach(owner);

        _fsChromeFloating = true;
        _fsChromeVisible = false;
    }

    private void DetachFsChromeFloat()
    {
        if (!_fsChromeFloating) return;

        try { _fsChromeOverlay?.HideChrome(); } catch { /* ignore */ }
        try { _fsChromeOverlay?.Detach(); } catch { /* ignore */ }

        var panel = _fsChromeOverlay?.Panel;
        if (panel is not null)
        {
            panel.Children.Remove(WriteBar);
            panel.Children.Remove(InfoBar);
        }

        // Clear float-only size constraints
        WriteBar.ClearValue(FrameworkElement.MaxWidthProperty);
        InfoBar.ClearValue(FrameworkElement.MaxWidthProperty);

        if (WriteBar.Parent is null)
            RootLayoutGrid.Children.Add(WriteBar);
        if (InfoBar.Parent is null)
            RootLayoutGrid.Children.Add(InfoBar);

        Grid.SetRow(WriteBar, 1);
        Grid.SetRow(InfoBar, 2);

        WriteBarRow.Height = GridLength.Auto;
        InfoBarRow.Height = GridLength.Auto;

        WriteBar.Visibility = Visibility.Visible;
        InfoBar.Visibility = Visibility.Visible;

        _fsChromeFloating = false;
        _fsChromeVisible = true;
    }

    private void EnsureFsChromeOverlay()
    {
        if (_fsChromeOverlay is not null && !_fsChromeOverlay.IsDisposed)
            return;
        _fsChromeOverlay = new FsChromeOverlay();
    }

    private void SetFullscreenChromeVisible(bool visible)
    {
        if (_fsChromeVisible == visible &&
            (!visible || (_fsChromeOverlay?.Visible ?? false) == visible))
        {
            if (visible && _fsChromeFloating)
                _fsChromeOverlay?.SyncToMonitor();
            return;
        }

        if (_fsChromeFloating && _fsChromeOverlay is not null)
        {
            WriteBar.Visibility = Visibility.Visible;
            InfoBar.Visibility = Visibility.Visible;

            if (visible)
            {
                _fsChromeOverlay.ShowChrome();
            }
            else
            {
                try { Keyboard.ClearFocus(); } catch { /* ignore */ }
                try { WriteBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)); }
                catch { /* ignore */ }
                _fsChromeOverlay.HideChrome();
            }
        }
        else
        {
            // Not floating: should not happen in FS. Never Collapsed on layout rows in FS path.
            var vis = visible ? Visibility.Visible : Visibility.Collapsed;
            WriteBar.Visibility = vis;
            InfoBar.Visibility = vis;
        }

        _fsChromeVisible = visible;
    }

    /// <summary>
    /// Fullscreen hover: show float chrome near bottom; hide immediately when pointer leaves
    /// (including after a click / while caret is blinking). Never touches main layout size.
    /// </summary>
    private void UpdateFullscreenChromeFromPointer(System.Drawing.Point screen)
    {
        if (!_isFullscreen || !_fsChromeFloating) return;
        try
        {
            // Use monitor of main window — same space the float bar is pinned to
            var hwnd = new WindowInteropHelper(this).Handle;
            var mon = hwnd != IntPtr.Zero
                ? System.Windows.Forms.Screen.FromHandle(hwnd).Bounds
                : System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                  ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            var bottomZone = mon.Bottom - 80;
            var inMonitor = screen.X >= mon.Left && screen.X < mon.Right
                            && screen.Y >= mon.Top && screen.Y < mon.Bottom;
            var inBottom = inMonitor && screen.Y >= bottomZone;
            var overChrome = _fsChromeOverlay is not null &&
                             _fsChromeOverlay.Visible &&
                             _fsChromeOverlay.ContainsScreenPoint(screen);

            // Focus must NOT pin. Only pointer over bottom strip or the bar itself.
            if (inBottom || overChrome)
                SetFullscreenChromeVisible(true);
            else if (_fsChromeVisible || (_fsChromeOverlay?.Visible ?? false))
                SetFullscreenChromeVisible(false);
        }
        catch
        {
            // ignore
        }
    }

    private void DisposeFsChromeOverlay()
    {
        try
        {
            if (_fsChromeFloating)
                DetachFsChromeFloat();
        }
        catch { /* ignore */ }

        var overlay = _fsChromeOverlay;
        _fsChromeOverlay = null;
        _fsChromeFloating = false;
        if (overlay is null) return;
        try
        {
            overlay.Detach();
            overlay.Dispose();
        }
        catch { /* ignore */ }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Orderly teardown before visual tree teardown (prevents late mpv/BBS callbacks)
        _closing = true;
        _reconnectGeneration++;
        try { _retryCts?.Cancel(); } catch { /* ignore */ }
        try { _statsTimer?.Stop(); } catch { /* ignore */ }
        try { _channelTimer?.Stop(); } catch { /* ignore */ }
        try { _pointerTimer?.Stop(); } catch { /* ignore */ }
        try { DisposeFsChromeOverlay(); } catch { /* ignore */ }
        try { _bbsPoller?.Stop(); } catch { /* ignore */ }
        try { _videoChrome?.HideChrome(); } catch { /* ignore */ }
        try { _player?.Stop(); } catch { /* ignore */ }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        WriteLog("---- close ----");
        _closing = true;

        try { _statsTimer?.Stop(); } catch { }
        try { _channelTimer?.Stop(); } catch { }
        try { _pointerTimer?.Stop(); } catch { }
        try { DisposeFsChromeOverlay(); } catch { }
        try { Mouse.OverrideCursor = null; } catch { }

        try
        {
            if (_videoChrome is not null)
            {
                _videoChrome.HideChrome();
                _videoChrome.Dispose();
                _videoChrome = null;
            }
        }
        catch { }

        try { _sizingHook?.Dispose(); } catch { }
        _sizingHook = null;

        if (_commentVisible && CommentColumn.Width.IsAbsolute && CommentColumn.Width.Value > 120)
            _settings.CommentPanelWidth = CommentColumn.Width.Value;
        _settings.CommentPanelVisible = _commentVisible;
        try
        {
            SaveWindowPlacement();
            _settings.Save();
        }
        catch { /* ignore */ }

        try { _retryCts?.Cancel(); } catch { }
        try { _retryCts?.Dispose(); } catch { }
        _retryCts = null;

        try { _bbsPoller?.Dispose(); } catch { }
        _bbsPoller = null;

        try { _bbsWriter.Dispose(); } catch { }

        try
        {
            if (_player is not null)
            {
                _player.FileLoaded -= OnPlayerFileLoaded;
                _player.EndFile -= OnPlayerEndFile;
                _player.Stop();
                _player.Dispose();
            }
        }
        catch { }
        _player = null;
        _playerReady = false;

        try { _statsTimer = null; _channelTimer = null; _pointerTimer = null; }
        catch { /* ignore */ }
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
