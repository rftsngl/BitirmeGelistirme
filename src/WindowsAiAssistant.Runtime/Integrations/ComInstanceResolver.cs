using System.Runtime.InteropServices;

namespace WindowsAiAssistant.Runtime.Integrations;

internal static class ComInstanceResolver
{
    private const int HResultMkEUnavailable = unchecked((int)0x800401E3);

    public static object? TryGetRunningInstance(string progId)
    {
        var hr = CLSIDFromProgID(progId, out var clsid);
        if (hr != 0)
        {
            return null;
        }

        hr = NativeGetActiveObject(ref clsid, IntPtr.Zero, out var instance);
        return hr switch
        {
            0 => instance,
            HResultMkEUnavailable => null,
            _ => null
        };
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int NativeGetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.Interface)] out object? ppunk);
}
