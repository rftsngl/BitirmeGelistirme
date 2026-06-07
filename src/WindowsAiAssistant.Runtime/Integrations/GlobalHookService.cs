using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class GlobalHookService : IGlobalHookService, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int WmLbuttondown = 0x0201;
    private const int WmRbuttondown = 0x0204;
    private const int WmMbuttondown = 0x0207;

    private readonly object _sync = new();
    private readonly ConcurrentQueue<string> _events = new();
    private HookNativeMethods.LowLevelProc? _keyboardProc;
    private HookNativeMethods.LowLevelProc? _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _disposed;

    public ActionResult Execute(string mode, string? hookType = null, int maxEvents = 32)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "start" => Start(hookType),
            "stop" => Stop(maxEvents),
            "peek" or "status" => Peek(maxEvents),
            _ => new ActionResult
            {
                Success = false,
                Message = "global_hook mode: start|stop|peek. parameters.type=keyboard|mouse|both."
            }
        };
    }

    private ActionResult Start(string? hookType)
    {
        lock (_sync)
        {
            var type = (hookType ?? "keyboard").Trim().ToLowerInvariant();
            var wantKeyboard = type is "keyboard" or "both";
            var wantMouse = type is "mouse" or "both";

            if (wantKeyboard && _keyboardHook == IntPtr.Zero)
            {
                _keyboardProc = KeyboardHookCallback;
                _keyboardHook = HookNativeMethods.SetWindowsHookEx(
                    WhKeyboardLl,
                    _keyboardProc,
                    HookNativeMethods.GetModuleHandle(null),
                    0);
            }

            if (wantMouse && _mouseHook == IntPtr.Zero)
            {
                _mouseProc = MouseHookCallback;
                _mouseHook = HookNativeMethods.SetWindowsHookEx(
                    WhMouseLl,
                    _mouseProc,
                    HookNativeMethods.GetModuleHandle(null),
                    0);
            }

            if (_keyboardHook == IntPtr.Zero && _mouseHook == IntPtr.Zero)
            {
                return new ActionResult { Success = false, Message = "Global hook baslatilamadi." };
            }

            return new ActionResult
            {
                Success = true,
                Message = $"Global hook baslatildi (type={type}). stop veya peek ile olaylari okuyun."
            };
        }
    }

    private ActionResult Stop(int maxEvents)
    {
        lock (_sync)
        {
            UninstallHooks();
            return new ActionResult
            {
                Success = true,
                Message = BuildEventSummary(maxEvents, stopped: true)
            };
        }
    }

    private ActionResult Peek(int maxEvents)
    {
        lock (_sync)
        {
            return new ActionResult
            {
                Success = true,
                Message = BuildEventSummary(maxEvents, stopped: false)
            };
        }
    }

    private string BuildEventSummary(int maxEvents, bool stopped)
    {
        var limit = Math.Clamp(maxEvents, 1, 200);
        var lines = new List<string>
        {
            stopped ? "hook=stopped" : "hook=running",
            $"keyboard={(_keyboardHook != IntPtr.Zero ? "on" : "off")}",
            $"mouse={(_mouseHook != IntPtr.Zero ? "on" : "off")}"
        };

        var count = 0;
        while (count < limit && _events.TryDequeue(out var line))
        {
            count++;
            lines.Add(line);
        }

        if (_events.IsEmpty == false)
        {
            lines.Add($"... daha fazla olay kuyrukta");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
        {
            var vk = Marshal.ReadInt32(lParam);
            _events.Enqueue($"keyboard vk={vk} ({DateTimeOffset.Now:HH:mm:ss})");
        }

        return HookNativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var code = wParam.ToInt32();
            if (code is WmLbuttondown or WmRbuttondown or WmMbuttondown)
            {
                var x = Marshal.ReadInt32(lParam);
                var y = Marshal.ReadInt32(lParam, 4);
                _events.Enqueue($"mouse event={code} x={x} y={y} ({DateTimeOffset.Now:HH:mm:ss})");
            }
        }

        return HookNativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            HookNativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _keyboardProc = null;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            HookNativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseProc = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            UninstallHooks();
        }

        _disposed = true;
    }

    private static class HookNativeMethods
    {
        internal delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
