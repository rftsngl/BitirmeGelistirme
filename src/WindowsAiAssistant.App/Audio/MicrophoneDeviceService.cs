using NAudio.Wave;

namespace WindowsAiAssistant.App.Audio;

public sealed record MicrophoneDevice(int Index, string Name)
{
    public string Display => Index < 0 ? Name : $"[{Index}] {Name}";
}

/// <summary>
/// Kullanilabilir mikrofon (WaveIn) cihazlarini listeler. Index -1 = varsayilan cihaz.
/// </summary>
public sealed class MicrophoneDeviceService
{
    public IReadOnlyList<MicrophoneDevice> ListDevices()
    {
        var devices = new List<MicrophoneDevice>
        {
            new(-1, "Varsayilan cihaz")
        };

        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                devices.Add(new MicrophoneDevice(i, capabilities.ProductName));
            }
        }
        catch
        {
            // Cihaz numaralandirilamadi; en azindan varsayilan secenek doner.
        }

        return devices;
    }
}
