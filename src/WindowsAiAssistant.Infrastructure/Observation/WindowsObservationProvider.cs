using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Observation;

public interface IForegroundWindowNativeApi
{
    nint GetForegroundWindow();
    string? GetWindowTitle(nint windowHandle);
    uint? GetWindowProcessId(nint windowHandle);
    string? GetProcessName(uint? processId);
}

public sealed class WindowsObservationProvider : IObservationProvider
{
    private readonly IForegroundWindowNativeApi _nativeApi;
    private readonly Func<bool> _isWindowsPlatform;

    public WindowsObservationProvider()
        : this(new ForegroundWindowNativeApi(), null)
    {
    }

    public WindowsObservationProvider(
        IForegroundWindowNativeApi nativeApi,
        Func<bool>? isWindowsPlatform = null)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _isWindowsPlatform = isWindowsPlatform ?? OperatingSystem.IsWindows;
    }

    public Task<ObservationSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;

        if (!_isWindowsPlatform())
        {
            return Task.FromResult(CreateEmptySnapshot(capturedAtUtc, "Foreground observation is unavailable on this platform."));
        }

        try
        {
            var windowHandle = _nativeApi.GetForegroundWindow();
            if (windowHandle == nint.Zero)
            {
                return Task.FromResult(CreateEmptySnapshot(capturedAtUtc, "No foreground window detected."));
            }

            var title = _nativeApi.GetWindowTitle(windowHandle);
            var processId = _nativeApi.GetWindowProcessId(windowHandle);
            var processName = _nativeApi.GetProcessName(processId);

            var snapshot = new ObservationSnapshot
            {
                ActiveWindow = new WindowContext
                {
                    Title = string.IsNullOrWhiteSpace(title) ? null : title,
                    ProcessName = processName,
                    Handle = windowHandle.ToInt64(),
                    IsForeground = true
                },
                ActiveProcessName = processName,
                ClipboardTextPreview = null,
                HasSelection = false,
                SelectionTextPreview = null,
                DesktopStateSummary = "Foreground window captured.",
                CapturedAtUtc = capturedAtUtc
            };

            return Task.FromResult(snapshot);
        }
        catch
        {
            return Task.FromResult(CreateEmptySnapshot(capturedAtUtc, "Foreground observation failed."));
        }
    }

    private static ObservationSnapshot CreateEmptySnapshot(DateTimeOffset capturedAtUtc, string summary)
    {
        return new ObservationSnapshot
        {
            ActiveWindow = new WindowContext
            {
                Title = null,
                ProcessName = null,
                Handle = null,
                IsForeground = false
            },
            ActiveProcessName = null,
            ClipboardTextPreview = null,
            HasSelection = false,
            SelectionTextPreview = null,
            DesktopStateSummary = summary,
            CapturedAtUtc = capturedAtUtc
        };
    }

    private sealed class ForegroundWindowNativeApi : IForegroundWindowNativeApi
    {
        public nint GetForegroundWindow()
        {
            return NativeMethods.GetForegroundWindow();
        }

        public string? GetWindowTitle(nint windowHandle)
        {
            var length = NativeMethods.GetWindowTextLengthW(windowHandle);
            if (length <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length + 1);
            var copied = NativeMethods.GetWindowTextW(windowHandle, builder, builder.Capacity);
            if (copied <= 0)
            {
                return null;
            }

            return builder.ToString();
        }

        public uint? GetWindowProcessId(nint windowHandle)
        {
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            return processId == 0 ? null : processId;
        }

        public string? GetProcessName(uint? processId)
        {
            if (!processId.HasValue || processId.Value == 0 || processId.Value > int.MaxValue)
            {
                return null;
            }

            try
            {
                return Process.GetProcessById((int)processId.Value).ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(nint hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    }
}
