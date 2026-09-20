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
using DSPlayer.Controls;
using DSPlayer.Models;
using DSPlayer.Services;
using DSPlayer.Services.Bbs;
using DSPlayer.Services.Mpv;
using DSPlayer.Services.PeerCast;
using DSPlayer.Themes;
using WinForms = System.Windows.Forms;

namespace DSPlayer;

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
    private readonly ThreadMomentum _momentum = new();
    private const double MomentumBarMaxWidth = 64;
    private BbsPoller? _bbsPoller;
    private readonly BbsWriter _bbsWriter;
    private readonly PeerCastXmlClient _pcsXml = new();
    private bool _stickToBottom = true;
    /// <summary>New posts arrived while the user was reading history.</summary>
    private int _unseenNewPosts;
    /// <summary>True while we ourselves are snapping to the latest res (ignore ScrollChanged).</summary>
    private bool _programmaticCommentScroll;
    /// <summary>Invalidates delayed live-follow callbacks when the user starts scrolling.</summary>
    private int _commentScrollRequestVersion;
    /// <summary>True when this poll rebuilt the list (first load / thread replace).</summary>
    private bool _commentsRebuiltThisUpdate;
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
    private WindowSnapHook? _snapHook;
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

    // Comment list: middle-click autoscroll mode (browser-style: click once, move to scroll)
    private bool _commentAutoScroll;
    private System.Windows.Point _commentAutoScrollOrigin; // relative to ScrollViewer

    // Write-box multi-line: freeze content row (video+comments) in pixels; only window grows down
    private bool _contentRowFrozen;
    private double _frozenContentHeight;
    private const double WriteBoxMaxHeight = 160;
    private const string WriteButtonIdleLabel = "書込";
    private readonly BbsWriteCooldown _writeCooldown = new();
    private DispatcherTimer? _postCooldownTimer;
    private DispatcherTimer? _newHighlightTimer;
    private bool IsPostCooldownActive =>
        _writeCooldown.RemainingSeconds(_writeThread ?? _bbsPoller?.ResolvedThread) > 0;

    /// <summary>WinForms min/max/close on the video panel (over mpv, not a top-level window).</summary>
    private VideoChromeOverlay? _videoChrome;

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_SETREDRAW = 0x000B;
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

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprc, IntPtr hrgn, uint flags);

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwFrame = 0x0400;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdatenow = 0x0100;

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
            "DSPlayer",
            "player.log");

        InitializeComponent();
        CommentImageLoader.EmbedEnabled = _settings.EmbedCommentImages;
        CommentList.ItemsSource = _comments;
        // Bubbling >>N clicks from AnchorBodyBlock inside the item template
        CommentList.AddHandler(
            AnchorBodyBlock.AnchorClickEvent,
            new EventHandler<AnchorClickEventArgs>(CommentList_AnchorClick));
        CommentList.AddHandler(
            AnchorBodyBlock.ImageClickEvent,
            new EventHandler<CommentImageClickEventArgs>(CommentImage_PreviewClick));
        CommentList.AddHandler(
            CommentHeaderBlock.LinkClickEvent,
            new EventHandler<HeaderLinkEventArgs>(CommentHeader_LinkClick));
        CommentList.AddHandler(
            CommentHeaderBlock.LinkHoverEvent,
            new EventHandler<HeaderLinkEventArgs>(CommentHeader_LinkHover));
        CommentList.AddHandler(
            CommentHeaderBlock.LinkLeaveEvent,
            new EventHandler<HeaderLinkEventArgs>(CommentHeader_LinkLeave));
        // Middle-click autoscroll: click once → move to scroll → click again / LMB to exit
        CommentList.PreviewMouseDown += CommentList_AutoScroll_PreviewMouseDown;
        CommentList.PreviewMouseWheel += CommentList_PreviewMouseWheel;
        PreviewKeyDown += CommentList_AutoScroll_PreviewKeyDown;
        ApplyCommentPanelSettings();
        ApplyCommentFont();
        ApplyUiTheme(); // chrome + always ends with ApplyCommentListTheme()
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
                MenuReconnect.IsEnabled = !_closing && _playerReady && _launchArgs.HasStream;
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
        // Re-apply chrome colors (overlay may be created after first ApplyUiTheme)
        try
        {
            var t = UiTheme.FromId(_settings.UiTheme);
            _videoChrome.ApplyTheme(
                t.ChromeBarR, t.ChromeBarG, t.ChromeBarB,
                t.ChromeHoverR, t.ChromeHoverG, t.ChromeHoverB,
                t.ChromePressR, t.ChromePressG, t.ChromePressB,
                t.ChromeTextR, t.ChromeTextG, t.ChromeTextB);
        }
        catch { /* ignore */ }
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
        _snapHook = new WindowSnapHook(this, () => _settings.WindowSnapEnabled && !_isFullscreen);
        _snapHook.Attach();
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
        // Browser-style comment autoscroll: keep ScrollNS and scroll by pointer offset
        if (_commentAutoScroll)
        {
            TickCommentAutoScroll();
            return;
        }

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
        if (_commentAutoScroll) return;
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
            UpdateWriteBoxHeight();
            EnsureNewHighlightTimer();
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
                "DSPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
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
        StatusText.Text = left.Count > 0 ? string.Join("  |  ", left) : "DSPlayer";
        var right = BuildRightStatusParts();
        RightStatsText.Text = right.Count > 0 ? string.Join("  ", right) : "";
        RefreshMomentumUi();
        UpdateWindowTitle();
    }

    private void ResetMomentum()
    {
        _momentum.Clear();
        RefreshMomentumUi();
    }

    /// <summary>
    /// Score from DAT post clocks in the last 5 minutes — including the first load,
    /// so launch already shows whether the thread is hot.
    /// </summary>
    private void ApplyMomentumUpdate(BbsUpdatedEventArgs e)
    {
        _momentum.UpdateFromPosts(e.AllPosts, DateTime.UtcNow);
    }

    /// <summary>
    /// Recompute the 5-minute window and paint the status-bar heat meter.
    /// Always visible, including score 0. Chrome follows <see cref="AppSettings.MomentumStyle"/>.
    /// </summary>
    private void RefreshMomentumUi()
    {
        _momentum.Recalculate(DateTime.UtcNow);
        var score = _momentum.Score;
        MomentumPanel.Visibility = Visibility.Visible;

        var heat = ThreadMomentum.IsHeatStyle(_settings.MomentumStyle);
        MomentumHeat.Visibility = heat ? Visibility.Visible : Visibility.Collapsed;
        MomentumSimple.Visibility = heat ? Visibility.Collapsed : Visibility.Visible;

        if (!heat)
        {
            MomentumText.Text = ThreadMomentum.FormatDisplay(score);
            MomentumBarFill.Width = MomentumBarMaxWidth * score / 100.0;
            return;
        }

        ThreadMomentum.SplitMeter(score, out var filled, out var empty);
        MomentumHeatPrefix.Text = "勢い(" + score + ")";
        MomentumHeatFilled.Text = filled;
        MomentumHeatEmpty.Text = empty;
        MomentumHeatMood.Text = ThreadMomentum.HeatLabelFromScore(score);
        PaintMomentumMeter(score);
    }

    private void PaintMomentumMeter(int score)
    {
        var t = UiTheme.FromId(_settings.UiTheme);
        var muted = t.Brush(t.StatusMuted);
        MomentumHeatPrefix.Foreground = muted;
        MomentumHeatEmpty.Foreground = muted;

        if (score <= 0)
        {
            MomentumHeatMood.Foreground = muted;
            return;
        }

        var rgb = ThreadMomentum.MeterColorFromScore(score);
        var heat = t.Brush(System.Windows.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B));
        MomentumHeatFilled.Foreground = heat;
        MomentumHeatMood.Foreground = heat;
    }

    /// <summary>
    /// Taskbar hover preview / Alt-Tab use <see cref="Window.Title"/>.
    /// PCRPlayer shows the channel name; keep the same when we know it.
    /// Prefer live view.xml name, then PeCaRecorder <c>$0</c>, else "DSPlayer".
    /// </summary>
    private void UpdateWindowTitle()
    {
        var name = FirstNonEmpty(_channelInfo?.Name, _launchArgs.ChannelName)?.Trim();
        var title = !string.IsNullOrEmpty(name) ? name! : "DSPlayer";
        if (!string.Equals(Title, title, StringComparison.Ordinal))
            Title = title;
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
            ResetMomentum();
            return;
        }

        var thread = BbsThreadRef.TryParse(_contactUrl);
        if (thread is null || !thread.CanFetch)
        {
            BoardTitleText.Text = "掲示板を解釈できません";
            SetWriteEnabled(false);
            ResetMomentum();
            return;
        }

        WriteLog("bbs kind=" + thread.Kind);
        BoardTitleText.Text = thread.Board ?? "掲示板";
        SetWriteEnabled(thread.CanWrite || thread.Kind is BbsBoardKind.Shitaraba or BbsBoardKind.TwochStyle);
        _writeThread = thread.CanWrite ? thread : null;
        ResetMomentum();

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
        RefreshWriteButtonEnabled();
    }

    private bool CanWriteNow =>
        !_writing &&
        (_writeThread?.CanWrite == true || _bbsPoller?.ResolvedThread?.CanWrite == true);

    private void RefreshWriteButtonEnabled()
    {
        // Keep enabled during cooldown so the countdown number isn't grayed out
        WriteButton.IsEnabled = CanWriteNow;
        if (IsPostCooldownActive)
            UpdatePostCooldownUi();
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
            ClearUnseenNewPosts();
            ResetMomentum();
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
        ClearUnseenNewPosts();
        ResetMomentum();
        _bbsPoller.SwitchThread(item.Id);
        UpdatePostCooldownUi();
    }

    private void Menu_SelectThread_Click(object sender, RoutedEventArgs e)
    {
        OpenThreadPopup();
    }

    private void BbsPoller_Updated(object? sender, BbsUpdatedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Capture before adding rows — extent growth can look like "left the bottom".
            var follow = _stickToBottom && !_userScrollingComments;
            ApplyCommentUpdate(e);
            _threadResCount = e.AllPosts.Count;

            ApplyMomentumUpdate(e);
            RefreshMomentumUi();

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

            // Follow live only while the viewport is already on the latest res.
            // Reading history must not be yanked back when a new post lands.
            if (e.IsFirstLoad || _commentsRebuiltThisUpdate)
            {
                ResumeLiveComments();
                ScheduleScrollCommentsToEnd();
            }
            else if (e.NewPosts.Count > 0)
            {
                if (follow)
                {
                    ResumeLiveComments();
                    ScheduleScrollCommentsToEnd();
                }
                else
                {
                    _unseenNewPosts += e.NewPosts.Count;
                    UpdateNewPostsJumpButton();
                }
            }
            else if (follow)
            {
                ResumeLiveComments();
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
        _commentsRebuiltThisUpdate = false;

        // First open / empty UI / thread replaced → show only the tail (latest)
        if (e.IsFirstLoad || _comments.Count == 0)
        {
            RebuildCommentsFromTail(e.AllPosts, highlightNew: false);
            _commentsRebuiltThisUpdate = true;
            return;
        }

        if (e.AllPosts.Count == 0)
        {
            _comments.Clear();
            _commentsRebuiltThisUpdate = true;
            return;
        }

        // Thread switch or reset (res count dropped, or numbering jumped back)
        var lastUi = _comments[^1].Number;
        var lastAll = e.AllPosts[^1].Number;
        if (lastAll < lastUi || e.AllPosts.Count < _threadResCount && e.AllPosts.Count < lastUi)
        {
            RebuildCommentsFromTail(e.AllPosts, highlightNew: false);
            _commentsRebuiltThisUpdate = true;
            return;
        }

        if (e.NewPosts.Count == 0)
            return;

        foreach (var c in _comments)
            c.IsNew = false;

        foreach (var p in e.NewPosts)
            _comments.Add(new CommentItem(p, _settings, isNew: true));

        TrimCommentsHead();
        RefreshIdCounts(e.AllPosts);
        EnsureNewHighlightTimer();
    }

    private void EnsureNewHighlightTimer()
    {
        if (_newHighlightTimer is not null) return;
        _newHighlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _newHighlightTimer.Tick += (_, _) => ExpireNewHighlights();
        _newHighlightTimer.Start();
    }

    private void ExpireNewHighlights()
    {
        if (_comments.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var c in _comments)
        {
            if (c.IsNew && now >= c.HighlightUntilUtc)
                c.IsNew = false;
        }
    }

    private void RebuildCommentsFromTail(IReadOnlyList<BbsPost> all, bool highlightNew)
    {
        _comments.Clear();
        if (all.Count == 0) return;

        var start = Math.Max(0, all.Count - MaxDisplayedComments);
        for (var i = start; i < all.Count; i++)
            _comments.Add(new CommentItem(all[i], _settings, isNew: highlightNew));
        RefreshIdCounts(all);
    }

    private void RefreshIdCounts(IReadOnlyList<BbsPost> all)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in all)
        {
            var id = p.PosterId;
            if (string.IsNullOrEmpty(id))
                continue;
            counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
        }

        foreach (var c in _comments)
        {
            var id = c.PosterId;
            c.IdCount = !string.IsNullOrEmpty(id) && counts.TryGetValue(id, out var n) ? n : 0;
        }
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
    /// Grow write box only on explicit newlines. Always top-aligned so the first
    /// Enter grows downward by a full line (center→top used to look like half a line).
    /// Content row is pixel-frozen; only the window grows downward.
    /// </summary>
    private void WriteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWriteBoxHeight();
    }

    private double WriteBoxChromeHeight =>
        WriteBox.Padding.Top + WriteBox.Padding.Bottom
        + WriteBox.BorderThickness.Top + WriteBox.BorderThickness.Bottom;

    private double WriteBoxSingleLineHeight =>
        Math.Max(18.0, Math.Ceiling(WriteBoxChromeHeight + WriteBoxLineStep));

    private double WriteBoxLineStep
    {
        get
        {
            var fontSize = WriteBox.FontSize > 0 ? WriteBox.FontSize : 13;
            double spacing;
            try { spacing = WriteBox.FontFamily.LineSpacing; }
            catch { spacing = 1.35; }
            if (spacing < 1.2) spacing = 1.35;
            return Math.Ceiling(fontSize * spacing);
        }
    }

    private static int CountWriteBoxLines(string text)
    {
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lines++;
        }
        return lines;
    }

    private double ComputeWriteBoxHeight(int lines)
    {
        var n = Math.Max(1, lines);
        return Math.Min(WriteBoxMaxHeight, Math.Ceiling(WriteBoxChromeHeight + n * WriteBoxLineStep));
    }

    private void UpdateWriteBoxHeight()
    {
        var lines = CountWriteBoxLines(WriteBox.Text ?? "");
        var single = WriteBoxSingleLineHeight;
        var target = ComputeWriteBoxHeight(lines);

        WriteBox.MinHeight = single;
        WriteBox.MaxHeight = WriteBoxMaxHeight;
        WriteBox.VerticalContentAlignment = VerticalAlignment.Top;
        WriteBox.VerticalScrollBarVisibility = target >= WriteBoxMaxHeight - 0.5
            ? System.Windows.Controls.ScrollBarVisibility.Auto
            : System.Windows.Controls.ScrollBarVisibility.Disabled;

        var prev = WriteBox.Height;
        if (Math.Abs(prev - target) > 0.5)
            ApplyWriteBoxHeightChange(prev, target);

        try { WriteBox.ScrollToHome(); } catch { /* ignore */ }
        WriteButton.Height = single;
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

            // Set write box first, then grow the window by the bar's real delta
            // (first Enter used to clip because the theoretical line-step was short).
            var barBefore = WriteBar.ActualHeight;
            WriteBox.Height = targetHeight;
            try { WriteBox.UpdateLayout(); WriteBar.UpdateLayout(); } catch { /* ignore */ }
            var barDelta = WriteBar.ActualHeight - barBefore;
            if (Math.Abs(barDelta) < 0.5)
                barDelta = delta;

            var work = SystemParameters.WorkArea;
            var newWindowH = Height + barDelta;
            if (barDelta > 0)
                newWindowH = Math.Min(newWindowH, Math.Max(MinHeight, work.Bottom - Top));
            else
                newWindowH = Math.Max(MinHeight, newWindowH);

            if (Math.Abs(newWindowH - Height) >= 0.5)
                Height = newWindowH;

            // Keep content frozen at original size after window change
            ContentRow.Height = new GridLength(_frozenContentHeight, GridUnitType.Pixel);

            // Back to single line → allow normal * layout again
            if (targetHeight <= WriteBoxSingleLineHeight + 0.5)
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
            var single = WriteBoxSingleLineHeight;
            var prev = WriteBox.Height;
            if (Math.Abs(prev - single) > 0.5)
                ApplyWriteBoxHeightChange(prev, single);
            else
            {
                WriteBox.Height = single;
                UnfreezeContentRow();
            }
            WriteButton.Height = single;
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
        if (IsPostCooldownActive)
            return;

        var thread = _writeThread ?? _bbsPoller?.ResolvedThread;
        if (thread is null || !thread.CanWrite) return;

        var body = (WriteBox.Text ?? "").Trim();
        if (body.Length == 0) return;

        // Name/mail are not in settings UI; always sage anonymous post for now.
        var name = "";
        var mail = "sage";

        _writing = true;
        WriteButton.IsEnabled = false;
        WriteLog("bbs write begin len=" + body.Length);

        try
        {
            var result = await _bbsWriter.PostAsync(thread, name, mail, body).ConfigureAwait(true);
            WriteLog("bbs write: " + result.Success + " " + result.Message +
                     (string.IsNullOrEmpty(result.ResponseSnippet) ? "" : " | " + result.ResponseSnippet));

            if (result.Success)
            {
                ClearWriteBox();
                _writeCooldown.NoteSuccess(thread);
                StartPostCooldownClock();
                ResumeLiveComments();
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
                    _writeCooldown.NoteSuccess(thread);
                    StartPostCooldownClock();
                    ResumeLiveComments();
                    await Task.Delay(800).ConfigureAwait(true);
                    _bbsPoller?.RequestRefresh();
                }
                else if (retry.IsFloodLimited)
                {
                    _writeCooldown.NoteFlood(thread, retry.RetryAfterSeconds);
                    StartPostCooldownClock();
                }
            }
            else if (result.IsFloodLimited)
            {
                _writeCooldown.NoteFlood(thread, result.RetryAfterSeconds);
                StartPostCooldownClock();
            }
        }
        catch (Exception ex)
        {
            WriteLog("bbs write error: " + ex.Message);
        }
        finally
        {
            _writing = false;
            RefreshWriteButtonEnabled();
        }
    }

    private void StartPostCooldownClock()
    {
        if (!IsPostCooldownActive)
        {
            ClearPostCooldownUi();
            return;
        }

        _postCooldownTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _postCooldownTimer.Tick -= PostCooldown_Tick;
        _postCooldownTimer.Tick += PostCooldown_Tick;
        if (!_postCooldownTimer.IsEnabled)
            _postCooldownTimer.Start();
        UpdatePostCooldownUi();
    }

    private void ClearPostCooldownUi()
    {
        try { _postCooldownTimer?.Stop(); } catch { /* ignore */ }
        WriteButton.Content = WriteButtonIdleLabel;
        WriteButton.ToolTip = null;
        try
        {
            var t = UiTheme.FromId(_settings.UiTheme);
            WriteButton.FontWeight = t.UseAccentButton ? FontWeights.SemiBold : FontWeights.Normal;
        }
        catch { WriteButton.FontWeight = FontWeights.Normal; }
        RefreshWriteButtonEnabled();
    }

    private void PostCooldown_Tick(object? sender, EventArgs e) => UpdatePostCooldownUi();

    private void UpdatePostCooldownUi()
    {
        if (_closing)
        {
            try { _postCooldownTimer?.Stop(); } catch { /* ignore */ }
            return;
        }

        var thread = _writeThread ?? _bbsPoller?.ResolvedThread;
        var remain = _writeCooldown.RemainingSeconds(thread);
        if (remain <= 0)
        {
            ClearPostCooldownUi();
            return;
        }

        WriteButton.Content = remain.ToString(CultureInfo.InvariantCulture);
        WriteButton.FontWeight = FontWeights.Bold;
        var interval = thread is null ? remain : _writeCooldown.KnownIntervalSeconds(thread);
        WriteButton.ToolTip = "連投規制 あと " + remain + " 秒（間隔 " + interval + " 秒）";
        // Stay enabled so the number isn't drawn with the disabled gray brush
    }

    private bool IsFollowingLiveComments => _stickToBottom && !_userScrollingComments;

    private void ResumeLiveComments()
    {
        _stickToBottom = true;
        _userScrollingComments = false;
        ClearUnseenNewPosts();
    }

    private void PauseLiveComments()
    {
        _commentScrollRequestVersion++;
        _programmaticCommentScroll = false;
        _stickToBottom = false;
        _userScrollingComments = true;
    }

    private void ClearUnseenNewPosts()
    {
        if (_unseenNewPosts == 0 && NewPostsJumpButton.Visibility == Visibility.Collapsed)
            return;
        _unseenNewPosts = 0;
        UpdateNewPostsJumpButton();
    }

    private void UpdateNewPostsJumpButton()
    {
        if (_unseenNewPosts <= 0)
        {
            NewPostsJumpButton.Visibility = Visibility.Collapsed;
            return;
        }

        NewPostsJumpButton.Content = _unseenNewPosts == 1
            ? "新着 ↓"
            : "新着 " + _unseenNewPosts + " ↓";
        NewPostsJumpButton.Visibility = Visibility.Visible;
    }

    private void NewPostsJumpButton_Click(object sender, RoutedEventArgs e)
    {
        ResumeLiveComments();
        ScheduleScrollCommentsToEnd();
    }

    private void CommentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_programmaticCommentScroll) return;
        if (e.OriginalSource is not ScrollViewer sv) return;

        // Content grew or viewport resized while following → keep the latest pinned
        if (IsFollowingLiveComments &&
            (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0))
        {
            ScrollCommentsToEndCore();
            return;
        }

        // Virtualizing panel can report 0 during recycle; don't treat that as "at bottom".
        if (sv.ScrollableHeight <= 0)
            return;

        var atBottom = sv.ScrollableHeight - sv.VerticalOffset < 40;
        if (atBottom)
        {
            if (!IsFollowingLiveComments || _unseenNewPosts > 0)
                ResumeLiveComments();
        }
        else if (e.VerticalChange != 0)
        {
            PauseLiveComments();
        }
    }

    private void CommentList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;
        if (!IsFollowingLiveComments) return;
        ScheduleScrollCommentsToEnd();
    }

    private void ScheduleScrollCommentsToEnd()
    {
        // Virtualizing list needs layout pass(es) before ScrollIntoView works on last item
        var requestVersion = ++_commentScrollRequestVersion;
        _programmaticCommentScroll = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (requestVersion == _commentScrollRequestVersion && IsFollowingLiveComments)
                ScrollCommentsToEndCore();
        }));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (requestVersion != _commentScrollRequestVersion)
                return;
            if (IsFollowingLiveComments)
                ScrollCommentsToEndCore();
            _programmaticCommentScroll = false;
        }));
    }

    private void ScrollCommentsToEndCore()
    {
        if (!_stickToBottom) return;
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

    private DispatcherTimer? _idRefLeaveTimer;
    private DispatcherTimer? _idRefHoverTimer;
    private string? _idRefPendingId;
    private string? _idRefOpenId;
    private bool _idRefBusy;

    /// <summary>Phase 6b: >>N in comment body → jump to that res in the list.</summary>
    private void CommentList_AnchorClick(object? sender, AnchorClickEventArgs e)
    {
        e.Handled = true;
        JumpToResNumber(e.ResNumber);
    }

    private void CommentHeader_LinkClick(object? sender, HeaderLinkEventArgs e)
    {
        e.Handled = true;
        if (e.Kind == HeaderLinkKind.ResNumber)
        {
            HideIdRefPopup();
            QuoteResNumber(e.ResNumber);
            return;
        }

        if (e.Kind == HeaderLinkKind.PosterId && !string.IsNullOrEmpty(e.PosterId))
            ShowPosterIdOverlay(e.PosterId);
    }

    private void CommentHeader_LinkHover(object? sender, HeaderLinkEventArgs e)
    {
        if (e.Kind != HeaderLinkKind.PosterId || string.IsNullOrEmpty(e.PosterId))
            return;
        CancelIdRefLeave();
        SchedulePosterIdOverlay(e.PosterId);
    }

    private void CommentHeader_LinkLeave(object? sender, HeaderLinkEventArgs e)
    {
        ScheduleIdRefLeave();
    }

    private void ResBadge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CommentItem item })
            return;
        e.Handled = true;
        HideIdRefPopup();
        QuoteResNumber(item.Number);
    }

    private CommentImageWindow? _imagePopup;

    private void CommentImage_PreviewClick(object? sender, CommentImageClickEventArgs e)
    {
        e.Handled = true;
        ShowImagePopup(e.Image);
    }

    private void ShowImagePopup(LoadedCommentImage image)
    {
        try
        {
            if (_imagePopup is null)
            {
                _imagePopup = new CommentImageWindow();
                _imagePopup.Closed += (_, _) => _imagePopup = null;
            }

            _imagePopup.ShowImage(image, this);
        }
        catch (Exception ex)
        {
            WriteLog("image popup: " + ex.Message);
        }
    }

    private void CloseImagePopup()
    {
        try { _imagePopup?.Close(); } catch { /* ignore */ }
        _imagePopup = null;
    }

    private void IdRefOverlay_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => CancelIdRefLeave();

    private void IdRefOverlay_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleIdRefLeave();

    private void QuoteResNumber(int number)
    {
        if (number <= 0 || !WriteBox.IsEnabled)
            return;

        var insert = ">>" + number;
        var text = WriteBox.Text ?? "";
        var caret = WriteBox.CaretIndex;
        if (caret < 0 || caret > text.Length)
            caret = text.Length;

        var needNl = caret > 0 && text[caret - 1] != '\n';
        var chunk = (needNl ? "\n" : "") + insert + "\n";
        WriteBox.Text = text.Insert(caret, chunk);
        WriteBox.CaretIndex = caret + chunk.Length;
        try { WriteBox.Focus(); } catch { /* ignore */ }
    }

    private void SchedulePosterIdOverlay(string posterId)
    {
        if (string.Equals(_idRefOpenId, posterId, StringComparison.Ordinal) &&
            IdRefOverlay.Visibility == Visibility.Visible)
            return;

        _idRefPendingId = posterId;
        _idRefHoverTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _idRefHoverTimer.Tick -= IdRefHover_Tick;
        _idRefHoverTimer.Tick += IdRefHover_Tick;
        _idRefHoverTimer.Stop();
        _idRefHoverTimer.Start();
    }

    private void IdRefHover_Tick(object? sender, EventArgs e)
    {
        try { _idRefHoverTimer?.Stop(); } catch { /* ignore */ }
        var id = _idRefPendingId;
        _idRefPendingId = null;
        if (!string.IsNullOrEmpty(id))
            ShowPosterIdOverlay(id);
    }

    private void ShowPosterIdOverlay(string posterId)
    {
        if (_closing || _idRefBusy)
            return;
        if (string.Equals(_idRefOpenId, posterId, StringComparison.Ordinal) &&
            IdRefOverlay.Visibility == Visibility.Visible)
            return;

        _idRefBusy = true;
        try
        {
            var rows = new List<IdRefRow>(16);
            IReadOnlyList<BbsPost> source;
            try { source = SnapshotPosts(); }
            catch { return; }

            for (var i = 0; i < source.Count; i++)
            {
                var p = source[i];
                if (!BbsPosterId.Same(p.PosterId, posterId))
                    continue;
                rows.Add(ToIdRefRow(p));
                if (rows.Count >= 20)
                    break;
            }

            if (rows.Count == 0)
            {
                HideIdRefPopup();
                return;
            }

            IdRefTitle.Text = "ID:" + posterId + "  " + rows.Count + "レス";
            IdRefRows.Children.Clear();
            foreach (var row in rows)
            {
                var tb = new TextBlock
                {
                    Text = row.Number + "  " + row.Preview,
                    FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = row.Number,
                };
                tb.MouseLeftButtonUp += IdRefRow_MouseLeftButtonUp;
                IdRefRows.Children.Add(tb);
            }

            var pos = Mouse.GetPosition(RootLayoutGrid);
            var x = Math.Max(8, pos.X + 14);
            var y = Math.Max(8, pos.Y + 18);
            IdRefOverlay.Visibility = Visibility.Visible;
            IdRefOverlay.UpdateLayout();
            var w = IdRefOverlay.ActualWidth;
            var h = IdRefOverlay.ActualHeight;
            var maxX = Math.Max(8, RootLayoutGrid.ActualWidth - w - 8);
            var maxY = Math.Max(8, RootLayoutGrid.ActualHeight - h - 8);
            if (x > maxX) x = maxX;
            if (y > maxY) y = maxY;
            IdRefOverlay.Margin = new Thickness(x, y, 0, 0);
            _idRefOpenId = posterId;
        }
        catch (Exception ex)
        {
            WriteLog("id overlay: " + ex.Message);
            HideIdRefPopup();
        }
        finally
        {
            _idRefBusy = false;
        }
    }

    private void IdRefRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock { Tag: int n })
            return;
        e.Handled = true;
        HideIdRefPopup();
        JumpToResNumber(n);
    }

    private IReadOnlyList<BbsPost> SnapshotPosts()
    {
        try
        {
            if (_bbsPoller?.Posts is { Count: > 0 } all)
                return all.ToArray();
        }
        catch { /* poller may replace the list */ }

        if (_comments.Count == 0)
            return Array.Empty<BbsPost>();

        var copy = new BbsPost[_comments.Count];
        for (var i = 0; i < _comments.Count; i++)
            copy[i] = _comments[i].Post;
        return copy;
    }

    private static IdRefRow ToIdRefRow(BbsPost p)
    {
        var body = (p.BodyText ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (body.Length > 80)
            body = body[..80] + "…";
        return new IdRefRow(p.Number, string.IsNullOrEmpty(body) ? "（本文なし）" : body);
    }

    private void ScheduleIdRefLeave()
    {
        _idRefLeaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _idRefLeaveTimer.Tick -= IdRefLeave_Tick;
        _idRefLeaveTimer.Tick += IdRefLeave_Tick;
        _idRefLeaveTimer.Stop();
        _idRefLeaveTimer.Start();
    }

    private void CancelIdRefLeave()
    {
        try { _idRefLeaveTimer?.Stop(); } catch { /* ignore */ }
    }

    private void IdRefLeave_Tick(object? sender, EventArgs e)
    {
        CancelIdRefLeave();
        HideIdRefPopup();
    }

    private void HideIdRefPopup()
    {
        CancelIdRefLeave();
        try { _idRefHoverTimer?.Stop(); } catch { /* ignore */ }
        _idRefPendingId = null;
        _idRefOpenId = null;
        try
        {
            IdRefRows.Children.Clear();
            IdRefOverlay.Visibility = Visibility.Collapsed;
        }
        catch { /* ignore */ }
    }

    private sealed class IdRefRow
    {
        public IdRefRow(int number, string preview)
        {
            Number = number;
            Preview = preview;
        }

        public int Number { get; }
        public string Preview { get; }
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
            PauseLiveComments();
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
        // Prefer in-body / header text selection (Grok: select-copy body, not card)
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox tb && tb.SelectionLength > 0)
        {
            try { System.Windows.Clipboard.SetText(tb.SelectedText); return; }
            catch { /* ignore */ }
        }

        if (Keyboard.FocusedElement is System.Windows.Controls.RichTextBox rtb)
        {
            try
            {
                var t = rtb.Selection?.Text ?? "";
                if (!string.IsNullOrEmpty(t))
                {
                    System.Windows.Clipboard.SetText(t);
                    return;
                }
            }
            catch { /* ignore */ }
        }

        if (Keyboard.FocusedElement is AnchorBodyBlock body && !string.IsNullOrEmpty(body.SelectedText))
        {
            try { System.Windows.Clipboard.SetText(body.SelectedText); return; }
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
            // Stop/end-file arrives asynchronously, often after ForceReconnect returns.
            // Keep its scheduled retry instead of scheduling another from this late event.
            if (_suppressEndFileRetry || _reconnectPending)
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
        var retryToken = cts.Token;
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
                await Task.Delay(delayMs, retryToken).ConfigureAwait(false);
                if (_closing || retryToken.IsCancellationRequested || gen != _reconnectGeneration) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_closing || retryToken.IsCancellationRequested || gen != _reconnectGeneration) return;
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

    private void UpdateVolumeText()
    {
        var n = (int)Math.Round(_player?.Volume ?? 0);
        n = Math.Clamp(n, 0, 100);
        VolumeText.Text = n.ToString();
    }

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
            case Key.Escape when _imagePopup is { IsVisible: true }:
                CloseImagePopup();
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
    private void Menu_Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || !_playerReady || !_launchArgs.HasStream) return;

        WriteLog("manual reconnect requested");
        // Restart at the short retry delay even during an automatic backoff. The new
        // generation invalidates any previous retry already queued on the dispatcher.
        _reconnectGeneration++;
        _loadAttempts = 0;
        _reconnectPending = false;
        ForceReconnect("手動で再接続します…");
    }

    private void Menu_CommentVisible_Click(object sender, RoutedEventArgs e) =>
        SetCommentVisible(MenuCommentVisible.IsChecked == true);
    private void Menu_ScrollBottom_Click(object sender, RoutedEventArgs e)
    {
        ResumeLiveComments();
        ScheduleScrollCommentsToEnd();
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
        EndCommentAutoScroll();
        ClearPointerCursorState();

        try
        {
            // Snapshot for Cancel → full revert
            var snapshotJson = AppSettings.SerializeSnapshot(_settings);

            var dlg = new SettingsWindow(
                _settings,
                onLiveApply: () =>
                {
                    try { ApplySettingsLive(restartBbs: false); }
                    catch (Exception ex) { WriteLog("settings live: " + ex.Message); }
                })
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
            };

            var ok = dlg.ShowDialog() == true;
            WriteLog("settings closed ok=" + ok);
            if (ok)
            {
                _settings.Save();
                ApplySettingsLive(restartBbs: true);
            }
            else
            {
                // Revert in-memory settings + UI
                AppSettings.RestoreSnapshot(_settings, snapshotJson);
                ApplySettingsLive(restartBbs: true);
            }
        }
        catch (Exception ex)
        {
            WriteLog("settings dialog: " + ex);
            System.Windows.MessageBox.Show(
                "設定画面を開けませんでした:\n" + ex.Message,
                "DSPlayer",
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
    /// <param name="restartBbs">
    /// Restart poller for interval/normalize. Skip during live-preview typing to avoid spam.
    /// </param>
    private void ApplySettingsLive(bool restartBbs = true)
    {
        CommentImageLoader.EmbedEnabled = _settings.EmbedCommentImages;
        ApplyCommentFont();
        ApplyUiTheme(); // includes ApplyCommentListTheme at end
        ApplyCommentListTheme();
        RefreshAllCommentHeaders();
        RefreshStatusBar();
        WriteLog("settings applied: ui=" + _settings.UiTheme +
                 " comments=" + _settings.CommentListTheme +
                 " header=" + _settings.CommentHeaderFontFamily +
                 "/" + _settings.CommentHeaderFontSize +
                 " body=" + _settings.CommentBodyFontFamily +
                 "/" + _settings.CommentBodyFontSize +
                 " interval=" + _settings.BbsIntervalSeconds +
                 " normalize=" + _settings.MessageNormalize);

        if (restartBbs &&
            (!string.IsNullOrWhiteSpace(_contactUrl) || _launchArgs.HasContact))
        {
            _ = RestartBbsAfterSettingsAsync();
        }
    }

    // --- Comment middle-click autoscroll (browser-style: click, move, click again) ---

    private void CommentList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindDescendantScrollViewer(CommentList);
        if (sv is null || sv.ScrollableHeight <= 0)
            return;

        // RichTextBox/image content otherwise consumes the wheel before the outer list.
        // Pause live-follow first so a queued "scroll to latest" cannot undo this notch.
        if (e.Delta > 0)
            PauseLiveComments();

        var notches = e.Delta / 120.0;
        var lines = Math.Max(1, SystemParameters.WheelScrollLines);
        var pixels = notches * lines * Math.Max(18, CommentList.FontSize * 1.35);
        var target = Math.Clamp(sv.VerticalOffset - pixels, 0, sv.ScrollableHeight);
        sv.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private void CommentList_AutoScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Exit on left/right click while in mode
        if (_commentAutoScroll && e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            EndCommentAutoScroll();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle) return;

        // Toggle: second middle-click exits
        if (_commentAutoScroll)
        {
            EndCommentAutoScroll();
            e.Handled = true;
            return;
        }

        var sv = FindDescendantScrollViewer(CommentList);
        if (sv is null) return;

        StartCommentAutoScroll(e.GetPosition(sv));
        e.Handled = true;
    }

    private void CommentList_AutoScroll_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_commentAutoScroll) return;
        if (e.Key == Key.Escape)
        {
            EndCommentAutoScroll();
            e.Handled = true;
        }
    }

    private void StartCommentAutoScroll(System.Windows.Point originInScrollViewer)
    {
        _commentAutoScroll = true;
        _commentAutoScrollOrigin = originInScrollViewer;
        try { Mouse.OverrideCursor = System.Windows.Input.Cursors.ScrollNS; } catch { /* ignore */ }
        // Capture on the list so moves outside items still count (release on exit)
        try { CommentList.CaptureMouse(); } catch { /* ignore */ }
        WriteLog("comment autoscroll: on");
    }

    private void TickCommentAutoScroll()
    {
        if (!_commentAutoScroll) return;

        // Keep cursor in scroll mode (pointer poll is skipped while active)
        try
        {
            if (!ReferenceEquals(Mouse.OverrideCursor, System.Windows.Input.Cursors.ScrollNS))
                Mouse.OverrideCursor = System.Windows.Input.Cursors.ScrollNS;
        }
        catch { /* ignore */ }

        var sv = FindDescendantScrollViewer(CommentList);
        if (sv is null)
        {
            EndCommentAutoScroll();
            return;
        }

        System.Windows.Point pos;
        try { pos = Mouse.GetPosition(sv); }
        catch { return; }

        var dy = pos.Y - _commentAutoScrollOrigin.Y;
        const double deadZone = 5; // px — no scroll near origin
        if (Math.Abs(dy) <= deadZone)
            return;

        // Distance beyond dead-zone → speed (px per ~16ms tick).
        // Slightly stronger than the old curve so it feels like normal browser autoscroll.
        var dist = Math.Abs(dy) - deadZone;
        var speed = Math.Min(80, 0.5 * dist + 1.1 * (dist * dist) / 80);
        if (dy < 0) speed = -speed; // pointer above origin → scroll up

        var target = sv.VerticalOffset + speed;
        target = Math.Clamp(target, 0, Math.Max(0, sv.ScrollableHeight));
        if (Math.Abs(target - sv.VerticalOffset) >= 0.5)
            sv.ScrollToVerticalOffset(target);
    }

    private void EndCommentAutoScroll()
    {
        if (!_commentAutoScroll) return;
        _commentAutoScroll = false;
        try
        {
            if (CommentList.IsMouseCaptured)
                CommentList.ReleaseMouseCapture();
        }
        catch { /* ignore */ }
        try
        {
            if (ReferenceEquals(Mouse.OverrideCursor, System.Windows.Input.Cursors.ScrollNS) ||
                Mouse.OverrideCursor is not null)
                Mouse.OverrideCursor = null;
        }
        catch { /* ignore */ }
        WriteLog("comment autoscroll: off");
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
            var headerWeight = ParseFontWeight(_settings.CommentHeaderFontWeight, FontWeights.Normal);
            var bodyWeight = ParseFontWeight(_settings.CommentBodyFontWeight, FontWeights.Normal);
            var headerFg = ParseBrush(_settings.CommentHeaderColor, "#888888");
            var bodyFg = ParseBrush(_settings.CommentBodyColor, "#111111");

            // DynamicResource keys used by comment ItemTemplate (A/B style)
            CommentList.Resources["HeaderFontFamily"] = headerFamily;
            CommentList.Resources["HeaderFontSize"] = headerSize;
            CommentList.Resources["HeaderFontWeight"] = headerWeight;
            CommentList.Resources["HeaderForeground"] = headerFg;
            CommentList.Resources["BodyFontFamily"] = bodyFamily;
            CommentList.Resources["BodyFontSize"] = bodySize;
            CommentList.Resources["BodyFontWeight"] = bodyWeight;
            CommentList.Resources["BodyForeground"] = bodyFg;

            CommentList.FontFamily = bodyFamily;
            CommentList.FontSize = bodySize;
            CommentList.FontWeight = bodyWeight;
        }
        catch (Exception ex)
        {
            WriteLog("font apply: " + ex.Message);
        }
    }

    /// <summary>
    /// App chrome skin (write/status bars, caption). Comment list is applied separately.
    /// </summary>
    private void ApplyUiTheme()
    {
        try
        {
            var t = UiTheme.FromId(_settings.UiTheme);

            Background = t.Brush(t.WindowBg);
            RootLayoutGrid.Background = t.Brush(t.WindowBg);

            WriteBar.Background = t.Brush(t.WriteBarBg);
            WriteBar.BorderBrush = t.Brush(t.WriteBarBorder);
            BoardTitleText.Foreground = t.Brush(t.WriteBarMuted);

            WriteBox.Background = t.Brush(t.InputBg);
            WriteBox.Foreground = t.Brush(t.InputFg);
            WriteBox.BorderBrush = t.Brush(t.InputBorder);
            WriteBox.CaretBrush = t.Brush(t.InputFg);

            WriteButton.Background = t.Brush(t.ButtonBg);
            WriteButton.Foreground = t.Brush(t.ButtonFg);
            WriteButton.BorderBrush = t.Brush(t.ButtonBorder);
            WriteButton.BorderThickness = new Thickness(t.UseAccentButton ? 0 : 1);
            try
            {
                if (t.UseAccentButton)
                {
                    WriteButton.Padding = new Thickness(4, 2, 4, 2);
                    WriteButton.FontWeight = FontWeights.SemiBold;
                    WriteButton.Template = CreateFlatButtonTemplate(t.ButtonBg, t.ButtonFg, t.ButtonBorder);
                }
                else
                {
                    WriteButton.ClearValue(System.Windows.Controls.Control.TemplateProperty);
                    WriteButton.FontWeight = FontWeights.Normal;
                }
            }
            catch (Exception ex)
            {
                WriteLog("write button theme: " + ex.Message);
                try { WriteButton.ClearValue(System.Windows.Controls.Control.TemplateProperty); } catch { /* ignore */ }
            }

            InfoBar.Background = t.Brush(t.StatusBg);
            StatusText.Foreground = t.Brush(t.StatusFg);
            VolumeLabel.Foreground = t.Brush(t.StatusFg);
            VolumeText.Foreground = t.Brush(t.StatusFg);
            RightStatsText.Foreground = t.Brush(t.StatusMuted);

            MomentumText.Foreground = t.Brush(t.StatusFg);
            var barH = Math.Clamp(t.MomentumBarHeight, 2, 4);
            MomentumBarTrack.Height = barH;
            MomentumBarFill.Height = barH;
            MomentumBarFill.Background = t.Brush(t.MomentumBar);
            var track = t.MomentumBar;
            MomentumBarTrack.Background = t.Brush(
                System.Windows.Media.Color.FromArgb(0x40, track.R, track.G, track.B));

            PaintMomentumMeter(_momentum.Score);

            CommentSplitter.Background = t.Brush(t.Splitter);

            // Application-level brushes (settings dialog / status StaticResource consumers)
            try
            {
                if (System.Windows.Application.Current?.Resources is { } appRes)
                {
                    appRes["WindowBackgroundBrush"] = t.Brush(t.WindowBg);
                    appRes["StatusBarBrush"] = t.Brush(t.StatusBg);
                    appRes["ForegroundBrush"] = t.Brush(t.StatusFg);
                }
            }
            catch { /* ignore */ }

            _videoChrome?.ApplyTheme(
                t.ChromeBarR, t.ChromeBarG, t.ChromeBarB,
                t.ChromeHoverR, t.ChromeHoverG, t.ChromeHoverB,
                t.ChromePressR, t.ChromePressG, t.ChromePressB,
                t.ChromeTextR, t.ChromeTextG, t.ChromeTextB);

            _fsChromeOverlay?.ApplyTheme(t.ChromeBarR, t.ChromeBarG, t.ChromeBarB);
        }
        catch (Exception ex)
        {
            WriteLog("ui theme: " + ex.Message);
        }

        // Always apply comment theme separately — must not depend on chrome template success
        ApplyCommentListTheme();
    }

    private static ControlTemplate CreateFlatButtonTemplate(
        System.Windows.Media.Color bg, System.Windows.Media.Color fg, System.Windows.Media.Color border)
    {
        // No x: prefix (avoids undeclared xmlns:x). Colors as #AARRGGBB.
        var bgHex = $"#FF{bg.R:X2}{bg.G:X2}{bg.B:X2}";
        var bdHex = $"#FF{border.R:X2}{border.G:X2}{border.B:X2}";
        var xaml =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<Border Name='Bd' Background='" + bgHex + "' BorderBrush='" + bdHex +
            "' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "</Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'>" +
            "<Setter TargetName='Bd' Property='Opacity' Value='0.92'/>" +
            "</Trigger>" +
            "<Trigger Property='IsPressed' Value='True'>" +
            "<Setter TargetName='Bd' Property='Opacity' Value='0.85'/>" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate>";
        return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    /// <summary>
    /// Comment list layout + palette. Independent of chrome <see cref="UiTheme"/>.
    /// </summary>
    private void ApplyCommentListTheme()
    {
        try
        {
            var ct = CommentListTheme.FromId(_settings.CommentListTheme);
            var freeze = (System.Windows.Media.Color c) =>
            {
                var b = new System.Windows.Media.SolidColorBrush(c);
                if (b.CanFreeze) b.Freeze();
                return b;
            };

            // Palette resources for Flat / Card templates
            Resources["CommentRowBorder"] = freeze(ct.RowBorder);
            Resources["CommentRowHover"] = freeze(ct.RowHover);
            Resources["CommentRowNewBg"] = freeze(ct.RowNewBg);
            Resources["CommentRowNewBorder"] = freeze(ct.RowNewBorder);
            Resources["CommentCardBg"] = freeze(ct.CardBg);
            Resources["CommentCardBorder"] = freeze(ct.CardBorder);
            Resources["CommentCardHoverBorder"] = freeze(ct.CardHoverBorder);
            Resources["CommentCardNewBg"] = freeze(ct.CardNewBg);
            Resources["CommentCardNewBorder"] = freeze(ct.CardNewBorder);
            Resources["CommentCardNewAccent"] = freeze(ct.CardNewAccent);
            Resources["CommentCardAccentIdle"] = freeze(ct.CardAccentIdle);
            Resources["CommentBadgeBg"] = freeze(ct.BadgeBg);
            Resources["CommentBadgeFg"] = freeze(ct.BadgeFg);
            Resources["CommentCardCornerRadius"] = new CornerRadius(ct.CardCornerRadius);
            Resources["CommentCardInnerPadding"] = ct.CardInnerPadding;
            Resources["CommentItemPadding"] = ct.ItemPadding;
            Resources["CommentItemMargin"] = ct.ItemMargin;

            CommentPanel.Background = freeze(ct.PanelBg);
            CommentPanel.BorderBrush = freeze(ct.PanelBorder);
            CommentList.Padding = ct.ListPadding;

            var jumpAccent = ct.CardNewAccent;
            var jumpLuma = 0.299 * jumpAccent.R + 0.587 * jumpAccent.G + 0.114 * jumpAccent.B;
            var jumpFg = jumpLuma > 160
                ? System.Windows.Media.Colors.Black
                : System.Windows.Media.Colors.White;
            NewPostsJumpButton.Background = freeze(jumpAccent);
            NewPostsJumpButton.Foreground = freeze(jumpFg);
            NewPostsJumpButton.BorderBrush = freeze(jumpAccent);
            NewPostsJumpButton.BorderThickness = new Thickness(0);
            try
            {
                NewPostsJumpButton.Template = CreateFlatButtonTemplate(jumpAccent, jumpFg, jumpAccent);
            }
            catch { /* keep default chrome */ }

            if (ct.Layout == CommentLayoutKind.Card)
            {
                CommentList.ItemContainerStyle = (Style)FindResource("CommentItemStyleCard");
                CommentList.ItemTemplate = (DataTemplate)FindResource("CommentItemTemplateCard");
                CommentList.SelectionMode = System.Windows.Controls.SelectionMode.Single;
                CommentList.SelectedIndex = -1;
            }
            else
            {
                CommentList.ItemContainerStyle = (Style)FindResource("CommentItemStyleFlat");
                CommentList.ItemTemplate = (DataTemplate)FindResource("CommentItemTemplateFlat");
                CommentList.SelectionMode = System.Windows.Controls.SelectionMode.Extended;
            }

            // Dark themes: if body/header still use light-panel defaults, switch to readable colors
            ApplyCommentColorSuggestions(ct);

            // Force container recycle so style swap paints immediately
            var src = CommentList.ItemsSource;
            CommentList.ItemsSource = null;
            CommentList.ItemsSource = src;
            WriteLog("comment list theme: " + ct.Id);
        }
        catch (Exception ex)
        {
            WriteLog("comment theme: " + ex.Message);
        }
    }

    /// <summary>
    /// When switching skins, auto-adjust body/header colors if they still match a
    /// known theme default (does not override arbitrary custom colors).
    /// </summary>
    private void ApplyCommentColorSuggestions(CommentListTheme ct)
    {
        // Any suggested color from known skins counts as "theme default"
        var knownBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#111111", "#000000", "#E8E8E8", "#E8E8F0", "#FFFFFF",
            "#F5F3FF", "#3B0A2A", "#422006", "#0F172A", "#86EFAC",
        };
        var knownHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#888888", "#777777", "#9A9A9A", "#9A9AA8",
            "#A78BFA", "#BE185D", "#A16207", "#2563EB", "#4ADE80",
        };
        foreach (var t in CommentListTheme.All)
        {
            if (t.SuggestedBodyColor is not null) knownBodies.Add(t.SuggestedBodyColor);
            if (t.SuggestedHeaderColor is not null) knownHeaders.Add(t.SuggestedHeaderColor);
        }

        var changed = false;
        if (ct.SuggestedBodyColor is not null &&
            (string.IsNullOrWhiteSpace(_settings.CommentBodyColor) ||
             knownBodies.Contains(_settings.CommentBodyColor.Trim())))
        {
            if (!string.Equals(_settings.CommentBodyColor, ct.SuggestedBodyColor, StringComparison.OrdinalIgnoreCase))
            {
                _settings.CommentBodyColor = ct.SuggestedBodyColor;
                changed = true;
            }
        }
        if (ct.SuggestedHeaderColor is not null &&
            (string.IsNullOrWhiteSpace(_settings.CommentHeaderColor) ||
             knownHeaders.Contains(_settings.CommentHeaderColor.Trim())))
        {
            if (!string.Equals(_settings.CommentHeaderColor, ct.SuggestedHeaderColor, StringComparison.OrdinalIgnoreCase))
            {
                _settings.CommentHeaderColor = ct.SuggestedHeaderColor;
                changed = true;
            }
        }

        if (changed)
        {
            ApplyCommentFont();
            try { _settings.Save(); } catch { /* ignore */ }
        }
    }

    private static FontWeight ParseFontWeight(string? name, FontWeight fallback)
    {
        return (name ?? "").Trim().ToLowerInvariant() switch
        {
            "thin" or "ultralight" or "extralight" or "light" => FontWeights.Light,
            "normal" or "regular" => FontWeights.Normal,
            "medium" => FontWeights.Medium,
            "semibold" or "demibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            "extrabold" or "ultrabold" or "black" or "heavy" => FontWeights.Bold,
            _ => fallback,
        };
    }

    private static System.Windows.Media.SolidColorBrush ParseBrush(string? hex, string fallbackHex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(hex) ? fallbackHex : hex.Trim())!;
            var b = new System.Windows.Media.SolidColorBrush(c);
            if (b.CanFreeze) b.Freeze();
            return b;
        }
        catch
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex)!;
            var b = new System.Windows.Media.SolidColorBrush(c);
            if (b.CanFreeze) b.Freeze();
            return b;
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
        _preFullscreenWidth = Width;
        _preFullscreenHeight = Height;
        _preFullscreenLeft = Left;
        _preFullscreenTop = Top;
        if (_sizingHook is not null) _sizingHook.Enabled = false;

        // Suppress intermediate paints: zero chrome + maximize as one visual step
        // (otherwise: bars collapse → video expands in windowed → then maximize = びよん)
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetWindowRedraw(hwnd, false);
        try
        {
            // Multi-line write freeze is window-layout specific
            UnfreezeContentRow();

            _isFullscreen = true;
            AttachFsChromeFloat();
            SetFullscreenChromeVisible(false);

            // Single transition to maximized (avoid Normal→Maximized double layout when already Normal)
            if (WindowState != WindowState.Maximized)
                WindowState = WindowState.Maximized;
            else
            {
                // Already maximized via caption: force a clean max layout after chrome detach
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }

            SetFullscreenChromeVisible(false);
        }
        finally
        {
            SetWindowRedraw(hwnd, true);
        }

        UpdateMaximizeButtonGlyph();
        WriteLog("fullscreen: enter (redraw suspended for chrome+maximize)");
    }

    private void ExitFullscreen()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetWindowRedraw(hwnd, false);
        try
        {
            // Put bars back WHILE still maximized so proportions match windowed layout
            // before the size change. Unmaximize-first left rows=0 → video full-bleed in a
            // small window, then bars returned and video shrank (= exit びよん).
            DetachFsChromeFloat();
            _isFullscreen = false;

            // Multi-line write height while layout is already windowed-chrome (still max frame)
            if (WriteBox.Height > WriteBoxSingleLineHeight + 0.5)
                ApplyWriteBoxHeightChange(WriteBoxSingleLineHeight, WriteBox.Height);

            // Restore geometry in one step (avoid Normal's intermediate restore-bounds flicker)
            WindowState = WindowState.Normal;
            if (_preFullscreenWidth > 0) Width = _preFullscreenWidth;
            if (_preFullscreenHeight > 0) Height = _preFullscreenHeight;
            Left = _preFullscreenLeft;
            Top = _preFullscreenTop;

            try { UpdateLayout(); } catch { /* ignore */ }
        }
        finally
        {
            SetWindowRedraw(hwnd, true);
        }

        if (_sizingHook is not null) _sizingHook.Enabled = true;
        UpdateMaximizeButtonGlyph();
        WriteLog("fullscreen: exit (bars first, then restore size)");
    }

    /// <summary>
    /// Freeze WM paints while we mutate layout + WindowState so the user never sees
    /// intermediate sizes (the FS 「びよん」).
    /// </summary>
    private static void SetWindowRedraw(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            SendMessage(hwnd, WM_SETREDRAW, enable ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
            if (enable)
            {
                RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                    RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdatenow);
            }
        }
        catch
        {
            // ignore — worst case is the old multi-step bounce
        }
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
        try
        {
            var t = UiTheme.FromId(_settings.UiTheme);
            _fsChromeOverlay.ApplyTheme(t.ChromeBarR, t.ChromeBarG, t.ChromeBarB);
        }
        catch { /* ignore */ }
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
        try { _postCooldownTimer?.Stop(); } catch { /* ignore */ }
        try { _newHighlightTimer?.Stop(); } catch { /* ignore */ }
        try { _idRefLeaveTimer?.Stop(); } catch { /* ignore */ }
        try { _idRefHoverTimer?.Stop(); } catch { /* ignore */ }
        try { HideIdRefPopup(); } catch { /* ignore */ }
        try { CloseImagePopup(); } catch { /* ignore */ }
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
        try { _postCooldownTimer?.Stop(); } catch { }
        try { _newHighlightTimer?.Stop(); } catch { }
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
        try { _snapHook?.Dispose(); } catch { }
        _snapHook = null;

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
        Debug.WriteLine("[DSPlayer] " + message);
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
