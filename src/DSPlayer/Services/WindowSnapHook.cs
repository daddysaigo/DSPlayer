using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Point = System.Drawing.Point;

namespace DSPlayer.Services;

/// <summary>Magnetic edges during the existing native move loop; resizing is left to WindowSizingHook.</summary>
internal sealed class WindowSnapHook : IDisposable
{
    private const int WM_MOVING = 0x0216;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int VK_SHIFT = 0x10;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const uint GA_ROOTOWNER = 3;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    private const int SnapDistanceDip = 12;

    private readonly Window _window;
    private readonly Func<bool> _isEnabled;
    private HwndSource? _source;
    private Rectangle[] _workAreas = [];
    private Rectangle[] _otherWindows = [];
    private Rectangle _dragStart;
    private Point _dragCursor;
    private bool _dragging;

    public WindowSnapHook(Window window, Func<bool> isEnabled)
    {
        _window = window;
        _isEnabled = isEnabled;
    }

    public void Attach()
    {
        if (_source is not null) return;
        var hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("Window handle is not ready.");
        _source.AddHook(WndProc);
    }

    public void Dispose()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
        EndDrag();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE)
        {
            EndDrag();
            return IntPtr.Zero;
        }

        if (!_isEnabled() || _window.WindowState != WindowState.Normal)
            return IntPtr.Zero;

        if (msg == WM_ENTERSIZEMOVE)
        {
            EndDrag();
            if (GetWindowRect(hwnd, out var start) && GetCursorPos(out var cursor))
            {
                _dragStart = start.ToRectangle();
                _dragCursor = new Point(cursor.X, cursor.Y);
                _workAreas = System.Windows.Forms.Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
                _otherWindows = CollectWindows(hwnd);
                _dragging = true;
            }
        }
        else if (msg == WM_MOVING && _dragging && lParam != IntPtr.Zero && GetCursorPos(out var cursor))
        {
            var native = Marshal.PtrToStructure<NativeRect>(lParam);
            var free = WindowSnapGeometry.FollowCursor(native.ToRectangle(), _dragStart, _dragCursor,
                new Point(cursor.X, cursor.Y));
            var result = free;
            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
            {
                // Snap the painted edges, excluding Windows' invisible resize borders.
                var visible = free;
                if (GetWindowRect(hwnd, out var current) && TryGetVisibleBounds(hwnd, out var frame))
                    visible = Rectangle.FromLTRB(free.Left + frame.Left - current.Left,
                        free.Top + frame.Top - current.Top, free.Right + frame.Right - current.Right,
                        free.Bottom + frame.Bottom - current.Bottom);

                var distance = Math.Max(1, (int)Math.Round(SnapDistanceDip * GetDpiForWindow(hwnd) / 96.0));
                var snapped = WindowSnapGeometry.Snap(visible, _workAreas, _otherWindows, distance);
                result.Offset(snapped.Left - visible.Left, snapped.Top - visible.Top);
            }

            Marshal.StructureToPtr(NativeRect.FromRectangle(result), lParam, false);
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    private void EndDrag()
    {
        _dragging = false;
        _workAreas = [];
        _otherWindows = [];
    }

    private static Rectangle[] CollectWindows(IntPtr ourWindow)
    {
        var result = new List<Rectangle>();
        var shell = GetShellWindow();
        var desktop = GetDesktopWindow();
        // Snapshot once per drag, rather than enumerate every time the pointer moves.
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == ourWindow || hwnd == shell || hwnd == desktop ||
                GetAncestor(hwnd, GA_ROOTOWNER) == ourWindow || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;

            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((style & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0)
                return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            if (TryGetVisibleBounds(hwnd, out var bounds))
                result.Add(bounds);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    private static bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetWindowRect(hwnd, out var logicalWindow)) return false;
        bounds = logicalWindow.ToRectangle();
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        // DWM reports physical pixels even if the caller uses DPI-virtualized coordinates.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect frame,
                Marshal.SizeOf<NativeRect>()) == 0 && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            // Read the outer rect in physical pixels too, then map just the frame insets.
            // PhysicalToLogicalPointForPerMonitorDPI cannot map another window's points
            // through our HWND: it fails for points outside that HWND's bounds.
            var previousContext = SetThreadDpiAwarenessContext(new IntPtr(-3)); // PER_MONITOR_AWARE
            if (previousContext != IntPtr.Zero)
            {
                try
                {
                    if (GetWindowRect(hwnd, out var physicalWindow))
                    {
                        var visible = WindowSnapGeometry.MapVisibleFrame(bounds,
                            physicalWindow.ToRectangle(), frame.ToRectangle());
                        if (visible.Width > 0 && visible.Height > 0) bounds = visible;
                    }
                }
                finally
                {
                    SetThreadDpiAwarenessContext(previousContext);
                }
            }
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        public static NativeRect FromRectangle(Rectangle rect) =>
            new() { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
