using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Tests;

public sealed class MouseCoordinateTransformTests
{
    [Fact]
    public void ToNormalizedVirtualDesktop_PrimaryOrigin_MapsCorners()
    {
        var (leftX, topY) = MouseCoordinateTransform.ToNormalizedVirtualDesktop(0, 0, 0, 0, 1920, 1080);
        var (rightX, bottomY) = MouseCoordinateTransform.ToNormalizedVirtualDesktop(1919, 1079, 0, 0, 1920, 1080);

        Assert.Equal(0, leftX);
        Assert.Equal(0, topY);
        Assert.Equal(MouseCoordinateTransform.NormalizedMax, rightX);
        Assert.Equal(MouseCoordinateTransform.NormalizedMax, bottomY);
    }

    [Fact]
    public void ToNormalizedVirtualDesktop_SecondaryMonitorOffset_UsesVirtualDesktopBounds()
    {
        var (x, y) = MouseCoordinateTransform.ToNormalizedVirtualDesktop(1920, 540, 0, 0, 3840, 1080);

        Assert.InRange(x, 32_700, 32_800);
        Assert.InRange(y, 32_700, 32_800);
    }

    [Fact]
    public void ToNormalizedVirtualDesktop_ClampNegativeRelativeCoordinates()
    {
        var (x, y) = MouseCoordinateTransform.ToNormalizedVirtualDesktop(-10, -10, 0, 0, 1920, 1080);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
