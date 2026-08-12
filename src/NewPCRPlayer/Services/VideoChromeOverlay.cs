using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewPCRPlayer.Services;

/// <summary>
/// Min / Max / Close as WinForms children of the video panel (mpv wid host).
/// Must raise HWND above mpv's native VO sibling via SetWindowPos; WinForms
/// BringToFront alone is not enough.
/// </summary>
public sealed class VideoChromeOverlay : Panel
{
    private readonly Button _min;
    private readonly Button _max;
    private readonly Button _close;
    private readonly System.Windows.Forms.Timer _zOrderTimer;
    private readonly System.Windows.Forms.Timer _repaintTimer;

    public const int BarHeight = 28;
    public const int ButtonWidth = 40;
    /// <summary>Top-right hover zone (must beat resize grip ~14px).</summary>
    public const int HotWidth = 140;
    public const int HotHeight = 44;

    // DEBUG: opaque loud colors so "clickable but invisible" is impossible to miss.
    // Later: tone down to semi-opaque dark chrome.
    private static readonly Color DebugBarColor = Color.Magenta;
    private static readonly Color DebugMinColor = Color.Lime;
    private static readonly Color DebugMaxColor = Color.Cyan;
    private static readonly Color DebugCloseColor = Color.OrangeRed;
    private static readonly Color DebugTextColor = Color.Black;

    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprc, IntPtr hrgn, uint flags);

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwFrame = 0x0400;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdatenow = 0x0100;

    public event EventHandler? MinimizeClick;
    public event EventHandler? MaximizeClick;
    public event EventHandler? CloseClick;

    public VideoChromeOverlay()
    {
        Height = BarHeight;
        Width = ButtonWidth * 3;
        // Opaque — WinForms BackColor alpha is unreliable over mpv
        BackColor = DebugBarColor;
        ForeColor = DebugTextColor;
        Visible = false;
        TabStop = false;
        // Opaque painting path (no "transparent" panel tricks)
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Opaque |
            ControlStyles.ResizeRedraw,
            true);
        UpdateStyles();

        _min = MakeButton("─", "最小化", DebugMinColor);
        _max = MakeButton("□", "最大化", DebugMaxColor);
        _close = MakeButton("✕", "閉じる", DebugCloseColor);

        _min.Click += (_, _) => MinimizeClick?.Invoke(this, EventArgs.Empty);
        _max.Click += (_, _) => MaximizeClick?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => CloseClick?.Invoke(this, EventArgs.Empty);

        Controls.Add(_close);
        Controls.Add(_max);
        Controls.Add(_min);
        LayoutButtons();

        // Keep above mpv's native child after it (re)creates surfaces
        _zOrderTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _zOrderTimer.Tick += (_, _) =>
        {
            if (IsDisposed || Parent is null) return;
            Reposition();
            if (Visible)
            {
                RaiseZOrder();
                ForceRepaint();
            }
        };

        // Extra repaint while visible (mpv can overwrite the surface)
        _repaintTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _repaintTimer.Tick += (_, _) =>
        {
            if (IsDisposed || !Visible) return;
            ForceRepaint();
        };
    }

    public void SetMaximizedGlyph(bool restored)
    {
        _max.Text = restored ? "❐" : "□";
        _max.AccessibleName = restored ? "元のサイズに戻す" : "最大化";
        if (Visible)
            ForceRepaint();
    }

    public void AttachTo(Control videoPanel)
    {
        ArgumentNullException.ThrowIfNull(videoPanel);
        if (Parent is not null)
            Parent.Controls.Remove(this);

        videoPanel.Controls.Add(this);
        if (!IsHandleCreated)
            CreateControl();

        videoPanel.Resize -= ParentOnResize;
        videoPanel.Resize += ParentOnResize;
        videoPanel.SizeChanged -= ParentOnResize;
        videoPanel.SizeChanged += ParentOnResize;

        Reposition();
        RaiseZOrder();
        HideChrome();
        _zOrderTimer.Start();
    }

    private void ParentOnResize(object? sender, EventArgs e) => Reposition();

    public void Reposition()
    {
        if (Parent is null || Parent.IsDisposed) return;
        var w = Parent.ClientSize.Width;
        var h = Parent.ClientSize.Height;
        if (w < 1 || h < 1) return;

        Width = ButtonWidth * 3;
        Height = BarHeight;
        Left = Math.Max(0, w - Width);
        Top = 0;
        LayoutButtons();
        RaiseZOrder();
        if (Visible)
            ForceRepaint();
    }

    /// <summary>Put this HWND above mpv's native sibling (WinForms BringToFront is not enough).</summary>
    public void RaiseZOrder()
    {
        if (IsDisposed) return;
        try
        {
            if (!IsHandleCreated)
                CreateControl();
            if (!IsHandleCreated) return;

            BringToFront();
            SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNoactivate | (Visible ? SwpShowwindow : 0u));
        }
        catch
        {
            // ignore
        }
    }

    public void ShowChrome()
    {
        if (IsDisposed) return;
        Reposition();
        if (!Visible)
            Visible = true;
        RaiseZOrder();
        ForceRepaint();
        if (!_repaintTimer.Enabled)
            _repaintTimer.Start();
    }

    public void HideChrome()
    {
        if (IsDisposed) return;
        try { _repaintTimer.Stop(); } catch { /* ignore */ }
        if (Visible)
            Visible = false;
    }

    /// <summary>Force GDI repaint of panel + buttons (mpv may cover without invalidating us).</summary>
    public void ForceRepaint()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            // Ensure opaque debug colors stick (some themes reset)
            BackColor = DebugBarColor;
            _min.BackColor = DebugMinColor;
            _max.BackColor = DebugMaxColor;
            _close.BackColor = DebugCloseColor;

            Invalidate(true);
            Refresh();
            foreach (Control c in Controls)
            {
                c.Invalidate();
                c.Refresh();
            }

            InvalidateRect(Handle, IntPtr.Zero, true);
            UpdateWindow(Handle);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdatenow);
        }
        catch
        {
            // ignore
        }
    }

    public bool IsMouseOverChrome(Point clientOnParent)
    {
        if (Parent is null) return false;
        var r = new Rectangle(Left, Top, Width, Height);
        r.Inflate(6, 6);
        return r.Contains(clientOnParent);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        // Explicit fill so we never depend on default background erase alone
        using (var br = new SolidBrush(DebugBarColor))
            e.Graphics.FillRectangle(br, ClientRectangle);
        using (var pen = new Pen(Color.Yellow, 2))
            e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        base.OnPaint(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var br = new SolidBrush(DebugBarColor);
        e.Graphics.FillRectangle(br, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _zOrderTimer.Stop(); _zOrderTimer.Dispose(); } catch { /* ignore */ }
            try { _repaintTimer.Stop(); _repaintTimer.Dispose(); } catch { /* ignore */ }
        }
        base.Dispose(disposing);
    }
}
