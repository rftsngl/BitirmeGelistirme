using System.Runtime.InteropServices;
using WindowsAiAssistant.Runtime.Debugging;
using WindowsAiAssistant.Runtime.Session;

namespace WindowsAiAssistant.Runtime.Input;

public static class MouseInput
{
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfMiddledown = 0x0020;
    private const uint MouseeventfMiddleup = 0x0040;
    private const uint MouseeventfAbsolute = 0x8000;
    private const uint MouseeventfWheel = 0x0800;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public static void Click(int x, int y, MouseButton button = MouseButton.Left) =>
        ClickAt(x, y, button);

    public static void RightClick(int x, int y) => ClickAt(x, y, MouseButton.Right);

    public static void MiddleClick(int x, int y) => ClickAt(x, y, MouseButton.Middle);

    public static void MoveTo(int x, int y) => MoveToInternal(x, y);

    private static void ClickAt(int x, int y, MouseButton button)
    {
        MoveToInternal(x, y);
        var (down, up) = ButtonFlags(button);
        SendMouse(down);
        SendMouse(up);
    }

    private static (uint Down, uint Up) ButtonFlags(MouseButton button) =>
        button switch
        {
            MouseButton.Right => (MouseeventfRightdown, MouseeventfRightup),
            MouseButton.Middle => (MouseeventfMiddledown, MouseeventfMiddleup),
            _ => (MouseeventfLeftdown, MouseeventfLeftup)
        };

    public static void Drag(int startX, int startY, int endX, int endY)
    {
        MoveTo(startX, startY);
        SendMouse(MouseeventfLeftdown);
        Thread.Sleep(40);
        MoveTo(endX, endY);
        Thread.Sleep(40);
        SendMouse(MouseeventfLeftup);
    }

    public static void Scroll(int delta)
    {
        var input = new Input
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MouseInputData
                {
                    mouseData = (uint)delta,
                    dwFlags = MouseeventfWheel
                }
            }
        };

        SendInputChecked(1, [input], "MouseInput.Scroll");
    }

    private static void MoveToInternal(int x, int y)
    {
        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = GetSystemMetrics(SmCxVirtualScreen);
        var virtualHeight = GetSystemMetrics(SmCyVirtualScreen);

        var (absoluteX, absoluteY) = MouseCoordinateTransform.ToNormalizedVirtualDesktop(
            x,
            y,
            virtualLeft,
            virtualTop,
            virtualWidth,
            virtualHeight);

        var input = new Input
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MouseInputData
                {
                    dx = absoluteX,
                    dy = absoluteY,
                    dwFlags = MouseeventfMove | MouseeventfAbsolute
                }
            }
        };

        SendInputChecked(1, [input], "MouseInput.MoveTo", new { x, y, absoluteX, absoluteY, virtualLeft, virtualTop, virtualWidth, virtualHeight });
    }

    private static void SendMouse(uint flags)
    {
        var input = new Input
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MouseInputData { dwFlags = flags }
            }
        };

        SendInputChecked(1, [input], "MouseInput.SendMouse", new { flags });
    }

    private static void SendInputChecked(uint expectedCount, Input[] inputs, string location, object? data = null)
    {
        var sent = SendInput(expectedCount, inputs, Marshal.SizeOf<Input>());
        if (sent == expectedCount)
        {
            return;
        }

        DebugAgentLog.Write(
            "F006",
            location,
            "SendInput returned fewer events than requested",
            new
            {
                expected = expectedCount,
                sent,
                lastError = Marshal.GetLastWin32Error(),
                data
            },
            AgentRunScope.Current?.RunId);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }
}
