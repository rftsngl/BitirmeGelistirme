using System.Runtime.InteropServices;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private const uint WmTrayIcon = 0x8000 + 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;
    private const uint WmLbuttonDblClk = 0x0203;
    private const uint WmRbuttonUp = 0x0205;
    private const uint WmCommand = 0x0111;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint LoadFromFile = 0x0010;
    private const uint ImageIcon = 1;

    private const int CmdOpenMain = 1001;
    private const int CmdActivateOverlay = 1002;
    private const int CmdToggleListening = 1003;
    private const int CmdExit = 1004;

    private readonly TrayMessageWindow _messageWindow;
    private NOTIFYICONDATAW _iconData;
    private nint _iconHandle;
    private bool _listeningEnabled = true;
    private bool _initialized;

    public TrayIconService()
    {
        _messageWindow = new TrayMessageWindow(WmTrayIcon, OnWindowMessage);
        _iconData = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow.Handle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = WmTrayIcon,
            szTip = "Windows AI Assistant"
        };
    }

    public event EventHandler? OpenMainWindowRequested;
    public event EventHandler? ActivateOverlayRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ListeningToggled;

    public bool IsListeningEnabled => _listeningEnabled;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _iconHandle = LoadCustomIcon() ?? LoadIcon(nint.Zero, new nint(32512));
        _iconData.hIcon = _iconHandle;
        Shell_NotifyIcon(NimAdd, ref _iconData);
        _initialized = true;
    }

    public void SetListeningEnabled(bool enabled)
    {
        _listeningEnabled = enabled;
        if (!_initialized)
        {
            return;
        }

        _iconData.szTip = enabled
            ? "Windows AI Assistant — dinleniyor"
            : "Windows AI Assistant — dinleme kapalı";
        Shell_NotifyIcon(NimModify, ref _iconData);
    }

    public void Dispose()
    {
        if (_initialized)
        {
            Shell_NotifyIcon(NimDelete, ref _iconData);
            _initialized = false;
        }

        if (_iconHandle != nint.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = nint.Zero;
        }

        _messageWindow.Dispose();
    }

    private static nint? LoadCustomIcon()
    {
        if (!AppIconPaths.TryGetIcoPath(out var iconPath))
        {
            return null;
        }

        var handle = LoadImage(nint.Zero, iconPath, ImageIcon, 16, 16, LoadFromFile);
        return handle == nint.Zero ? null : handle;
    }

    private void OnWindowMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != WmTrayIcon)
        {
            return;
        }

        var lowParam = (uint)lParam;
        if (lowParam == WmLbuttonDblClk)
        {
            ActivateOverlayRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (lowParam == WmRbuttonUp)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, CmdOpenMain, "Asistanı Aç");
        AppendMenu(menu, MfString, CmdActivateOverlay, "Sesli Komut (Ctrl+Alt+A)");
        AppendMenu(
            menu,
            MfString,
            CmdToggleListening,
            _listeningEnabled ? "Dinlemeyi Durdur" : "Dinlemeyi Başlat");
        AppendMenu(menu, MfString, CmdExit, "Çıkış");

        GetCursorPos(out var point);
        SetForegroundWindow(_messageWindow.Handle);
        var command = TrackPopupMenuEx(
            menu,
            TpmRightButton | TpmReturnCmd,
            point.X,
            point.Y,
            _messageWindow.Handle,
            nint.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case CmdOpenMain:
                OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdActivateOverlay:
                ActivateOverlayRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdToggleListening:
                SetListeningEnabled(!_listeningEnabled);
                ListeningToggled?.Invoke(this, EventArgs.Empty);
                break;
            case CmdExit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;
        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(
        nint hInst,
        string name,
        uint type,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, int uIdNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint hmenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

internal sealed class TrayMessageWindow : IDisposable
{
    private const int GwlWndproc = -4;
    private readonly uint _callbackMessage;
    private readonly Action<uint, nint, nint> _handler;
    private readonly WndProcDelegate _wndProcDelegate;
    private readonly nint _hwnd;
    private nint _oldWndProc;
    private bool _disposed;

    public TrayMessageWindow(uint callbackMessage, Action<uint, nint, nint> handler)
    {
        _callbackMessage = callbackMessage;
        _handler = handler;
        _wndProcDelegate = WindowProc;
        _hwnd = CreateWindowEx(
            0,
            "STATIC",
            "WindowsAiAssistantTrayHost",
            0,
            0, 0, 0, 0,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);

        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException("Tray mesaj penceresi oluşturulamadı.");
        }

        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndproc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    public nint Handle => _hwnd;

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
        if (msg == _callbackMessage)
        {
            _handler(msg, wParam, lParam);
            return nint.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

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
