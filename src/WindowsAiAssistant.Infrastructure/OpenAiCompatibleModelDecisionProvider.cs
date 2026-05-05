using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Infrastructure.Secrets;

namespace WindowsAiAssistant.Infrastructure;

public sealed class OpenAiCompatibleModelDecisionProvider : IModelDecisionProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ModelDecisionProfile _profile;
    private readonly ModelProviderCredentialResolver _credentialResolver;

    public OpenAiCompatibleModelDecisionProvider(
        HttpClient httpClient,
        ModelDecisionProfile profile,
        ModelProviderCredentialResolver credentialResolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
    }

    public async Task<ModelDecisionResult> TryDecideAsync(
        ModelFacingObservationPackage observationPackage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observationPackage);

        if (!SupportsProfileKind(_profile.Kind))
        {
            return CreateNotAvailableResult($"unsupported-provider-kind:{_profile.Kind}");
        }

        if (_profile.EndpointStyle != ModelEndpointStyle.OpenAiChatCompletions)
        {
            return CreateNotAvailableResult($"unsupported-endpoint-style:{_profile.EndpointStyle}");
        }

        if (string.IsNullOrWhiteSpace(_profile.BaseUrl))
        {
            return CreateNotAvailableResult("missing-base-url");
        }

        if (string.IsNullOrWhiteSpace(_profile.EndpointPath))
        {
            return CreateNotAvailableResult("missing-endpoint-path");
        }

        if (string.IsNullOrWhiteSpace(_profile.Model))
        {
            return CreateNotAvailableResult("missing-model");
        }

        if (!TryCreateEndpointUri(out var endpointUri))
        {
            return CreateNotAvailableResult("invalid-endpoint");
        }

        var resolution = _credentialResolver.Resolve(_profile);
        if (resolution.IsMissing)
        {
            return CreateNotAvailableResult("missing-api-key");
        }

        var authToken = resolution.Token;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = BuildRequestContent(observationPackage)
        };

        ApplyAuthentication(request, authToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CreateNotAvailableResult($"http-{(int)response.StatusCode}");
            }

            if (!TryExtractMessageContent(responseBody, out var messageContent))
            {
                return CreateInvalidResponseResult("missing-message-content");
            }

            if (!NextActionDecisionContractJsonParser.TryParseDecision(
                    messageContent,
                    out var nextActionDecision,
                    out var reason))
            {
                return CreateInvalidResponseResult(reason ?? "invalid-decision-payload");
            }

            return new ModelDecisionResult
            {
                Status = ModelDecisionStatus.DecisionAvailable,
                NextActionDecision = nextActionDecision,
                Reason = reason
            };
        }
        catch (JsonException)
        {
            return CreateInvalidResponseResult("invalid-json");
        }
        catch (HttpRequestException ex)
        {
            return CreateNotAvailableResult($"http-request:{ex.GetType().Name}");
        }
    }

    private HttpContent BuildRequestContent(ModelFacingObservationPackage observationPackage)
    {
        var payload = new
        {
            model = _profile.Model,
            response_format = new
            {
                type = "json_object"
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt()
                },
                new
                {
                    role = "user",
                    content = BuildUserPrompt(observationPackage)
                }
            }
        };

        return new StringContent(
            JsonSerializer.Serialize(payload, SerializerOptions),
            Encoding.UTF8,
            "application/json");
    }

    private void ApplyAuthentication(HttpRequestMessage request, string? authToken)
    {
        if (_profile.AuthScheme == ModelAuthScheme.None)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        if (_profile.ApiKeyHeaderName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            if (_profile.AuthScheme == ModelAuthScheme.Bearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                return;
            }

            request.Headers.TryAddWithoutValidation(_profile.ApiKeyHeaderName, authToken);
            return;
        }

        var headerValue = _profile.AuthScheme == ModelAuthScheme.Bearer
            ? $"Bearer {authToken}"
            : authToken;
        request.Headers.TryAddWithoutValidation(_profile.ApiKeyHeaderName, headerValue);
    }

    private bool TryCreateEndpointUri(out Uri? endpointUri)
    {
        endpointUri = null;

        if (!Uri.TryCreate(_profile.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var normalizedBaseUrl = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : $"{baseUri.AbsoluteUri}/";
        var relativePath = _profile.EndpointPath.TrimStart('/');

        endpointUri = new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), relativePath);
        return true;
    }

    private static bool SupportsProfileKind(ModelProviderKind kind)
    {
        return kind is ModelProviderKind.OpenAICompatible or ModelProviderKind.OpenAI;
    }

    private static string BuildSystemPrompt()
    {
        return
            """
            You are the decision engine for a Windows assistant.
            Return exactly one JSON object and no markdown.
            You must produce one next-action contract using these enums exactly:
            DecisionKind: ExecuteAction, AskObserve, AskApproval, Retry, Stop
            ActionType: Launch, Focus, InputText, Confirm, Cancel, Navigate, PressKey, PressShortcut, Verify, OpenFile
            ActionTargetKind: Application, Process, Window, File, Path, Element, Url, Service, Generic
            StopDisposition: Completed, Blocked, Aborted

            JSON shape:
            {
              "kind": "...",
              "message": "...",
              "rationale": "...",
              "confidence": 0.0,
              "requiresApprovalReason": "...",
              "retryReason": "...",
              "stopReason": "...",
              "executeAction": {
                "actionType": "...",
                "target": {
                  "kind": "...",
                  "reference": "...",
                  "displayName": "...",
                  "metadata": { "key": "value" }
                },
                "parameters": { "key": "value" },
                "capabilityHint": "...",
                "retryHint": "..."
              },
              "askObserve": {
                "observationRequest": "...",
                "observationHint": "..."
              },
              "askApproval": {
                "requiresApprovalReason": "...",
                "proposedAction": { same shape as executeAction }
              },
              "retry": {
                "retryReason": "...",
                "retryCountHint": 1,
                "action": { same shape as executeAction }
              },
              "stop": {
                "disposition": "Completed|Blocked|Aborted",
                "stopReason": "..."
              },
              "metadata": {
                "chain_continue": "true|false",
                "goal_pending": "true|false"
              }
            }

            Set metadata.chain_continue=true (or metadata.goal_pending=true) when the goal is not finished and another model decision step is required after this execution result.
            Set both flags to false when the goal is complete.
            If you do not have enough evidence to execute safely, return Stop with disposition Blocked.
            Never emit tool names. Emit only the next-action contract.
            """;
    }

    private static string BuildUserPrompt(ModelFacingObservationPackage observationPackage)
    {
        return
            $"""
            Observation package:
            {JsonSerializer.Serialize(observationPackage, SerializerOptions)}
            """;
    }

    private static bool TryExtractMessageContent(string responseBody, out string content)
    {
        content = string.Empty;

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var messageContent))
        {
            return false;
        }

        if (messageContent.ValueKind == JsonValueKind.String)
        {
            content = messageContent.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(content);
        }

        if (messageContent.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var item in messageContent.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var part = item.GetString();
                if (!string.IsNullOrWhiteSpace(part))
                {
                    parts.Add(part);
                }

                continue;
            }

            if (item.TryGetProperty("text", out var textElement))
            {
                var part = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(part))
                {
                    parts.Add(part);
                }
            }
        }

        content = string.Join(Environment.NewLine, parts);
        return !string.IsNullOrWhiteSpace(content);
    }

    private static ModelDecisionResult CreateNotAvailableResult(string reason)
    {
        return new ModelDecisionResult
        {
            Status = ModelDecisionStatus.NotAvailable,
            Reason = reason
        };
    }

    private static ModelDecisionResult CreateInvalidResponseResult(string reason)
    {
        return new ModelDecisionResult
        {
            Status = ModelDecisionStatus.InvalidResponse,
            Reason = reason
        };
    }
}
