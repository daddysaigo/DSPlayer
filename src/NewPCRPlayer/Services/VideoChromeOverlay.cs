using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewPCRPlayer.Services;

/// <summary>
/// Min/Max/Close as an <b>owned</b> tool window over the video (not a free-floating app).
/// Child-of-wid GDI controls are clickable but invisible under mpv D3D; ownership fixes paint.
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

    // WinForms Form/Button BackColor must be opaque (no alpha) — throws otherwise.
    private static readonly Color BarColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color BtnHover = Color.FromArgb(0x3A, 0x3A, 0x3A);
    private static readonly Color BtnPress = Color.FromArgb(0x4A, 0x4A, 0x4A);
    private static readonly Color CloseHover = Color.FromArgb(0xE8, 0x11, 0x23);
    private static readonly Color ClosePress = Color.FromArgb(0xC5, 0x0F, 0x1F);
    private static readonly Color TextColor = Color.FromArgb(0xE0, 0xE0, 0xE0);

    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;

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
        TopMost = false;
        BackColor = BarColor;
        ForeColor = TextColor;
        Size = new Size(ButtonWidth * 3, BarHeight);
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.Selectable, false);

        _min = MakeButton("─", "最小化", isClose: false);
        _max = MakeButton("□", "最大化", isClose: false);
        _close = MakeButton("✕", "閉じる", isClose: true);

        _min.Click += (_, _) => MinimizeClick?.Invoke(this, EventArgs.Empty);
        _max.Click += (_, _) => MaximizeClick?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => CloseClick?.Invoke(this, EventArgs.Empty);

        Controls.Add(_close);
        Controls.Add(_max);
        Controls.Add(_min);
        LayoutButtons();

        _syncTimer = new System.Windows.Forms.Timer { Interval = 32 };
        _syncTimer.Tick += (_, _) =>
        {
            if (IsDisposed || _videoPanel is null) return;
            SyncToVideoPanel();
            if (Visible)
                RaiseAboveOwner();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolwindow | WsExNoactivate;
            return cp;
        }
    }

    public void SetMaximizedGlyph(bool restored)
    {
        if (IsDisposed) return;
        _max.Text = restored ? "❐" : "□";
        _max.AccessibleName = restored ? "元のサイズに戻す" : "最大化";
    }

    public void Attach(IWin32Window owner, Control videoPanel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(videoPanel);
        _videoPanel = videoPanel;

        videoPanel.Resize -= OnVideoResized;
        videoPanel.SizeChanged -= OnVideoResized;
        videoPanel.Resize += OnVideoResized;
        videoPanel.SizeChanged += OnVideoResized;

        if (!Visible)
        {
            try { Show(owner); }
            catch { Show(); }
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

            var pt = _videoPanel.PointToScreen(new Point(Math.Max(0, w - Width), 0));
            if (Location != pt)
                Location = pt;
        }
        catch
        {
            // ignore
        }
    }

    public void RaiseAboveOwner()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        try
        {
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
            Visible = true;
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

    private static Button MakeButton(string text, string tip, bool isClose)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = BarColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Symbol", 9f, FontStyle.Regular),
            TabStop = false,
            Cursor = Cursors.Arrow,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        if (isClose)
        {
            b.FlatAppearance.MouseOverBackColor = CloseHover;
            b.FlatAppearance.MouseDownBackColor = ClosePress;
            b.MouseEnter += (_, _) => b.ForeColor = Color.White;
            b.MouseLeave += (_, _) => b.ForeColor = TextColor;
        }
        else
        {
            b.FlatAppearance.MouseOverBackColor = BtnHover;
            b.FlatAppearance.MouseDownBackColor = BtnPress;
        }

        var tt = new ToolTip { ShowAlways = false, AutoPopDelay = 2000 };
        tt.SetToolTip(b, tip);
        return b;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var br = new SolidBrush(BarColor);
        e.Graphics.FillRectangle(br, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using (var br = new SolidBrush(BarColor))
            e.Graphics.FillRectangle(br, ClientRectangle);
        // subtle bottom edge
        using (var pen = new Pen(Color.FromArgb(0xFF, 0x33, 0x33, 0x33)))
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
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
