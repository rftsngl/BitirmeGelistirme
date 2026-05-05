namespace WindowsAiAssistant.Infrastructure.State;

/// <summary>
/// Persists user-defined model provider profiles. Built-in profiles continue to live in appsettings.json
/// and are not written by this store. Stored data does not contain API keys.
/// </summary>
public interface IUserProviderProfileStore
{
    IReadOnlyList<ModelDecisionProfile> LoadAll();

    void Save(ModelDecisionProfile profile);

    void Delete(string profileId);
}
