using System.Net.Http;

namespace WindowsAiAssistant.App.Audio;

public sealed record ModelDownloadProgress(double Percent, string Stage);

public static class ModelFileDownloader
{
    public static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken,
        string stage = "İndiriliyor")
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = destination + ".part";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(tempPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;

            if (totalBytes is > 0)
            {
                progress?.Report(new ModelDownloadProgress(downloaded * 100.0 / totalBytes.Value, stage));
            }
            else
            {
                progress?.Report(new ModelDownloadProgress(-1, stage));
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(tempPath, destination);
        progress?.Report(new ModelDownloadProgress(100, stage));
    }
}
