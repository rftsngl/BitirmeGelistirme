using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsAiAssistant.Runtime.Windows;

public sealed class WindowManager
{
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;

    public IReadOnlyList<WindowInfo> ListVisibleWindows()
    {
        var foreground = NativeWindowMethods.GetForegroundWindow();
        var windows = new List<(nint Handle, string Title, string ProcessName, bool Minimized, bool Maximized)>();

        NativeWindowMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeWindowMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            var length = NativeWindowMethods.GetWindowTextLength(hwnd);
            if (length == 0)
            {
                return true;
            }

            var title = ReadWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeWindowMethods.GetWindowThreadProcessId(hwnd, out var processId);
            var processName = ResolveProcessName(processId);
            NativeWindowMethods.GetWindowPlacement(hwnd, out var placement);
            var minimized = placement.showCmd == SwMinimize;
            var maximized = placement.showCmd == SwMaximize;

            windows.Add((hwnd, title, processName, minimized, maximized));
            return true;
        }, nint.Zero);

        return windows
            .OrderByDescending(window => window.Handle == foreground)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .Select((window, index) => new WindowInfo
            {
                WindowId = $"w{index + 1}",
                Handle = window.Handle,
                Title = window.Title,
                ProcessName = window.ProcessName,
                IsForeground = window.Handle == foreground,
                IsMinimized = window.Minimized,
                IsMaximized = window.Maximized
            })
            .ToList();
    }

    public bool TryResolveWindow(string? target, IReadOnlyList<WindowInfo> windows, out WindowInfo? window, out string? error)
    {
        window = null;
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Pencere hedefi belirtilmedi.";
            return false;
        }

        target = target.Trim();
        window = windows.FirstOrDefault(item =>
            item.WindowId.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (window is not null)
        {
            return true;
        }

        window = windows.FirstOrDefault(item =>
            item.Title.Contains(target, StringComparison.OrdinalIgnoreCase));
        if (window is not null)
        {
            return true;
        }

        error = $"Pencere bulunamadi: '{target}'.";
        return false;
    }

    public bool FocusWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        NativeWindowMethods.ShowWindow(handle, SwRestore);

        // SetForegroundWindow is restricted unless the calling thread is attached to the
        // foreground thread's input queue. Attach, bring to front, then detach.
        var foreground = NativeWindowMethods.GetForegroundWindow();
        var targetThread = NativeWindowMethods.GetWindowThreadProcessId(handle, out _);
        var foregroundThread = NativeWindowMethods.GetWindowThreadProcessId(foreground, out _);

        var attached = false;
        if (foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread)
        {
            attached = NativeWindowMethods.AttachThreadInput(foregroundThread, targetThread, true);
        }

        try
        {
            NativeWindowMethods.SetForegroundWindow(handle);
            NativeWindowMethods.BringWindowToTop(handle);
        }
        finally
        {
            if (attached)
            {
                NativeWindowMethods.AttachThreadInput(foregroundThread, targetThread, false);
            }
        }

        return NativeWindowMethods.GetForegroundWindow() == handle;
    }

    public bool MoveWindow(nint handle, int x, int y, int width, int height)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        const uint swpNoZOrder = 0x0004;
        const uint swpNoActivate = 0x0010;
        return NativeWindowMethods.SetWindowPos(
            handle, nint.Zero, x, y, width, height, swpNoZOrder | swpNoActivate);
    }

    public bool SetWindowState(nint handle, string state)
    {
        if (handle == nint.Zero)
        {
            return false;
        }

        return state.Trim().ToLowerInvariant() switch
        {
            "minimize" => NativeWindowMethods.ShowWindow(handle, SwMinimize),
            "maximize" => NativeWindowMethods.ShowWindow(handle, SwMaximize),
            "restore" => NativeWindowMethods.ShowWindow(handle, SwRestore),
            "close" => NativeWindowMethods.PostMessage(handle, NativeWindowMethods.WmClose, nint.Zero, nint.Zero),
            _ => false
        };
    }

    private static string ReadWindowTitle(nint handle)
    {
        var length = NativeWindowMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        _ = NativeWindowMethods.GetWindowText(handle, buffer, buffer.Length);
        return new string(buffer).Trim('\0', ' ');
    }

    private static string ResolveProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}

internal static class NativeWindowMethods
{
    public const uint WmClose = 0x0010;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rect rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint hWnd, out WindowPlacement lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
