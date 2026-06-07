using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ScreenCaptureService
{
    private const int MaxVisionWidth = 1280;

    private readonly RuntimeOptions _options;

    public ScreenCaptureService(RuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ScreenshotObservation Capture(string runId, int stepIndex) =>
        CaptureRegion(runId, stepIndex, ResolvePrimaryBounds(), "primary");

    public ScreenshotObservation CaptureRegion(string runId, int stepIndex, Rectangle bounds, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        using var fullBitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(fullBitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        using var visionBitmap = ResizeIfNeeded(fullBitmap, MaxVisionWidth);

        var directory = Path.GetFullPath(_options.ScreenshotsDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{runId}_step{stepIndex}_{label}.png";
        var filePath = Path.Combine(directory, fileName);
        visionBitmap.Save(filePath, ImageFormat.Png);

        using var memoryStream = new MemoryStream();
        visionBitmap.Save(memoryStream, ImageFormat.Png);
        var base64 = Convert.ToBase64String(memoryStream.ToArray());

        return new ScreenshotObservation
        {
            FilePath = filePath,
            Base64Png = base64,
            Width = visionBitmap.Width,
            Height = visionBitmap.Height,
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    public IReadOnlyList<(Rectangle Bounds, string Label)> ListMonitorBounds()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return [(new Rectangle(0, 0, 1920, 1080), "primary")];
        }

        return screens
            .Select((screen, index) => (screen.Bounds, index == 0 ? "primary" : $"monitor{index}"))
            .ToList();
    }

    public Rectangle ResolveMonitorBounds(string? monitor)
    {
        var normalized = (monitor ?? "primary").Trim().ToLowerInvariant();
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return new Rectangle(0, 0, 1920, 1080);
        }

        if (normalized is "primary" or "0")
        {
            return screens[0].Bounds;
        }

        if (normalized.StartsWith("monitor", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized["monitor".Length..], out var index) &&
            index >= 0 &&
            index < screens.Length)
        {
            return screens[index].Bounds;
        }

        if (int.TryParse(normalized, out var numeric) && numeric >= 0 && numeric < screens.Length)
        {
            return screens[numeric].Bounds;
        }

        return screens[0].Bounds;
    }

    private static Rectangle ResolvePrimaryBounds() =>
        System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

    private static Bitmap ResizeIfNeeded(Bitmap original, int maxWidth)
    {
        if (original.Width <= maxWidth)
        {
            return new Bitmap(original);
        }

        var ratio = (double)maxWidth / original.Width;
        var newHeight = Math.Max(1, (int)(original.Height * ratio));
        var resized = new Bitmap(maxWidth, newHeight);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, 0, 0, maxWidth, newHeight);
        return resized;
    }
}
