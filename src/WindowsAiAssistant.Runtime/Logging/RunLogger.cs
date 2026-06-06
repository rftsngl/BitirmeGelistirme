using System.Text.Json;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Logging;

public sealed class RunLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly RuntimeOptions _options;

    public RunLogger(RuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string LogsDirectoryFullPath => Path.GetFullPath(_options.LogsDirectory);

    public string GetLogFilePath(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return Path.Combine(LogsDirectoryFullPath, $"{runId}.jsonl");
    }

    public IReadOnlyList<string> ListRunFilePaths(int maxCount = 50)
    {
        var directory = LogsDirectoryFullPath;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(directory, "*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Clamp(maxCount, 1, 500))
            .ToList();
    }

    public Task AppendAsync(AgentRunLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetLogFilePath(entry.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        return File.AppendAllTextAsync(path, line, cancellationToken);
    }
}
