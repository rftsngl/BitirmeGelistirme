using System.Net.Http;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Infrastructure;

namespace WindowsAiAssistant.App.ProviderSettings;

/// <summary>
/// Narrow-scope connectivity probe for a profile. Does not log API keys or raw HTTP bodies.
/// </summary>
public sealed class ProviderConnectionTester
{
    private readonly ModelDecisionProviderFactory _factory;

    public ProviderConnectionTester(ModelDecisionProviderFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<ProviderConnectionTestResult> TestAsync(
        ModelDecisionProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        var minimalPackage = new ModelFacingObservationPackage
        {
            CorrelationId = "provider-connection-test",
            CommandText = "connection-test"
        };

        try
        {
            var provider = _factory.CreateProvider(profile);
            var result = await provider.TryDecideAsync(minimalPackage, linked.Token).ConfigureAwait(false);

            if (result.Status == ModelDecisionStatus.NotAvailable &&
                string.Equals(result.Reason, "missing-api-key", StringComparison.Ordinal))
            {
                return ProviderConnectionTestResult.MissingKey();
            }

            if (result.Status == ModelDecisionStatus.NotAvailable &&
                string.Equals(result.Reason, "timeout", StringComparison.Ordinal))
            {
                return ProviderConnectionTestResult.Timeout();
            }

            if (result.Status == ModelDecisionStatus.NotAvailable &&
                result.Reason is { } httpReason &&
                httpReason.StartsWith("http-", StringComparison.Ordinal))
            {
                var code = httpReason.Length > 5 ? httpReason[5..] : string.Empty;
                return ProviderConnectionTestResult.HttpError(code);
            }

            if (result.Status == ModelDecisionStatus.InvalidResponse)
            {
                return ProviderConnectionTestResult.InvalidResponse(result.Reason ?? "invalid");
            }

            if (result.Status == ModelDecisionStatus.DecisionAvailable)
            {
                return ProviderConnectionTestResult.Success();
            }

            return ProviderConnectionTestResult.Failure(result.Reason ?? "not-available");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderConnectionTestResult.Timeout();
        }
        catch (HttpRequestException)
        {
            return ProviderConnectionTestResult.NetworkFailure();
        }
        catch (TaskCanceledException)
        {
            return ProviderConnectionTestResult.Timeout();
        }
        catch (Exception ex)
        {
            return ProviderConnectionTestResult.Failure(ex.GetType().Name);
        }
    }
}

public sealed class ProviderConnectionTestResult
{
    public ProviderConnectionTestStatus Status { get; private init; }
    public string UserMessage { get; private init; } = string.Empty;

    public static ProviderConnectionTestResult Success() =>
        new()
        {
            Status = ProviderConnectionTestStatus.Success,
            UserMessage = "Connection OK (provider responded)."
        };

    public static ProviderConnectionTestResult MissingKey() =>
        new()
        {
            Status = ProviderConnectionTestStatus.MissingKey,
            UserMessage = "Missing API key. Save a key or set the configured environment variable."
        };

    public static ProviderConnectionTestResult Timeout() =>
        new()
        {
            Status = ProviderConnectionTestStatus.Timeout,
            UserMessage = "Request timed out."
        };

    public static ProviderConnectionTestResult HttpError(string code) =>
        new()
        {
            Status = ProviderConnectionTestStatus.HttpError,
            UserMessage = string.IsNullOrWhiteSpace(code)
                ? "HTTP error from provider endpoint."
                : $"HTTP error (status {code})."
        };

    public static ProviderConnectionTestResult InvalidResponse(string reason) =>
        new()
        {
            Status = ProviderConnectionTestStatus.InvalidResponse,
            UserMessage = "Connected but response was not a valid model decision."
        };

    public static ProviderConnectionTestResult NetworkFailure() =>
        new()
        {
            Status = ProviderConnectionTestStatus.NetworkFailure,
            UserMessage = "Network error while contacting the endpoint."
        };

    public static ProviderConnectionTestResult Failure(string category) =>
        new()
        {
            Status = ProviderConnectionTestStatus.Failure,
            UserMessage = string.IsNullOrWhiteSpace(category)
                ? "Connection test failed."
                : $"Connection test failed ({category})."
        };
}

public enum ProviderConnectionTestStatus
{
    Success,
    MissingKey,
    Timeout,
    HttpError,
    InvalidResponse,
    NetworkFailure,
    Failure
}
