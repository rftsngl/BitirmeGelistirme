using System.Collections.Concurrent;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class FileWatchIntegrationService : IFileWatchService
{
    private readonly ConcurrentDictionary<string, WatchEntry> _watches = new(StringComparer.OrdinalIgnoreCase);

    public ActionResult Execute(string mode, string? watchId = null, string? path = null, string? filter = null, bool recursive = false, int maxEvents = 32)
    {
        var normalized = (mode ?? "start").Trim().ToLowerInvariant();
        return normalized switch
        {
            "start" or "watch" => StartWatch(path, filter, recursive),
            "stop" or "unwatch" => StopWatch(watchId),
            "peek" or "read" => Peek(watchId, maxEvents),
            "list" => ListWatches(),
            _ => IntegrationResultHelper.Fail("Desteklenen modlar: start, stop, peek, list.")
        };
    }

    private ActionResult StartWatch(string? path, string? filter, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return IntegrationResultHelper.Fail("start icin var olan bir klasor path gerekli.");
        }

        try
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            var entry = new WatchEntry(path, filter ?? "*.*", recursive);
            if (!_watches.TryAdd(id, entry))
            {
                entry.Dispose();
                return IntegrationResultHelper.Fail("Izleyici olusturulamadi.");
            }

            return IntegrationResultHelper.Ok($"watchId={id}\npath={path}\nfilter={filter ?? "*.*"}\nrecursive={recursive}");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Dosya izleyici baslatilamadi: {ex.Message}");
        }
    }

    private ActionResult StopWatch(string? watchId)
    {
        if (string.IsNullOrWhiteSpace(watchId))
        {
            return IntegrationResultHelper.Fail("stop icin watchId gerekli.");
        }

        if (_watches.TryRemove(watchId, out var entry))
        {
            entry.Dispose();
            return IntegrationResultHelper.Ok($"Izleyici durduruldu: {watchId}");
        }

        return IntegrationResultHelper.Fail($"Izleyici bulunamadi: {watchId}");
    }

    private ActionResult Peek(string? watchId, int maxEvents)
    {
        if (string.IsNullOrWhiteSpace(watchId))
        {
            return IntegrationResultHelper.Fail("peek icin watchId gerekli.");
        }

        if (!_watches.TryGetValue(watchId, out var entry))
        {
            return IntegrationResultHelper.Fail($"Izleyici bulunamadi: {watchId}");
        }

        return IntegrationResultHelper.Ok(entry.DrainEvents(maxEvents));
    }

    private ActionResult ListWatches()
    {
        var builder = new StringBuilder();
        foreach (var pair in _watches)
        {
            builder.AppendLine($"{pair.Key} -> {pair.Value.Path}");
        }

        return IntegrationResultHelper.Ok(builder.Length == 0 ? "(aktif izleyici yok)" : builder.ToString());
    }

    private sealed class WatchEntry : IDisposable
    {
        private readonly ConcurrentQueue<string> _events = new();
        private readonly FileSystemWatcher _watcher;

        public WatchEntry(string path, string filter, bool recursive)
        {
            Path = path;
            _watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = recursive,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, e) => Enqueue("Created", e.FullPath);
            _watcher.Changed += (_, e) => Enqueue("Changed", e.FullPath);
            _watcher.Deleted += (_, e) => Enqueue("Deleted", e.FullPath);
            _watcher.Renamed += (_, e) => Enqueue($"Renamed {e.OldFullPath} -> {e.FullPath}", e.FullPath);
        }

        public string Path { get; }

        public string DrainEvents(int maxEvents)
        {
            var builder = new StringBuilder();
            var count = 0;
            while (count < maxEvents && _events.TryDequeue(out var line))
            {
                count++;
                builder.AppendLine(line);
            }

            builder.AppendLine($"count={count}");
            return builder.ToString();
        }

        private void Enqueue(string kind, string fullPath) =>
            _events.Enqueue($"{DateTimeOffset.Now:O} {kind} {fullPath}");

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
