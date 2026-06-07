using Windows.Security.Credentials.UI;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinWindowsHelloService : IWindowsHelloService
{
    public async Task<ActionResult> VerifyAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync().AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (availability != UserConsentVerifierAvailability.Available)
            {
                return new ActionResult
                {
                    Success = false,
                    Message = $"Windows Hello kullanilamiyor: {availability}"
                };
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(message)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return new ActionResult
            {
                Success = result == UserConsentVerificationResult.Verified,
                Message = result switch
                {
                    UserConsentVerificationResult.Verified => "Windows Hello dogrulamasi basarili.",
                    UserConsentVerificationResult.DeviceNotPresent => "Windows Hello cihazi yok.",
                    UserConsentVerificationResult.NotConfiguredForUser => "Windows Hello kullanici icin yapilandirilmamis.",
                    UserConsentVerificationResult.DisabledByPolicy => "Windows Hello politika ile devre disi.",
                    UserConsentVerificationResult.Canceled => "Windows Hello dogrulamasi iptal edildi.",
                    _ => $"Windows Hello sonucu: {result}"
                }
            };
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"Windows Hello hatasi: {ex.Message}"
            };
        }
    }
}
