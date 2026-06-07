using System.Text;
using Windows.Storage;
using Windows.Storage.Search;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinFileSearchService : IFileSearchService
{
    public async Task<ActionResult> ExecuteAsync(
        string mode,
        string? query = null,
        string? folder = null,
        int maxResults = 25,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!(mode ?? "search").Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            return IntegrationResultHelper.Fail("Desteklenen mod: search.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return IntegrationResultHelper.Fail("search icin target veya parameters.query gerekli.");
        }

        try
        {
            var root = await ResolveFolderAsync(folder).ConfigureAwait(false);
            var options = new QueryOptions(CommonFileQuery.DefaultQuery, [])
            {
                ApplicationSearchFilter = query.Trim(),
                FolderDepth = FolderDepth.Deep
            };

            var result = root.CreateFileQueryWithOptions(options);
            var files = await result.GetFilesAsync(0, (uint)Math.Clamp(maxResults, 1, 100))
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            var builder = new StringBuilder();
            builder.AppendLine($"query={query}");
            builder.AppendLine($"folder={root.Path}");
            builder.AppendLine($"count={files.Count}");

            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                builder.AppendLine($"{file.Name} | {file.Path} | {props.Size} bytes");
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Dosya aramasi basarisiz: {ex.Message}");
        }
    }

    private static async Task<StorageFolder> ResolveFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return await StorageFolder.GetFolderFromPathAsync(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).AsTask().ConfigureAwait(false);
        }

        var trimmed = folder.Trim();
        var path = trimmed.ToLowerInvariant() switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => trimmed
        };

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Klasor bulunamadi: {path}");
        }

        return await StorageFolder.GetFolderFromPathAsync(path).AsTask().ConfigureAwait(false);
    }
}
