namespace WindowsAiAssistant.Runtime.Observation;

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
}
