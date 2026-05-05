using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsAiAssistant.Infrastructure.State;

/// <summary>
/// Stores active profile override as JSON in LocalApplicationData (non-secret metadata).
/// </summary>
public sealed class JsonActiveProfileStore : IActiveProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly object _sync = new();

    public JsonActiveProfileStore(string? stateDirectory = null)
    {
        var dir = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "state");
        _filePath = Path.Combine(dir, "active-profile.json");
    }

    public string? GetActiveProfileId()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var doc = JsonSerializer.Deserialize<ActiveProfileFile>(json, SerializerOptions);
                var id = doc?.ActiveProfileId;
                return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            }
            catch
            {
                return null;
            }
        }
    }

    public void SetActiveProfileId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new ActiveProfileFile { ActiveProfileId = profileId.Trim() };
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
    }

    public void ClearOverride()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch
            {
                // Fail-safe.
            }
        }
    }

    private sealed class ActiveProfileFile
    {
        [JsonPropertyName("activeProfileId")]
        public string? ActiveProfileId { get; set; }
    }
}
