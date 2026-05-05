namespace WindowsAiAssistant.Infrastructure.State;

/// <summary>
/// Read-only marker for which provider profile ids ship with the application (loaded from configuration).
/// User-defined profiles are everything that is not present in this set.
/// </summary>
public interface IBuiltInProfileSet
{
    bool IsBuiltIn(string profileId);
    IReadOnlyCollection<string> BuiltInIds { get; }
}

public sealed class BuiltInProfileSet : IBuiltInProfileSet
{
    private readonly HashSet<string> _ids;

    public BuiltInProfileSet(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _ids = new HashSet<string>(ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsBuiltIn(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) && _ids.Contains(profileId.Trim());

    public IReadOnlyCollection<string> BuiltInIds => _ids;
}
