using System.Runtime.InteropServices;

namespace WindowsAiAssistant.App.HotKeys;

internal sealed class NativeMessageWindow : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly nint _hwnd;
    private readonly WndProcDelegate _wndProcDelegate;
    private nint _oldWndProc;
    private bool _disposed;

    public NativeMessageWindow()
    {
        _wndProcDelegate = WindowProc;
        _hwnd = CreateWindowEx(
            0,
            "STATIC",
            "WindowsAiAssistantHotKeyHost",
            0,
            0, 0, 0, 0,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);

        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException("Hotkey message window olusturulamadi.");
        }

        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndproc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    public event EventHandler<int>? HotKeyPressed;

    public nint Handle => _hwnd;

    public bool RegisterHotKey(int id, uint modifiers, uint virtualKey) =>
        NativeMethods.RegisterHotKey(_hwnd, id, modifiers, virtualKey);

    public bool UnregisterHotKey(int id) =>
        NativeMethods.UnregisterHotKey(_hwnd, id);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != nint.Zero)
        {
            if (_oldWndProc != nint.Zero)
            {
                SetWindowLongPtr(_hwnd, GwlWndproc, _oldWndProc);
            }

            DestroyWindow(_hwnd);
        }

        GC.SuppressFinalize(this);
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmHotkey)
        {
            HotKeyPressed?.Invoke(this, wParam.ToInt32());
            return nint.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GwlWndproc = -4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) =>
        nint.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, dwNewLong);
}

internal static class NativeMethods
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);
}
