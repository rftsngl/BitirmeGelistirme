namespace WindowsAiAssistant.Runtime.Observation;

public sealed record MonitorDescription(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    bool IsPrimary,
    float Scale);

public sealed class ScreenInfoService
{
    public (int Width, int Height, int CursorX, int CursorY) GetScreenAndCursor()
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyScreen);

        var cursorX = 0;
        var cursorY = 0;
        if (NativeMethods.GetCursorPos(out var point))
        {
            cursorX = point.X;
            cursorY = point.Y;
        }

        return (width, height, cursorX, cursorY);
    }

    public IReadOnlyList<MonitorDescription> GetMonitorInfos()
    {
        var monitors = new List<MonitorDescription>();
        var index = 0;

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (hMonitor, hdcMonitor, rcMonitor, dwData) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
                };
                if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    return true;
                }

                var isPrimary = (info.dwFlags & NativeMethods.MonitorinfofPrimary) != 0;
                NativeMethods.GetDpiForMonitor(hMonitor, 0, out var dpiX, out _);
                var scale = dpiX > 0 ? dpiX / 96.0f : 1.0f;

                monitors.Add(new MonitorDescription(
                    index++,
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top,
                    info.rcWork.Left,
                    info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top,
                    isPrimary,
                    scale));
                return true;
            },
            IntPtr.Zero);

        return monitors;
    }
}
