using System.Drawing;
using DSPlayer.Models;
using DSPlayer.Services;
using Xunit;

namespace DSPlayer.Tests;

public class WindowSnapTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(8, 8, 0, 0)]
    [InlineData(1512, 8, 1520, 0)]
    [InlineData(8, 732, 0, 740)]
    [InlineData(1512, 732, 1520, 740)]
    [InlineData(-8, -8, 0, 0)]
    [InlineData(1528, 748, 1520, 740)]
    public void ScreenCorners_SnapFromEitherSide_WithoutResizing(int x, int y, int expectedX, int expectedY)
    {
        var result = WindowSnapGeometry.Snap(new Rectangle(x, y, 400, 300), [WorkArea], [], 12);

        Assert.Equal(new Rectangle(expectedX, expectedY, 400, 300), result);
    }

    [Fact]
    public void BeyondThreshold_MovesFreely()
    {
        var moving = new Rectangle(13, 13, 400, 300);
        Assert.Equal(moving, WindowSnapGeometry.Snap(moving, [WorkArea], [], 12));
    }

    [Theory]
    [InlineData(12, 12, 0)]
    [InlineData(13, 12, 13)]
    [InlineData(18, 18, 0)]
    [InlineData(19, 18, 19)]
    [InlineData(0, 0, 0)]
    [InlineData(8, 0, 8)]
    public void Threshold_UsesProvidedDpiScaledDistance(int left, int distance, int expectedLeft)
    {
        var result = WindowSnapGeometry.Snap(new Rectangle(left, 200, 400, 300), [WorkArea], [], distance);
        Assert.Equal(expectedLeft, result.Left);
    }

    [Fact]
    public void LeftAndUpperMonitor_SupportsNegativeCoordinatesAndTaskbarInset()
    {
        Rectangle second = new(-1920, -200, 1920, 1040);
        var result = WindowSnapGeometry.Snap(new Rectangle(-1913, 533, 400, 300), [WorkArea, second], [], 12);

        Assert.Equal(new Rectangle(-1920, 540, 400, 300), result);
    }

    [Fact]
    public void MonitorSeam_CanBeCrossedAfterLeavingThreshold()
    {
        Rectangle second = new(1920, 0, 1920, 1040);
        var moving = new Rectangle(1533, 200, 400, 300);

        Assert.Equal(moving, WindowSnapGeometry.Snap(moving, [WorkArea, second], [], 12));
    }

    [Theory]
    [InlineData(193, 350, 200, 350)] // left of target
    [InlineData(1107, 350, 1100, 350)] // right of target
    [InlineData(650, -7, 650, 0)] // above target
    [InlineData(650, 707, 650, 700)] // below target
    [InlineData(1107, 293, 1100, 300)] // adjacent + top alignment
    [InlineData(1107, 407, 1100, 400)] // adjacent + bottom alignment
    [InlineData(593, 707, 600, 700)] // below + left alignment
    [InlineData(707, 707, 700, 700)] // below + right alignment
    public void OtherWindow_SnapsAdjacentAndAlignedEdges(int x, int y, int expectedX, int expectedY)
    {
        var result = WindowSnapGeometry.Snap(new Rectangle(x, y, 400, 300), [],
            [new Rectangle(600, 300, 500, 400)], 12);

        Assert.Equal(new Rectangle(expectedX, expectedY, 400, 300), result);
    }

    [Fact]
    public void DistantWindow_DoesNotAttractAlongExtendedEdge()
    {
        var moving = new Rectangle(1107, 900, 400, 300);
        var result = WindowSnapGeometry.Snap(moving, [], [new Rectangle(600, 300, 500, 400)], 12);

        Assert.Equal(moving, result);
    }

    [Fact]
    public void DistantMonitor_DoesNotAttractAlongExtendedEdge()
    {
        var moving = new Rectangle(8, 2000, 400, 300);
        Assert.Equal(moving, WindowSnapGeometry.Snap(moving, [WorkArea], [], 12));
    }

    [Fact]
    public void NearestEdge_WinsRegardlessOfEnumerationOrder()
    {
        Rectangle moving = new(194, 350, 400, 300);
        Rectangle nearer = new(597, 300, 500, 400);
        Rectangle farther = new(603, 300, 500, 400);
        var expected = new Rectangle(197, 350, 400, 300);

        Assert.Equal(expected, WindowSnapGeometry.Snap(moving, [], [farther, nearer], 12));
        Assert.Equal(expected, WindowSnapGeometry.Snap(moving, [], [nearer, farther], 12));
    }

    [Fact]
    public void EqualDistance_PrefersWorkAreaOverOtherWindow()
    {
        var result = WindowSnapGeometry.Snap(new Rectangle(6, 350, 400, 300), [WorkArea],
            [new Rectangle(412, 300, 500, 400)], 12);

        Assert.Equal(0, result.Left);
    }

    [Fact]
    public void SlowDrag_CanEscapeSnap_WithoutChangingGrabPoint()
    {
        Rectangle start = new(0, 200, 400, 300);
        Point startCursor = new(150, 300);
        var previous = start;
        for (var step = 1; step <= 20; step++)
        {
            var free = WindowSnapGeometry.FollowCursor(previous, start, startCursor,
                new Point(startCursor.X + step, startCursor.Y));
            previous = WindowSnapGeometry.Snap(free, [WorkArea], [], 12);

            Assert.Equal(step <= 12 ? 0 : step, previous.Left);
            Assert.Equal(start.Size, previous.Size);
        }
    }

    [Fact]
    public void TemporarilyBypassingSnap_ReturnsToFreeCursorPosition()
    {
        Rectangle start = new(0, 200, 400, 300);
        Point startCursor = new(150, 300);
        var free = WindowSnapGeometry.FollowCursor(start, start, startCursor, new Point(158, 300));
        Assert.Equal(8, free.Left);
        Assert.Equal(0, WindowSnapGeometry.Snap(free, [WorkArea], [], 12).Left);
    }

    [Fact]
    public void DpiSizeChange_PreservesRelativeGrabPointAndNewSize()
    {
        var result = WindowSnapGeometry.FollowCursor(new Rectangle(0, 0, 600, 450),
            new Rectangle(100, 200, 400, 300), new Point(300, 300), new Point(1800, 500));

        Assert.Equal(new Rectangle(1500, 350, 600, 450), result);
    }

    [Fact]
    public void VisibleFrame_ExcludesInvisibleBorders_InPhysicalCoordinates()
    {
        Rectangle outer = new(100, 200, 800, 600);
        Rectangle painted = Rectangle.FromLTRB(108, 200, 892, 792);

        Assert.Equal(painted, WindowSnapGeometry.MapVisibleFrame(outer, outer, painted));
    }

    [Fact]
    public void VisibleFrame_MapsDpiVirtualizedBorders_OnNegativeMonitor()
    {
        var result = WindowSnapGeometry.MapVisibleFrame(
            new Rectangle(-1600, 100, 800, 600),
            new Rectangle(-2400, 150, 1200, 900),
            Rectangle.FromLTRB(-2388, 150, -1212, 1038));

        Assert.Equal(Rectangle.FromLTRB(-1592, 100, -808, 692), result);
    }

    [Fact]
    public void BorderlessWindow_SnapsFlushToOtherWindowsPaintedEdge()
    {
        var frame = WindowSnapGeometry.MapVisibleFrame(new Rectangle(592, 300, 516, 408),
            new Rectangle(592, 300, 516, 408), new Rectangle(600, 300, 500, 400));
        var result = WindowSnapGeometry.Snap(new Rectangle(193, 350, 400, 300), [], [frame], 12);

        Assert.Equal(frame.Left, result.Right);
    }

    [Fact]
    public void Settings_DefaultEnabled_AndCancelRestoresBothValues()
    {
        var settings = new AppSettings();
        Assert.True(settings.WindowSnapEnabled);
        var enabledSnapshot = AppSettings.SerializeSnapshot(settings);
        settings.WindowSnapEnabled = false;
        var disabledSnapshot = AppSettings.SerializeSnapshot(settings);

        AppSettings.RestoreSnapshot(settings, enabledSnapshot);
        Assert.True(settings.WindowSnapEnabled);
        AppSettings.RestoreSnapshot(settings, disabledSnapshot);
        Assert.False(settings.WindowSnapEnabled);
    }

    [Fact]
    public void ExistingSettingsWithoutSnapOption_DefaultToEnabled()
    {
        var settings = new AppSettings { WindowSnapEnabled = false };
        AppSettings.RestoreSnapshot(settings, "{\"uiTheme\":\"Grok\"}");
        Assert.True(settings.WindowSnapEnabled);
    }
}
