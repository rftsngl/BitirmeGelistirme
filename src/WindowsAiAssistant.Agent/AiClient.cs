using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Agent;

public sealed class AiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AgentOptions _agentOptions;
    private readonly ProviderOptions _providerOptions;
    private readonly HttpClient _httpClient;

    public AiClient(AgentOptions agentOptions, ProviderOptions providerOptions)
    {
        _agentOptions = agentOptions ?? throw new ArgumentNullException(nameof(agentOptions));
        _providerOptions = providerOptions ?? throw new ArgumentNullException(nameof(providerOptions));

        // Timeout is enforced per request (see SendAsync). HttpClient.Timeout must not
        // change after the first request — that throws on subsequent agent loop steps.
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public Task<string> GetDecisionAsync(string prompt, CancellationToken cancellationToken = default) =>
        GetDecisionAsync(prompt, _providerOptions, _agentOptions.SystemPrompt, cancellationToken);

    public Task<string> ProbeAsync(ProviderOptions options, CancellationToken cancellationToken = default) =>
        GetDecisionAsync("Reply with exactly: OK", options, "You are a connectivity probe. Reply briefly.", cancellationToken);

    public async Task<string> GetDecisionAsync(
        string prompt,
        ProviderOptions options,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateConfiguration(options);

        if (string.Equals(options.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return await GetGeminiDecisionAsync(prompt, options, systemPrompt, cancellationToken).ConfigureAwait(false);
        }

        return await GetOpenAiCompatibleDecisionAsync(prompt, options, systemPrompt, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<string> GetOpenAiCompatibleDecisionAsync(
        string prompt,
        ProviderOptions options,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildOpenAiRequestUri(options);
        var requestBody = BuildOpenAiRequestBody(prompt, options, systemPrompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        ApplyAuthentication(request, options);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var rawBody = await SendAsync(request, options.RequestTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        return ExtractOpenAiAssistantContent(rawBody);
    }

    private async Task<string> GetGeminiDecisionAsync(
        string prompt,
        ProviderOptions options,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildGeminiRequestUri(options);
        var requestBody = BuildGeminiRequestBody(prompt, options, systemPrompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        ApplyAuthentication(request, options);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var rawBody = await SendAsync(request, options.RequestTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        return ExtractGeminiAssistantContent(rawBody);
    }

    private async Task<string> SendAsync(
        HttpRequestMessage request,
        int requestTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(requestTimeoutSeconds, 5, 600));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var response = await _httpClient
                .SendAsync(request, linkedCts.Token)
                .ConfigureAwait(false);
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var apiMessage = TryReadApiErrorMessage(rawBody);
                throw new AiClientException(
                    string.IsNullOrWhiteSpace(apiMessage)
                        ? $"LLM istegi basarisiz (HTTP {(int)response.StatusCode})."
                        : $"LLM istegi basarisiz: {apiMessage}");
            }

            return rawBody;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiClientException("LLM istegi zaman asimina ugradi.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException("LLM servisine ulasilamadi. BaseUrl ve ag baglantisini kontrol edin.", ex);
        }
    }

    private static void ValidateConfiguration(ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new AiClientException("Model BaseUrl yapilandirilmamis.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new AiClientException("Model adi yapilandirilmamis.");
        }
    }

    private static string? ResolveApiKey(ProviderOptions options)
    {
        if (!options.RequiresApiKey)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options.RuntimeApiKey))
        {
            return options.RuntimeApiKey.Trim();
        }

        var envName = options.ApiKeyEnvironmentVariable;
        if (!string.IsNullOrWhiteSpace(envName))
        {
            var key = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key.Trim();
            }
        }

        throw new AiClientException(
            string.IsNullOrWhiteSpace(envName)
                ? "API anahtari bulunamadi."
                : $"API anahtari bulunamadi. Ortam degiskeni: {envName}");
    }

    private static void ApplyAuthentication(HttpRequestMessage request, ProviderOptions options)
    {
        var apiKey = ResolveApiKey(options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (string.Equals(options.AuthScheme, "Raw", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(
                string.IsNullOrWhiteSpace(options.ApiKeyHeaderName) ? "x-goog-api-key" : options.ApiKeyHeaderName,
                apiKey);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static Uri BuildOpenAiRequestUri(ProviderOptions options)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var endpoint = options.Endpoint.Trim();
        if (!endpoint.StartsWith('/'))
        {
            endpoint = "/" + endpoint;
        }

        return new Uri($"{baseUrl}{endpoint}");
    }

    private static Uri BuildGeminiRequestUri(ProviderOptions options)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var endpoint = options.Endpoint.Trim().Replace("{model}", options.Model, StringComparison.OrdinalIgnoreCase);
        if (!endpoint.StartsWith('/'))
        {
            endpoint = "/" + endpoint;
        }

        return new Uri($"{baseUrl}{endpoint}");
    }

    private static string BuildOpenAiRequestBody(string userPrompt, ProviderOptions options, string systemPrompt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        if (options.Temperature is not null)
        {
            payload["temperature"] = options.Temperature.Value;
        }

        if (options.MaxTokens is not null)
        {
            payload["max_tokens"] = options.MaxTokens.Value;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildGeminiRequestBody(string userPrompt, ProviderOptions options, string systemPrompt)
    {
        var generationConfig = new Dictionary<string, object?>();
        if (options.Temperature is not null)
        {
            generationConfig["temperature"] = options.Temperature.Value;
        }

        if (options.MaxTokens is not null)
        {
            generationConfig["maxOutputTokens"] = options.MaxTokens.Value;
        }

        var payload = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            }
        };

        if (generationConfig.Count > 0)
        {
            payload["generationConfig"] = generationConfig;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string ExtractOpenAiAssistantContent(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : null;
                throw new AiClientException(
                    string.IsNullOrWhiteSpace(message) ? "LLM hata dondurdu." : message);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                throw new AiClientException("LLM cevabinda choices bulunamadi.");
            }

            var content = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiClientException("LLM bos icerik dondurdu.");
            }

            return content.Trim();
        }
        catch (AiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiClientException("LLM cevabi okunamadi.", ex);
        }
    }

    private static string ExtractGeminiAssistantContent(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : null;
                throw new AiClientException(
                    string.IsNullOrWhiteSpace(message) ? "Gemini hata dondurdu." : message);
            }

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                throw new AiClientException("Gemini cevabinda candidates bulunamadi.");
            }

            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
            {
                throw new AiClientException("Gemini bos icerik dondurdu.");
            }

            var content = parts[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiClientException("Gemini bos icerik dondurdu.");
            }

            return content.Trim();
        }
        catch (AiClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiClientException("Gemini cevabi okunamadi.", ex);
        }
    }

    private static string? TryReadApiErrorMessage(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
