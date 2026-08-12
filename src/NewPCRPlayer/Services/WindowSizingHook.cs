using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewPCRPlayer.Services;

/// <summary>
/// Native window interaction for borderless WPF:
/// <list type="bullet">
/// <item><b>WM_NCHITTEST</b> — resize grips only on the VIDEO rect (not whole window / not BBS).</item>
/// <item><b>WM_SIZING</b> — correct RECT during drag so VIDEO area keeps aspect ratio
/// (never sets Window.Width/Height from SizeChanged).</item>
/// </list>
/// </summary>
public sealed class WindowSizingHook : IDisposable
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SIZING = 0x0214;

    // HT* results
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    // WMSZ edges
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    /// <summary>Resize grip thickness inside the video rect, in DIPs (~10–16).</summary>
    public const double DefaultGripDip = 14;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _disposed;

    public WindowSizingHook(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public double VideoAspect { get; set; } = 16.0 / 9.0;
    public bool Enabled { get; set; } = true;
    public double GripDip { get; set; } = DefaultGripDip;

    /// <summary>Video surface in window client DIPs (relative to window content root).</summary>
    public Func<Rect>? GetVideoRectDip { get; set; }

    /// <summary>Width outside video column (comment + splitter + borders), DIPs.</summary>
    public Func<double>? GetSideChromeDip { get; set; }

    /// <summary>Height outside video row (write + info + borders + optional caption), DIPs.</summary>
    public Func<double>? GetBottomChromeDip { get; set; }

    /// <summary>Extra top chrome that is still "window" not video (e.g. caption strip).</summary>
    public Func<double>? GetTopChromeDip { get; set; }

    public void Attach()
    {
        if (_source is not null) return;
        var helper = new WindowInteropHelper(_window);
        if (helper.Handle == IntPtr.Zero)
            throw new InvalidOperationException("Window handle is not ready.");

        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!Enabled)
            return IntPtr.Zero;

        if (msg == WM_NCHITTEST)
            return HandleNcHitTest(lParam, ref handled);

        if (msg == WM_SIZING)
            return HandleSizing(wParam, lParam);

        return IntPtr.Zero;
    }

    private IntPtr HandleNcHitTest(IntPtr lParam, ref bool handled)
    {
        if (_window.WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var videoDip = GetVideoRectDip?.Invoke() ?? Rect.Empty;
        if (videoDip.IsEmpty || videoDip.Width < 8 || videoDip.Height < 8)
            return IntPtr.Zero;

        // lParam: screen coords
        int sx = (short)(lParam.ToInt64() & 0xFFFF);
        int sy = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        // Screen → window client DIPs
        var dpi = VisualTreeHelperDpi(_window);
        var helper = new WindowInteropHelper(_window);
        if (!ScreenToClient(helper.Handle, ref sx, ref sy))
            return IntPtr.Zero;

        var xDip = sx / dpi.DpiScaleX;
        var yDip = sy / dpi.DpiScaleY;

        // Only video rect participates in resize / caption drag
        if (!videoDip.Contains(xDip, yDip))
        {
            // Let WPF handle BBS / write bar / etc. as client
            return IntPtr.Zero;
        }

        var grip = Math.Clamp(GripDip, 10, 16);
        var left = xDip - videoDip.Left < grip;
        var right = videoDip.Right - xDip < grip;
        var top = yDip - videoDip.Top < grip;
        var bottom = videoDip.Bottom - yDip < grip;

        int ht;
        if (top && left) ht = HTTOPLEFT;
        else if (top && right) ht = HTTOPRIGHT;
        else if (bottom && left) ht = HTBOTTOMLEFT;
        else if (bottom && right) ht = HTBOTTOMRIGHT;
        else if (left) ht = HTLEFT;
        else if (right) ht = HTRIGHT;
        else if (top) ht = HTTOP;
        else if (bottom) ht = HTBOTTOM;
        else ht = HTCAPTION; // center of video = drag window

        handled = true;
        return new IntPtr(ht);
    }

    private IntPtr HandleSizing(IntPtr wParam, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return IntPtr.Zero;
        if (_window.WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var aspect = VideoAspect;
        if (aspect is < 0.2 or > 5.0)
            return IntPtr.Zero;

        var dpi = VisualTreeHelperDpi(_window);
        var sidePx = Math.Max(0, (GetSideChromeDip?.Invoke() ?? 0) * dpi.DpiScaleX);
        var bottomPx = Math.Max(0, (GetBottomChromeDip?.Invoke() ?? 0) * dpi.DpiScaleY);
        var topPx = Math.Max(0, (GetTopChromeDip?.Invoke() ?? 0) * dpi.DpiScaleY);

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var edge = wParam.ToInt32();

        var winW = (double)(rect.Right - rect.Left);
        var winH = (double)(rect.Bottom - rect.Top);

        // Video area inside proposed window
        var videoW = Math.Max(1, winW - sidePx);
        var videoH = Math.Max(1, winH - bottomPx - topPx);

        var changingWidth = edge is WMSZ_LEFT or WMSZ_RIGHT or WMSZ_TOPLEFT or WMSZ_TOPRIGHT
            or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
        var changingHeight = edge is WMSZ_TOP or WMSZ_BOTTOM or WMSZ_TOPLEFT or WMSZ_TOPRIGHT
            or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;

        if (changingWidth && changingHeight)
        {
            var hFromW = videoW / aspect;
            var wFromH = videoH * aspect;
            if (Math.Abs(videoH - hFromW) <= Math.Abs(videoW - wFromH))
            {
                videoH = videoW / aspect;
                ApplyHeight(ref rect, edge, videoH + bottomPx + topPx);
            }
            else
            {
                videoW = videoH * aspect;
                ApplyWidth(ref rect, edge, videoW + sidePx);
            }
        }
        else if (changingWidth)
        {
            videoH = videoW / aspect;
            ApplyHeight(ref rect, edge, videoH + bottomPx + topPx);
        }
        else if (changingHeight)
        {
            videoW = videoH * aspect;
            ApplyWidth(ref rect, edge, videoW + sidePx);
        }

        Marshal.StructureToPtr(rect, lParam, false);
        return IntPtr.Zero;
    }

    private static void ApplyHeight(ref NativeRect rect, int edge, double winH)
    {
        var h = Math.Max(100, (int)Math.Round(winH));
        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
            rect.Top = rect.Bottom - h;
        else
            rect.Bottom = rect.Top + h;
    }

    private static void ApplyWidth(ref NativeRect rect, int edge, double winW)
    {
        var w = Math.Max(200, (int)Math.Round(winW));
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
            rect.Left = rect.Right - w;
        else
            rect.Right = rect.Left + w;
    }

    private static bool ScreenToClient(IntPtr hwnd, ref int x, ref int y)
    {
        var p = new NativePoint { X = x, Y = y };
        if (!ScreenToClient(hwnd, ref p))
            return false;
        x = p.X;
        y = p.Y;
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    private static DpiScale VisualTreeHelperDpi(Window window)
    {
        try { return VisualTreeHelper.GetDpi(window); }
        catch { return new DpiScale(1, 1); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }
}
