using System.Drawing;
using System.Drawing.Imaging;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ScreenCaptureService
{
    private const int MaxVisionWidth = 1280;

    private readonly RuntimeOptions _options;

    public ScreenCaptureService(RuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ScreenshotObservation Capture(string runId, int stepIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                           ?? new Rectangle(0, 0, 1920, 1080);

        using var fullBitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
        using (var graphics = Graphics.FromImage(fullBitmap))
        {
            graphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
        }

        using var visionBitmap = ResizeIfNeeded(fullBitmap, MaxVisionWidth);

        var directory = Path.GetFullPath(_options.ScreenshotsDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{runId}_step{stepIndex}.png";
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
