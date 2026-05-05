namespace WindowsAiAssistant.Infrastructure.State;

/// <summary>
/// Persists the user's selected active model profile id (not secret).
/// When no override is stored, callers fall back to configuration defaults.
/// </summary>
public interface IActiveProfileStore
{
    /// <summary>
    /// Returns the persisted active profile id, or null when the user has not chosen an override yet.
    /// </summary>
    string? GetActiveProfileId();

    void SetActiveProfileId(string profileId);

    void ClearOverride();
}
