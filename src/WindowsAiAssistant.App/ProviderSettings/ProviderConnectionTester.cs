using System.Diagnostics;
using WindowsAiAssistant.Agent;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderConnectionTester
{
    private readonly AiClient _aiClient;
    private readonly IProviderConfigurationService _configurationService;

    public ProviderConnectionTester(AiClient aiClient, IProviderConfigurationService configurationService)
    {
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public async Task<ConnectionTestSummary> TestProfileAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var started = Stopwatch.StartNew();

        try
        {
            if (!_configurationService.IsProfileReady(profile) && profile.RequiresApiKey)
            {
                return new ConnectionTestSummary
                {
                    Status = ProviderConnectionTestStatus.Failed,
                    Duration = started.Elapsed,
                    Message = "API anahtarı bulunamadı. Ortam değişkeni veya yerel kayıtlı anahtar gerekli."
                };
            }

            var options = _configurationService.ToRuntimeOptions(profile);
            var response = await _aiClient
                .ProbeAsync(options, cancellationToken)
                .ConfigureAwait(false);

            return new ConnectionTestSummary
            {
                Status = ProviderConnectionTestStatus.Success,
                Duration = started.Elapsed,
                Message = string.IsNullOrWhiteSpace(response)
                    ? "Sağlayıcı yanıt verdi."
                    : $"Sağlayıcı yanıt verdi: {Truncate(response, 120)}"
            };
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestSummary
            {
                Status = ProviderConnectionTestStatus.Cancelled,
                Duration = started.Elapsed,
                Message = "Bağlantı testi iptal edildi."
            };
        }
        catch (AiClientException ex)
        {
            return new ConnectionTestSummary
            {
                Status = ProviderConnectionTestStatus.Failed,
                Duration = started.Elapsed,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestSummary
            {
                Status = ProviderConnectionTestStatus.Failed,
                Duration = started.Elapsed,
                Message = ex.Message
            };
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
