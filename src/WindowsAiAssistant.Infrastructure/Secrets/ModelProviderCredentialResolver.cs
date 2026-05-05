namespace WindowsAiAssistant.Infrastructure.Secrets;

/// <summary>
/// Where the API token for a profile was resolved from.
/// </summary>
public enum CredentialSource
{
    None = 0,
    Stored = 1,
    EnvVar = 2
}

/// <summary>
/// Result of resolving credentials for a <see cref="ModelDecisionProfile"/>.
/// </summary>
/// <param name="Token">Resolved API token, if any.</param>
/// <param name="Source">Where the token came from.</param>
/// <param name="IsMissing">True when the profile requires an API key but none was found in the secure store or configured env var.</param>
public readonly record struct CredentialResolution(string? Token, CredentialSource Source, bool IsMissing);

/// <summary>
/// Resolves API keys in order: UI-saved local secret, then profile env var, then missing.
/// </summary>
public sealed class ModelProviderCredentialResolver
{
    private readonly IModelProviderSecretStore _secretStore;

    public ModelProviderCredentialResolver(IModelProviderSecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public CredentialResolution Resolve(ModelDecisionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_profileDoesNotNeedCredential(profile))
        {
            return new CredentialResolution(null, CredentialSource.None, IsMissing: false);
        }

        if (_secretStore.TryGetApiKey(profile.Id, out var stored) &&
            !string.IsNullOrWhiteSpace(stored))
        {
            return new CredentialResolution(stored.Trim(), CredentialSource.Stored, IsMissing: false);
        }

        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar))
        {
            var environmentValue = Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return new CredentialResolution(environmentValue.Trim(), CredentialSource.EnvVar, IsMissing: false);
            }
        }

        return new CredentialResolution(null, CredentialSource.None, IsMissing: true);
    }

    private static bool _profileDoesNotNeedCredential(ModelDecisionProfile profile)
    {
        return profile.AuthScheme == ModelAuthScheme.None || !profile.RequiresApiKey;
    }
}
