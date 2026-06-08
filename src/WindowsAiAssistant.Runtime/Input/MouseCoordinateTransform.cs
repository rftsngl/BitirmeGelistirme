namespace WindowsAiAssistant.Runtime.Input;

/// <summary>
/// Pure coordinate math for virtual-desktop absolute mouse positioning (SendInput MOUSEEVENTF_ABSOLUTE).
/// </summary>
public static class MouseCoordinateTransform
{
    public const int NormalizedMax = 65535;

    public static (int AbsoluteX, int AbsoluteY) ToNormalizedVirtualDesktop(
        int physicalX,
        int physicalY,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
    {
        var width = Math.Max(1, virtualWidth);
        var height = Math.Max(1, virtualHeight);

        var relativeX = physicalX - virtualLeft;
        var relativeY = physicalY - virtualTop;

        var absoluteX = (int)Math.Round(relativeX * (double)NormalizedMax / Math.Max(1, width - 1));
        var absoluteY = (int)Math.Round(relativeY * (double)NormalizedMax / Math.Max(1, height - 1));

        return (
            Math.Clamp(absoluteX, 0, NormalizedMax),
            Math.Clamp(absoluteY, 0, NormalizedMax));
    }
}
