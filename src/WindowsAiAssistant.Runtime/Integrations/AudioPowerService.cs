using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class AudioPowerService : IAudioPowerService
{
    public ActionResult Execute(string mode, int? level = null)
    {
        var normalized = (mode ?? "get_volume").Trim().ToLowerInvariant();
        return normalized switch
        {
            "get_volume" => GetVolume(),
            "set_volume" => SetVolume(level),
            "mute" => SetMute(true),
            "unmute" => SetMute(false),
            "prevent_sleep" => SetSleepState(prevent: true),
            "allow_sleep" => SetSleepState(prevent: false),
            _ => IntegrationResultHelper.Fail("Desteklenen modlar: get_volume, set_volume, mute, unmute, prevent_sleep, allow_sleep.")
        };
    }

    private static ActionResult GetVolume()
    {
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            return IntegrationResultHelper.Ok($"volume={volume.MasterVolumeLevelScalar:P0} muted={volume.Mute}");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Ses seviyesi okunamadi: {ex.Message}");
        }
    }

    private static ActionResult SetVolume(int? level)
    {
        if (level is null)
        {
            return IntegrationResultHelper.Fail("set_volume icin parameters.level (0-100) gerekli.");
        }

        try
        {
            var scalar = Math.Clamp(level.Value, 0, 100) / 100f;
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
            return IntegrationResultHelper.Ok($"Ses seviyesi %{level.Value} olarak ayarlandi.");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Ses seviyesi ayarlanamadi: {ex.Message}");
        }
    }

    private static ActionResult SetMute(bool mute)
    {
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = mute;
            return IntegrationResultHelper.Ok(mute ? "Ses susturuldu." : "Ses acildi.");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Ses susturma ayarlanamadi: {ex.Message}");
        }
    }

    private static ActionResult SetSleepState(bool prevent)
    {
        try
        {
            var flags = prevent
                ? EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED
                : EXECUTION_STATE.ES_CONTINUOUS;
            SetThreadExecutionState(flags);
            return IntegrationResultHelper.Ok(prevent ? "Uyku/ekran kapatma gecici olarak engellendi." : "Uyku engeli kaldirildi.");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Guc ayari uygulanamadi: {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_CONTINUOUS = 0x80000000,
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002
    }
}
