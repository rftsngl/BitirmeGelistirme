using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsAiAssistant.Infrastructure.State;

/// <summary>
/// Stores user-defined provider profiles as JSON in LocalApplicationData.
/// Persisted data is non-secret; API keys live in <see cref="WindowsAiAssistant.Infrastructure.Secrets.IModelProviderSecretStore"/>.
/// </summary>
public sealed class JsonUserProviderProfileStore : IUserProviderProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _filePath;
    private readonly object _sync = new();

    public JsonUserProviderProfileStore(string? stateDirectory = null)
    {
        var dir = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "state");
        _filePath = Path.Combine(dir, "user-providers.json");
    }

    public IReadOnlyList<ModelDecisionProfile> LoadAll()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<ModelDecisionProfile>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var doc = JsonSerializer.Deserialize<UserProvidersFile>(json, SerializerOptions);
                if (doc?.Profiles is null)
                {
                    return Array.Empty<ModelDecisionProfile>();
                }

                return doc.Profiles
                    .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                    .Select(Sanitize)
                    .ToList();
            }
            catch
            {
                return Array.Empty<ModelDecisionProfile>();
            }
        }
    }

    public void Save(ModelDecisionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new ArgumentException("Profile id is required.", nameof(profile));
        }

        lock (_sync)
        {
            var current = LoadAllUnlocked().ToList();
            var existing = current.FindIndex(p =>
                p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            var sanitized = Sanitize(profile);
            if (existing >= 0)
            {
                current[existing] = sanitized;
            }
            else
            {
                current.Add(sanitized);
            }

            WriteAtomically(current);
        }
    }

    public void Delete(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        lock (_sync)
        {
            var current = LoadAllUnlocked().ToList();
            var removed = current.RemoveAll(p =>
                p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return;
            }

            WriteAtomically(current);
        }
    }

    private IReadOnlyList<ModelDecisionProfile> LoadAllUnlocked()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<ModelDecisionProfile>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var doc = JsonSerializer.Deserialize<UserProvidersFile>(json, SerializerOptions);
            if (doc?.Profiles is null)
            {
                return Array.Empty<ModelDecisionProfile>();
            }

            return doc.Profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .Select(Sanitize)
                .ToList();
        }
        catch
        {
            return Array.Empty<ModelDecisionProfile>();
        }
    }

    private void WriteAtomically(IReadOnlyList<ModelDecisionProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new UserProvidersFile { Profiles = profiles.Select(Sanitize).ToList() };
        var tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }

        if (File.Exists(tempPath))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static ModelDecisionProfile Sanitize(ModelDecisionProfile profile) =>
        new()
        {
            Id = profile.Id.Trim(),
            Kind = profile.Kind,
            DisplayName = profile.DisplayName?.Trim() ?? string.Empty,
            BaseUrl = profile.BaseUrl?.Trim() ?? string.Empty,
            Model = profile.Model?.Trim() ?? string.Empty,
            EndpointStyle = profile.EndpointStyle,
            EndpointPath = profile.EndpointPath?.Trim() ?? string.Empty,
            RequiresApiKey = profile.RequiresApiKey,
            ApiKeyEnvVar = profile.ApiKeyEnvVar?.Trim() ?? string.Empty,
            ApiKeyHeaderName = string.IsNullOrWhiteSpace(profile.ApiKeyHeaderName)
                ? "Authorization"
                : profile.ApiKeyHeaderName.Trim(),
            AuthScheme = profile.AuthScheme,
            IsEnabled = profile.IsEnabled
        };

    private sealed class UserProvidersFile
    {
        [JsonPropertyName("profiles")]
        public List<ModelDecisionProfile>? Profiles { get; set; }
    }
}
