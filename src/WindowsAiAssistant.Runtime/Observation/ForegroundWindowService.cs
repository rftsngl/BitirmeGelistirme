using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ForegroundWindowService
{
    public (string WindowTitle, string ProcessName, int ProcessId) GetForegroundInfo()
    {
        var details = GetForegroundDetails();
        return (details.WindowTitle, details.ProcessName, details.ProcessId);
    }

    public ForegroundWindowDetails GetForegroundDetails()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return new ForegroundWindowDetails(string.Empty, string.Empty, 0, string.Empty);
        }

        var title = ReadWindowTitle(handle);
        var className = ReadWindowClassName(handle);
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        var processName = ResolveProcessName(processId);

        return new ForegroundWindowDetails(title, processName, (int)processId, className);
    }

    private static string ReadWindowClassName(IntPtr handle)
    {
        var buffer = new char[256];
        var copied = NativeMethods.GetClassName(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static string ReadWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}
