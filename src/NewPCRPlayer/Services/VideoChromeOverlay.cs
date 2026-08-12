using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewPCRPlayer.Services;

/// <summary>
/// Min / Max / Close as WinForms children of the video panel (mpv wid host).
/// mpv creates a native child HWND that covers WinForms siblings — BringToFront alone
/// is not enough; we raise z-order with SetWindowPos(HWND_TOP) after mpv starts.
/// </summary>
public sealed class VideoChromeOverlay : Panel
{
    private readonly Button _min;
    private readonly Button _max;
    private readonly Button _close;
    private readonly System.Windows.Forms.Timer _zOrderTimer;

    public const int BarHeight = 28;
    public const int ButtonWidth = 40;
    /// <summary>Top-right hover zone (must beat resize grip ~14px).</summary>
    public const int HotWidth = 140;
    public const int HotHeight = 44;

    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public event EventHandler? MinimizeClick;
    public event EventHandler? MaximizeClick;
    public event EventHandler? CloseClick;

    public VideoChromeOverlay()
    {
        Height = BarHeight;
        Width = ButtonWidth * 3;
        BackColor = Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1A);
        Visible = false;
        TabStop = false;
        // Ensure we get a real HWND early
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _min = MakeButton("─", "最小化");
        _max = MakeButton("□", "最大化");
        _close = MakeButton("✕", "閉じる");
        _close.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0x11, 0x23);
        _close.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xC5, 0x0F, 0x1F);

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
                RaiseZOrder();
        };
    }

    public void SetMaximizedGlyph(bool restored)
    {
        _max.Text = restored ? "❐" : "□";
        _max.AccessibleName = restored ? "元のサイズに戻す" : "最大化";
    }

    public void AttachTo(Control videoPanel)
    {
        ArgumentNullException.ThrowIfNull(videoPanel);
        if (Parent is not null)
            Parent.Controls.Remove(this);

        videoPanel.Controls.Add(this);
        // Force HWND creation before mpv may cover the area
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

            // Among WinForms siblings first
            BringToFront();

            // Above native children (mpv VO window)
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
    }

    public void HideChrome()
    {
        if (IsDisposed) return;
        if (Visible)
            Visible = false;
    }

    public bool IsMouseOverChrome(Point clientOnParent)
    {
        if (Parent is null) return false;
        var r = new Rectangle(Left, Top, Width, Height);
        r.Inflate(6, 6);
        return r.Contains(clientOnParent);
    }

    /// <summary>Top-right zone on the video panel (client coords). Priority over resize grips.</summary>
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

    private static Button MakeButton(string text, string tip)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC),
            Font = new Font("Segoe UI Symbol", 9f, FontStyle.Regular),
            TabStop = false,
            Cursor = Cursors.Arrow,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);
        var tt = new ToolTip { ShowAlways = false, AutoPopDelay = 2000 };
        tt.SetToolTip(b, tip);
        return b;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var br = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(br, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _zOrderTimer.Stop(); } catch { /* ignore */ }
            try { _zOrderTimer.Dispose(); } catch { /* ignore */ }
        }
        base.Dispose(disposing);
    }
}
