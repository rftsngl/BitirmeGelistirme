using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class OpenUrlActionHandler : IActionHandler
{
    public string ActionName => "open_url";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = action.Target;
        if (string.IsNullOrWhiteSpace(url) &&
            action.Parameters.TryGetValue("url", out var urlParameter))
        {
            url = urlParameter;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "open_url icin target veya parameters.url gerekli."
            });
        }

        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "Gecersiz URL."
            });
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"URL acildi: {url}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"URL acilamadi: {ex.Message}"
            });
        }
    }
}
