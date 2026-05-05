using System.Net.Http;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Infrastructure.Secrets;
using WindowsAiAssistant.Infrastructure.State;

namespace WindowsAiAssistant.Infrastructure;

public sealed class ModelDecisionProviderFactory
{
    private readonly HttpClient _httpClient;
    private readonly ModelDecisionSettings _settings;
    private readonly ModelDecisionProfileResolver _profileResolver;
    private readonly ModelProviderCredentialResolver _credentialResolver;
    private readonly IActiveProfileStore? _activeProfileStore;

    public ModelDecisionProviderFactory(
        HttpClient httpClient,
        ModelDecisionSettings settings,
        ModelDecisionProfileResolver profileResolver,
        ModelProviderCredentialResolver credentialResolver,
        IActiveProfileStore? activeProfileStore = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _activeProfileStore = activeProfileStore;
    }

    public IModelDecisionProvider Create()
    {
        var effectiveSettings = BuildEffectiveSettings();
        if (!_profileResolver.TryResolveActiveProfile(effectiveSettings, out var profile, out var reason))
        {
            return new UnavailableModelDecisionProvider(reason);
        }

        if (profile is null)
        {
            return new UnavailableModelDecisionProvider("missing-resolved-profile");
        }

        return CreateProvider(profile);
    }

    /// <summary>
    /// Builds a decision provider for an explicit profile (e.g. connection test). Does not consult active profile override.
    /// </summary>
    public IModelDecisionProvider CreateProvider(ModelDecisionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Kind == ModelProviderKind.OpenAICompatible ||
            profile.Kind == ModelProviderKind.OpenAI)
        {
            if (profile.EndpointStyle != ModelEndpointStyle.OpenAiChatCompletions)
            {
                return new UnavailableModelDecisionProvider(
                    $"unsupported-endpoint-style:{profile.EndpointStyle}");
            }

            return new OpenAiCompatibleModelDecisionProvider(_httpClient, profile, _credentialResolver);
        }

        if (profile.Kind == ModelProviderKind.Gemini)
        {
            return new GeminiModelDecisionProvider(_httpClient, profile, _credentialResolver);
        }

        return new UnavailableModelDecisionProvider(
            $"unsupported-provider-kind:{profile.Kind}");
    }

    private ModelDecisionSettings BuildEffectiveSettings()
    {
        var activeId = ResolveEffectiveActiveProfileId();
        return new ModelDecisionSettings
        {
            Enabled = _settings.Enabled,
            ActiveProfile = activeId,
            Profiles = _settings.Profiles,
            TimeoutMilliseconds = _settings.TimeoutMilliseconds,
            RuntimeSliceFallbackPolicy = _settings.RuntimeSliceFallbackPolicy
        };
    }

    private string ResolveEffectiveActiveProfileId()
    {
        var stored = _activeProfileStore?.GetActiveProfileId();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored.Trim();
        }

        return _settings.ActiveProfile ?? string.Empty;
    }
}
