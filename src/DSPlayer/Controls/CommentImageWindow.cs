using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DSPlayer.Services;

namespace DSPlayer.Controls;

/// <summary>
/// Borderless image popup: hold-drag to move, click / Esc / right-click to close,
/// edge-resize keeps the image aspect.
/// </summary>
public sealed class CommentImageWindow : Window
{
    private const int WM_SIZING = 0x0214;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;
    private const int DragThresholdPx = 5;

    private readonly CommentImageView _view = new();
    private LoadedCommentImage? _image;
    private HwndSource? _hwndSource;
    private double _aspect = 1;
    private bool _pressed;
    private bool _dragging;
    private System.Windows.Point _pressScreen;

    public CommentImageWindow()
    {
        Title = "画像";
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/Assets/dsplayer.ico"));
        }
        catch { /* ignore */ }

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Transparent;
        MinWidth = 32;
        MinHeight = 32;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        _view.Cursor = System.Windows.Input.Cursors.Arrow;
        _view.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        _view.VerticalAlignment = VerticalAlignment.Stretch;
        Content = _view;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Close();
        };
        PreviewMouseLeftButtonDown += OnPreviewLeftDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewLeftUp;
        PreviewMouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        SizeChanged += (_, _) => LayoutImage();
        SourceInitialized += (_, _) => AttachHook();
        Closed += (_, _) => DetachHook();
    }

    public void ShowImage(LoadedCommentImage image, Window? owner)
    {
        _image = image;
        if (owner is not null && !ReferenceEquals(Owner, owner))
            Owner = owner;

        var pxW = Math.Max(1, image.Preview.PixelWidth);
        var pxH = Math.Max(1, image.Preview.PixelHeight);
        _aspect = pxW / (double)pxH;

        var work = SystemParameters.WorkArea;
        // Show at native size by default. Only large images are reduced so that a
        // 1920x1080-class image does not cover most of the desktop.
        var maxW = Math.Max(200, Math.Min(1280, work.Width * 0.80));
        var maxH = Math.Max(200, Math.Min(800, work.Height * 0.80));
        var scale = Math.Min(1.0, Math.Min(maxW / pxW, maxH / pxH));
        Width = Math.Max(1, pxW * scale);
        Height = Math.Max(1, pxH * scale);

        if (!IsVisible)
            Show();
        else
            Activate();

        Dispatcher.BeginInvoke(LayoutImage, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void LayoutImage()
    {
        if (_image is null || !IsVisible)
            return;
        _view.ShowLoaded(
            _image,
            Math.Max(32, ActualWidth),
            Math.Max(32, ActualHeight));
    }

    private void OnPreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragging = false;
        _pressScreen = PointToScreen(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed || _dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        var now = PointToScreen(e.GetPosition(this));
        if (Math.Abs(now.X - _pressScreen.X) < DragThresholdPx &&
            Math.Abs(now.Y - _pressScreen.Y) < DragThresholdPx)
            return;

        _dragging = true;
        _pressed = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        try { DragMove(); }
        catch { /* ignored if capture races */ }
    }

    private void OnPreviewLeftUp(object sender, MouseButtonEventArgs e)
    {
        var wasClick = _pressed && !_dragging;
        _pressed = false;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (!wasClick)
            return;
        e.Handled = true;
        Close();
    }

    private void AttachHook()
    {
        if (_hwndSource is not null)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    private void DetachHook()
    {
        if (_hwndSource is null)
            return;
        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_SIZING || lParam == IntPtr.Zero || WindowState != WindowState.Normal)
            return IntPtr.Zero;
        if (_aspect is < 0.05 or > 20)
            return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var edge = wParam.ToInt32();
        var winW = (double)(rect.Right - rect.Left);
        var winH = (double)(rect.Bottom - rect.Top);
        if (winW < 8 || winH < 8)
            return IntPtr.Zero;

        var changingWidth = edge is WMSZ_LEFT or WMSZ_RIGHT or WMSZ_TOPLEFT or WMSZ_TOPRIGHT
            or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
        var changingHeight = edge is WMSZ_TOP or WMSZ_BOTTOM or WMSZ_TOPLEFT or WMSZ_TOPRIGHT
            or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;

        if (changingWidth && changingHeight)
        {
            var hFromW = winW / _aspect;
            var wFromH = winH * _aspect;
            if (Math.Abs(winH - hFromW) <= Math.Abs(winW - wFromH))
                ApplyHeight(ref rect, edge, winW / _aspect);
            else
                ApplyWidth(ref rect, edge, winH * _aspect);
        }
        else if (changingWidth)
        {
            ApplyHeight(ref rect, edge, winW / _aspect);
        }
        else if (changingHeight)
        {
            ApplyWidth(ref rect, edge, winH * _aspect);
        }

        Marshal.StructureToPtr(rect, lParam, false);
        return IntPtr.Zero;
    }

    private static void ApplyHeight(ref NativeRect rect, int edge, double winH)
    {
        var h = Math.Max(80, (int)Math.Round(winH));
        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
            rect.Top = rect.Bottom - h;
        else
            rect.Bottom = rect.Top + h;
    }

    private static void ApplyWidth(ref NativeRect rect, int edge, double winW)
    {
        var w = Math.Max(80, (int)Math.Round(winW));
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
            rect.Left = rect.Right - w;
        else
            rect.Right = rect.Left + w;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
}
