using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewPCRPlayer.Services;

/// <summary>
/// Min/Max/Close chrome as an <b>owned</b> tool window (not a free-floating app window).
/// <para>
/// Parenting WinForms controls under the mpv <c>wid</c> panel is clickable but invisible:
/// mpv's D3D/GL VO paints over GDI siblings every frame. A small owned top-level HWND
/// paints above the owner's HwndHost (standard WPF airspace workaround) while still
/// minimizing/moving with the parent via Win32 ownership.
/// </para>
/// </summary>
public sealed class VideoChromeOverlay : Form
{
    private readonly Button _min;
    private readonly Button _max;
    private readonly Button _close;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private Control? _videoPanel;

    public const int BarHeight = 28;
    public const int ButtonWidth = 40;
    public const int HotWidth = 140;
    public const int HotHeight = 44;

    // DEBUG: loud opaque colors until visibility is confirmed
    private static readonly Color DebugBarColor = Color.Magenta;
    private static readonly Color DebugMinColor = Color.Lime;
    private static readonly Color DebugMaxColor = Color.Cyan;
    private static readonly Color DebugCloseColor = Color.OrangeRed;
    private static readonly Color DebugTextColor = Color.Black;

    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExLayered = 0x00080000;

    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public event EventHandler? MinimizeClick;
    public event EventHandler? MaximizeClick;
    public event EventHandler? CloseClick;

    public VideoChromeOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        TopMost = false; // ownership — not global topmost float
        BackColor = DebugBarColor;
        ForeColor = DebugTextColor;
        Size = new Size(ButtonWidth * 3, BarHeight);
        AutoScaleMode = AutoScaleMode.None;
        // Don't steal focus from main player
        SetStyle(ControlStyles.Selectable, false);

        _min = MakeButton("─", "最小化", DebugMinColor);
        _max = MakeButton("□", "最大化", DebugMaxColor);
        _close = MakeButton("✕", "閉じる", DebugCloseColor);
        _close.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xFF, 0x40, 0x40);
        _close.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xC5, 0x0F, 0x1F);

        _min.Click += (_, _) => MinimizeClick?.Invoke(this, EventArgs.Empty);
        _max.Click += (_, _) => MaximizeClick?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => CloseClick?.Invoke(this, EventArgs.Empty);

        Controls.Add(_close);
        Controls.Add(_max);
        Controls.Add(_min);
        LayoutButtons();

        // Keep glued to video top-right (move/resize parent, DPI, etc.)
        _syncTimer = new System.Windows.Forms.Timer { Interval = 32 };
        _syncTimer.Tick += (_, _) =>
        {
            if (IsDisposed || _videoPanel is null) return;
            SyncToVideoPanel();
            if (Visible)
                RaiseAboveOwner();
        };
    }

    /// <summary>Do not activate when shown (avoids focus fight with main window).</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolwindow | WsExNoactivate;
            // Not layered yet (debug solid). Layered can be re-enabled for alpha later.
            return cp;
        }
    }

    public void SetMaximizedGlyph(bool restored)
    {
        if (IsDisposed) return;
        _max.Text = restored ? "❐" : "□";
        _max.AccessibleName = restored ? "元のサイズに戻す" : "最大化";
    }

    /// <summary>
    /// Bind to main window HWND as owner and to the video panel for geometry.
    /// </summary>
    public void Attach(IWin32Window owner, Control videoPanel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(videoPanel);
        _videoPanel = videoPanel;

        videoPanel.Resize -= OnVideoResized;
        videoPanel.SizeChanged -= OnVideoResized;
        videoPanel.Resize += OnVideoResized;
        videoPanel.SizeChanged += OnVideoResized;

        // Show once to establish ownership, then hide
        if (!Visible)
        {
            try
            {
                Show(owner);
            }
            catch
            {
                // Fallback without owner if handle not ready
                Show();
            }
        }

        Visible = false;
        SyncToVideoPanel();
        _syncTimer.Start();
    }

    private void OnVideoResized(object? sender, EventArgs e) => SyncToVideoPanel();

    public void SyncToVideoPanel()
    {
        if (_videoPanel is null || _videoPanel.IsDisposed || IsDisposed)
            return;
        try
        {
            if (!_videoPanel.IsHandleCreated)
                return;

            var w = _videoPanel.ClientSize.Width;
            var h = _videoPanel.ClientSize.Height;
            if (w < 1 || h < 1) return;

            Width = ButtonWidth * 3;
            Height = BarHeight;
            LayoutButtons();

            // Screen position of video panel top-right
            var pt = _videoPanel.PointToScreen(new Point(Math.Max(0, w - Width), 0));
            if (Location != pt)
                Location = pt;
        }
        catch
        {
            // ignore transient handle issues
        }
    }

    public void RaiseAboveOwner()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        try
        {
            // Stay above owner content (including HwndHost) without TopMost=true over all apps
            SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        }
        catch
        {
            // ignore
        }
    }

    public void ShowChrome()
    {
        if (IsDisposed) return;
        SyncToVideoPanel();
        if (!Visible)
        {
            Visible = true;
            // Ensure solid debug paint
            BackColor = DebugBarColor;
            _min.BackColor = DebugMinColor;
            _max.BackColor = DebugMaxColor;
            _close.BackColor = DebugCloseColor;
        }
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
        if (Visible)
            Visible = false;
    }

    public bool IsMouseOverChrome(Point clientOnVideoPanel)
    {
        if (_videoPanel is null) return false;
        // Chrome occupies top-right Width x Height of video client
        var r = new Rectangle(
            Math.Max(0, _videoPanel.ClientSize.Width - Width),
            0,
            Width,
            Height);
        r.Inflate(6, 6);
        return r.Contains(clientOnVideoPanel);
    }

    public static bool IsInHotZone(Point clientOnParent, Size parentClientSize)
    {
        if (parentClientSize.Width <= 0 || parentClientSize.Height <= 0)
            return false;
        if (clientOnParent.X < 0 || clientOnParent.Y < 0)
            return false;
        if (clientOnParent.X >= parentClientSize.Width || clientOnParent.Y >= parentClientSize.Height)
            return false;

        return clientOnParent.X >= parentClientSize.Width - HotWidth
               && clientOnParent.Y <= HotHeight;
    }

    private void LayoutButtons()
    {
        _min.SetBounds(0, 0, ButtonWidth, BarHeight);
        _max.SetBounds(ButtonWidth, 0, ButtonWidth, BarHeight);
        _close.SetBounds(ButtonWidth * 2, 0, ButtonWidth, BarHeight);
    }

    private static Button MakeButton(string text, string tip, Color back)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = DebugTextColor,
            Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Arrow,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.Black;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back);
        var tt = new ToolTip { ShowAlways = false, AutoPopDelay = 2000 };
        tt.SetToolTip(b, tip);
        return b;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var br = new SolidBrush(DebugBarColor);
        e.Graphics.FillRectangle(br, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var br = new SolidBrush(DebugBarColor))
            e.Graphics.FillRectangle(br, ClientRectangle);
        using (var pen = new Pen(Color.Yellow, 2))
            e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _syncTimer.Stop(); _syncTimer.Dispose(); } catch { /* ignore */ }
            if (_videoPanel is not null)
            {
                try { _videoPanel.Resize -= OnVideoResized; } catch { /* ignore */ }
                try { _videoPanel.SizeChanged -= OnVideoResized; } catch { /* ignore */ }
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>Adapter so WPF HWND can own a WinForms Form.</summary>
public sealed class Win32WindowHandle : IWin32Window
{
    public Win32WindowHandle(IntPtr handle) => Handle = handle;
    public IntPtr Handle { get; }
}
