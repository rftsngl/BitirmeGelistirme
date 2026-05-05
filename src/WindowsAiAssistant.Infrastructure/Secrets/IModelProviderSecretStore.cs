namespace WindowsAiAssistant.Infrastructure.Secrets;

/// <summary>
/// Persists model provider API keys per profile id using a platform-specific secure store.
/// Implementations must not write plaintext secrets to disk or logs.
/// </summary>
public interface IModelProviderSecretStore
{
    void SaveApiKey(string profileId, string secret);

    bool TryGetApiKey(string profileId, out string? secret);

    void DeleteApiKey(string profileId);

    bool HasApiKey(string profileId);
}
