using System.Runtime.InteropServices;

namespace WindowsAiAssistant.Runtime.Input;

public static class MouseInput
{
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfAbsolute = 0x8000;
    private const uint MouseeventfWheel = 0x0800;

    public static void Click(int x, int y)
    {
        MoveTo(x, y);
        SendMouse(MouseeventfLeftdown);
        SendMouse(MouseeventfLeftup);
    }

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

        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void MoveTo(int x, int y)
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        var absoluteX = (int)((x * 65535.0) / Math.Max(1, screenWidth - 1));
        var absoluteY = (int)((y * 65535.0) / Math.Max(1, screenHeight - 1));

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

        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
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

        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
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
