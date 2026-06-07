using Windows.Security.Authorization.AppCapabilityAccess;

namespace WindowsAiAssistant.App.Audio;

public enum MicrophoneAccessState
{
    Granted,
    DeniedByUser,
    DeniedBySystem,
    Unavailable
}

public sealed class MicrophonePermissionService
{
    public MicrophoneAccessState CheckAccess()
    {
        try
        {
            var capability = AppCapability.Create("microphone");
            return Map(capability.CheckAccess());
        }
        catch
        {
            return MicrophoneAccessState.Unavailable;
        }
    }

    public async Task<MicrophoneAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var current = CheckAccess();
        if (current == MicrophoneAccessState.Granted)
        {
            return current;
        }

        try
        {
            var capability = AppCapability.Create("microphone");
            var result = await capability.RequestAccessAsync().AsTask().ConfigureAwait(false);
            return Map(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return MicrophoneAccessState.Unavailable;
        }
    }

    public static string Describe(MicrophoneAccessState state) => state switch
    {
        MicrophoneAccessState.Granted => "Mikrofon izni verildi.",
        MicrophoneAccessState.DeniedByUser =>
            "Mikrofon izni reddedildi. Windows Ayarlar → Gizlilik → Mikrofon bölümünden Windows AI Assistant için erişimi açın.",
        MicrophoneAccessState.DeniedBySystem =>
            "Sistem mikrofon erişimini engelliyor. Windows Ayarlar → Gizlilik → Mikrofon ayarlarını kontrol edin.",
        _ => "Mikrofon izni durumu alınamadı. Windows sürümünüz ve gizlilik ayarlarını kontrol edin."
    };

    private static MicrophoneAccessState Map(AppCapabilityAccessStatus status) => status switch
    {
        AppCapabilityAccessStatus.Allowed => MicrophoneAccessState.Granted,
        AppCapabilityAccessStatus.DeniedByUser => MicrophoneAccessState.DeniedByUser,
        AppCapabilityAccessStatus.DeniedBySystem => MicrophoneAccessState.DeniedBySystem,
        _ => MicrophoneAccessState.Unavailable
    };
}

public sealed class SpeechAccessException : Exception
{
    public SpeechAccessException(string message) : base(message)
    {
    }

    public SpeechAccessException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
