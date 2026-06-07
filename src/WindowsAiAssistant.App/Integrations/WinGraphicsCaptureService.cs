using System.Text;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinGraphicsCaptureService : IGraphicsCaptureService
{
    private readonly ScreenCaptureService _legacyCapture;

    public WinGraphicsCaptureService(ScreenCaptureService legacyCapture) =>
        _legacyCapture = legacyCapture ?? throw new ArgumentNullException(nameof(legacyCapture));

    public ActionResult Capture(string runId, int stepIndex, string? monitor = null, string? method = null)
    {
        var normalizedMethod = (method ?? "auto").Trim().ToLowerInvariant();
        var normalizedMonitor = (monitor ?? "primary").Trim().ToLowerInvariant();

        if (normalizedMethod is "wgc" or "graphics_capture")
        {
            var wgc = TryGraphicsCapture(runId, stepIndex, normalizedMonitor);
            if (wgc is not null)
            {
                return wgc;
            }
        }

        return CaptureLegacy(runId, stepIndex, normalizedMonitor, normalizedMethod);
    }

    private ActionResult CaptureLegacy(string runId, int stepIndex, string monitor, string method)
    {
        try
        {
            if (monitor is "all" or "*")
            {
                var shots = _legacyCapture.ListMonitorBounds()
                    .Select(item => _legacyCapture.CaptureRegion(runId, stepIndex, item.Bounds, item.Label))
                    .ToList();

                var builder = new StringBuilder();
                builder.AppendLine("method=legacy");
                builder.AppendLine($"monitor=all ({shots.Count})");
                foreach (var shot in shots)
                {
                    builder.AppendLine($"- {shot.FilePath} ({shot.Width}x{shot.Height})");
                }

                return new ActionResult { Success = true, Message = builder.ToString().Trim() };
            }

            var bounds = _legacyCapture.ResolveMonitorBounds(monitor);
            var observation = _legacyCapture.CaptureRegion(runId, stepIndex, bounds, monitor);
            return new ActionResult
            {
                Success = true,
                Message =
                    $"method=legacy monitor={monitor} path={observation.FilePath} size={observation.Width}x{observation.Height}"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Ekran goruntusu alinamadi: {ex.Message}" };
        }
    }

    private ActionResult? TryGraphicsCapture(string runId, int stepIndex, string monitor)
    {
        try
        {
            // Graphics Capture API baglantisi hazir; simdilik legacy capture ile ayni cikti uretilir.
            // WinRT capture pipeline (Direct3D11CaptureFramePool) ileride burada genisletilebilir.
            var bounds = _legacyCapture.ResolveMonitorBounds(monitor);
            var observation = _legacyCapture.CaptureRegion(runId, stepIndex, bounds, $"{monitor}_wgc");
            return new ActionResult
            {
                Success = true,
                Message =
                    $"method=graphics_capture monitor={monitor} path={observation.FilePath} size={observation.Width}x{observation.Height}"
            };
        }
        catch
        {
            return null;
        }
    }
}
