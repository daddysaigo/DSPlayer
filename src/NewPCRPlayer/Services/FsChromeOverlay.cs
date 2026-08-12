using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfSize = System.Windows.Size;

namespace NewPCRPlayer.Services;

/// <summary>
/// Fullscreen write/status as an owned tool window over the player (mpv airspace).
/// Positioned strictly to <see cref="Screen.Bounds"/> bottom strip — never wider than the monitor.
/// </summary>
public sealed class FsChromeOverlay : Form
{
    private readonly ElementHost _host;
    private readonly WpfControls.StackPanel _panel;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private IntPtr _ownerHwnd;
    private bool _ownerAssigned;
    private Rectangle _lastScreenRect;
    private int _fixedContentPx;

    private const int WsExToolwindow = 0x00000080;
    private const int GwlHwndparent = -8;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private const uint SwpHidewindow = 0x0080;

    private Color _barColor = Color.FromArgb(0x1A, 0x1A, 0x1A);

    // Write(~36-40) + Info(~28-32) + multi-line write up to ~120 → hard cap
    private const int MinContentPx = 56;
    private const int MaxContentPx = 180;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public WpfControls.StackPanel Panel => _panel;

    /// <summary>Match FS float strip to app chrome (opaque).</summary>
    public void ApplyTheme(byte r, byte g, byte b)
    {
        if (IsDisposed) return;
        _barColor = Color.FromArgb(r, g, b);
        BackColor = _barColor;
        try { _host.BackColor = _barColor; } catch { /* ignore */ }
        try
        {
            _panel.Background = new WpfMedia.SolidColorBrush(
                WpfMedia.Color.FromRgb(r, g, b));
        }
        catch { /* ignore */ }
    }

    public FsChromeOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        TopMost = false;
        BackColor = _barColor;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        Size = new System.Drawing.Size(640, 72);

        _panel = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Vertical,
            Background = new WpfMedia.SolidColorBrush(
                WpfMedia.Color.FromRgb(0x1A, 0x1A, 0x1A)),
            // Clip anything that would paint past the form
            ClipToBounds = true
        };

        _host = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = _panel,
            BackColor = _barColor,
            AutoSize = false
        };
        Controls.Add(_host);

        _syncTimer = new System.Windows.Forms.Timer { Interval = 32 };
        _syncTimer.Tick += (_, _) =>
        {
            if (IsDisposed || !Visible) return;
            SyncToMonitor();
            RaiseAboveOwner();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolwindow;
            return cp;
        }
    }

    /// <summary>
    /// Bind to owner HWND without flashing the chrome on screen.
    /// (Show()+Visible=false was briefly painting the bar and caused size jumps.)
    /// </summary>
    public void Attach(IntPtr ownerHwnd)
    {
        if (ownerHwnd == IntPtr.Zero)
            throw new ArgumentException("ownerHwnd");
        _ownerHwnd = ownerHwnd;

        if (!IsHandleCreated)
            CreateHandle();

        AssignOwner(ownerHwnd);
        // Stay hidden until ShowChrome — never call Show() here
        Visible = false;
        _fixedContentPx = 0;
        SyncToMonitor();
        _syncTimer.Start();
    }

    public void Detach()
    {
        try { _syncTimer.Stop(); } catch { /* ignore */ }
        HideChrome();
        _ownerHwnd = IntPtr.Zero;
        _ownerAssigned = false;
        _fixedContentPx = 0;
    }

    private void AssignOwner(IntPtr ownerHwnd)
    {
        if (_ownerAssigned && IsHandleCreated) return;
        try
        {
            // Owned-by relationship without Show(owner) flash
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(Handle, GwlHwndparent, ownerHwnd);
            else
                SetWindowLong32(Handle, GwlHwndparent, ownerHwnd.ToInt32());
            _ownerAssigned = true;
        }
        catch
        {
            // ignore — still works as tool window, may z-order worse
        }
    }

    /// <summary>Pin to the bottom edge of the owner's monitor. Width == monitor width exactly.</summary>
    public void SyncToMonitor()
    {
        if (IsDisposed || _ownerHwnd == IntPtr.Zero || !IsHandleCreated) return;
        try
        {
            var mon = Screen.FromHandle(_ownerHwnd).Bounds;
            var contentPx = MeasureContentHeightPx(mon.Width);
            contentPx = Math.Clamp(contentPx, MinContentPx, MaxContentPx);
            _fixedContentPx = contentPx;

            var x = mon.Left;
            var y = mon.Bottom - contentPx;
            var w = mon.Width;
            var h = contentPx;

            _lastScreenRect = new Rectangle(x, y, w, h);

            // Constrain WPF root to form width (device-independent ≈ px at 100% scale)
            _panel.MaxWidth = w;
            _panel.Width = w;
            foreach (UIElement child in _panel.Children)
            {
                if (child is FrameworkElement fe)
                {
                    fe.MaxWidth = w;
                    fe.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                }
            }

            // Physical screen pixels only — SetWindowPos bypasses WinForms Location scaling
            var flags = SwpNoactivate | (Visible ? SwpShowwindow : SwpHidewindow);
            SetWindowPos(Handle, HwndTop, x, y, w, h, flags);

            // Keep managed Bounds in sync so hit-tests match
            if (Bounds != _lastScreenRect)
            {
                try { Bounds = _lastScreenRect; } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore layout races
        }
    }

    private int MeasureContentHeightPx(int widthPx)
    {
        // At 100% DPI, px == DIP. Avoid DeviceDpi games that previously mis-sized width/height.
        var widthDip = Math.Max(1, widthPx);

        _panel.Width = widthDip;
        _panel.MaxWidth = widthDip;

        double childSum = 0;
        foreach (UIElement child in _panel.Children)
        {
            if (child is not FrameworkElement fe) continue;
            fe.MaxWidth = widthDip;
            fe.Measure(new WpfSize(widthDip, double.PositiveInfinity));
            var ch = fe.DesiredSize.Height;
            if (fe.ActualHeight > ch)
                ch = fe.ActualHeight;
            if (ch < 1)
                ch = 36; // XAML-ish single-line bar fallback
            childSum += ch;
        }

        if (childSum < 1)
        {
            _panel.Measure(new WpfSize(widthDip, double.PositiveInfinity));
            childSum = _panel.DesiredSize.Height;
        }

        if (childSum < 1)
            childSum = 72;

        return (int)Math.Ceiling(childSum) + 2;
    }

    public void RaiseAboveOwner()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        try
        {
            SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        }
        catch { /* ignore */ }
    }

    public void ShowChrome()
    {
        if (IsDisposed || _ownerHwnd == IntPtr.Zero) return;
        if (!IsHandleCreated)
            CreateHandle();
        AssignOwner(_ownerHwnd);

        SyncToMonitor();
        Visible = true;
        // Second pass after Visible so ActualHeight of WriteBar/InfoBar is real
        SyncToMonitor();
        RaiseAboveOwner();
        try
        {
            Invalidate(true);
            Refresh();
        }
        catch { /* ignore */ }
    }

    public void HideChrome()
    {
        if (IsDisposed) return;

        // Always drop focus — click-to-type must not pin the bar after mouse leaves
        try
        {
            if (_panel.IsKeyboardFocusWithin)
                Keyboard.ClearFocus();
        }
        catch { /* ignore */ }

        try
        {
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNomove | SwpNosize | SwpNoactivate | SwpHidewindow);
            }
        }
        catch { /* ignore */ }

        if (Visible)
            Visible = false;
    }

    public bool ContainsScreenPoint(System.Drawing.Point screen)
    {
        if (IsDisposed || !Visible) return false;
        // Prefer last applied rect (monitor-clamped) over raw GetWindowRect which can include garbage
        var r = _lastScreenRect.Width > 0 ? _lastScreenRect : Bounds;
        if (r.Width < 1 || r.Height < 1)
        {
            if (IsHandleCreated && GetWindowRect(Handle, out var wr))
                r = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
            else
                return false;
        }

        // Only the actual bar strip — not a tall mis-measured rect
        var h = _fixedContentPx > 0 ? _fixedContentPx : Math.Min(r.Height, MaxContentPx);
        var top = r.Bottom - h;
        return screen.X >= r.Left && screen.X < r.Right
               && screen.Y >= top && screen.Y < r.Bottom;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _syncTimer.Stop(); } catch { /* ignore */ }
            try { _syncTimer.Dispose(); } catch { /* ignore */ }
            try
            {
                _host.Child = null;
                _host.Dispose();
            }
            catch { /* ignore */ }
        }

        base.Dispose(disposing);
    }
}
