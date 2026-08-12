using System.Drawing;
using System.Windows.Forms;

namespace NewPCRPlayer.Services;

/// <summary>
/// Min / Max / Close chrome drawn as WinForms children of the video panel (mpv wid host).
/// Stays inside the video HWND tree — not a top-level window — so it tracks the parent
/// and can sit above the embedded mpv surface without WPF airspace issues.
/// </summary>
public sealed class VideoChromeOverlay : Panel
{
    private readonly Button _min;
    private readonly Button _max;
    private readonly Button _close;

    public const int BarHeight = 28;
    public const int ButtonWidth = 40;
    public const int HotWidth = 128;
    public const int HotHeight = 40;

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
    }

    public void SetMaximizedGlyph(bool restored)
    {
        _max.Text = restored ? "❐" : "□";
        // ToolTip via separate? keep simple — update AccessibleName
        _max.AccessibleName = restored ? "元のサイズに戻す" : "最大化";
    }

    public void AttachTo(Control videoPanel)
    {
        ArgumentNullException.ThrowIfNull(videoPanel);
        if (Parent is not null)
            Parent.Controls.Remove(this);

        videoPanel.Controls.Add(this);
        videoPanel.Resize += (_, _) => Reposition();
        Reposition();
        BringToFront();
        HideChrome();
    }

    public void Reposition()
    {
        if (Parent is null) return;
        Left = Math.Max(0, Parent.ClientSize.Width - Width);
        Top = 0;
        BringToFront();
    }

    public void ShowChrome()
    {
        if (!Visible)
            Visible = true;
        Reposition();
        BringToFront();
    }

    public void HideChrome()
    {
        if (Visible)
            Visible = false;
    }

    public bool IsMouseOverChrome(Point clientOnParent)
    {
        if (Parent is null) return false;
        var r = Bounds;
        // expand hit a bit so leaving via button doesn't flicker
        r.Inflate(4, 4);
        return r.Contains(clientOnParent);
    }

    public static bool IsInHotZone(Point clientOnParent, Size parentClientSize)
    {
        if (parentClientSize.Width <= 0 || parentClientSize.Height <= 0)
            return false;
        return clientOnParent.X >= parentClientSize.Width - HotWidth
               && clientOnParent.Y >= 0
               && clientOnParent.Y <= HotHeight
               && clientOnParent.X < parentClientSize.Width;
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
            Cursor = Cursors.Hand,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);
        // Simple tooltip
        var tt = new ToolTip { ShowAlways = false, AutoPopDelay = 2000 };
        tt.SetToolTip(b, tip);
        return b;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // solid semi-opaque bar
        using var br = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(br, ClientRectangle);
    }
}
