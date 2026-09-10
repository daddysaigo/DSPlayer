using System.Drawing;

namespace DSPlayer.Services;

/// <summary>Move-only snapping in native screen coordinates. Never changes the window size.</summary>
internal static class WindowSnapGeometry
{
    public static Rectangle Snap(Rectangle moving, IReadOnlyList<Rectangle> workAreas,
        IReadOnlyList<Rectangle> windows, int distance)
    {
        if (distance <= 0 || moving.Width <= 0 || moving.Height <= 0)
            return moving;

        int? dx = null, dy = null;
        foreach (var area in workAreas)
        {
            if (Near(moving.Top, moving.Bottom, area.Top, area.Bottom, distance))
            {
                Consider(area.Left - moving.Left, distance, ref dx);
                Consider(area.Right - moving.Right, distance, ref dx);
            }
            if (Near(moving.Left, moving.Right, area.Left, area.Right, distance))
            {
                Consider(area.Top - moving.Top, distance, ref dy);
                Consider(area.Bottom - moving.Bottom, distance, ref dy);
            }
        }

        foreach (var other in windows)
        {
            if (other.Width <= 0 || other.Height <= 0) continue;
            if (Near(moving.Top, moving.Bottom, other.Top, other.Bottom, distance))
            {
                Consider(other.Left - moving.Right, distance, ref dx);
                Consider(other.Right - moving.Left, distance, ref dx);
                Consider(other.Left - moving.Left, distance, ref dx);
                Consider(other.Right - moving.Right, distance, ref dx);
            }
            if (Near(moving.Left, moving.Right, other.Left, other.Right, distance))
            {
                Consider(other.Top - moving.Bottom, distance, ref dy);
                Consider(other.Bottom - moving.Top, distance, ref dy);
                Consider(other.Top - moving.Top, distance, ref dy);
                Consider(other.Bottom - moving.Bottom, distance, ref dy);
            }
        }

        moving.Offset(dx ?? 0, dy ?? 0);
        return moving;
    }

    // Always derive the free position from the cursor, not the previous snapped position.
    // Otherwise repeated small mouse movements can keep the window stuck to an edge.
    public static Rectangle FollowCursor(Rectangle proposed, Rectangle start, Point startCursor, Point cursor)
    {
        if (start.Width <= 0 || start.Height <= 0) return proposed;
        // Preserve the relative grab point if Windows changes the size on a DPI transition.
        var offsetX = (int)Math.Round((startCursor.X - start.Left) * (double)proposed.Width / start.Width);
        var offsetY = (int)Math.Round((startCursor.Y - start.Top) * (double)proposed.Height / start.Height);
        return new Rectangle(cursor.X - offsetX, cursor.Y - offsetY, proposed.Width, proposed.Height);
    }

    public static Rectangle MapVisibleFrame(Rectangle logicalWindow, Rectangle physicalWindow, Rectangle physicalFrame)
    {
        if (physicalWindow.Width <= 0 || physicalWindow.Height <= 0) return logicalWindow;
        var scaleX = (double)logicalWindow.Width / physicalWindow.Width;
        var scaleY = (double)logicalWindow.Height / physicalWindow.Height;
        return Rectangle.FromLTRB(
            logicalWindow.Left + (int)Math.Round((physicalFrame.Left - physicalWindow.Left) * scaleX),
            logicalWindow.Top + (int)Math.Round((physicalFrame.Top - physicalWindow.Top) * scaleY),
            logicalWindow.Right + (int)Math.Round((physicalFrame.Right - physicalWindow.Right) * scaleX),
            logicalWindow.Bottom + (int)Math.Round((physicalFrame.Bottom - physicalWindow.Bottom) * scaleY));
    }

    private static bool Near(int start, int end, int otherStart, int otherEnd, int distance) =>
        start <= otherEnd + distance && end >= otherStart - distance;

    private static void Consider(int delta, int distance, ref int? best)
    {
        if (Math.Abs(delta) <= distance && (best is null || Math.Abs(delta) < Math.Abs(best.Value)))
            best = delta;
    }
}
