namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchTargetResolver : ILaunchTargetResolver
{
    public LaunchTarget Resolve(LaunchTargetReference targetReference)
    {
        ArgumentNullException.ThrowIfNull(targetReference);

        var originalReference = targetReference.RawReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(originalReference))
        {
            return new LaunchTarget
            {
                Kind = LaunchTargetKind.Unknown,
                OriginalReference = string.Empty,
                NormalizedReference = string.Empty
            };
        }

        var resolvedKind = DetermineKind(originalReference, targetReference.PreferredKind);
        var normalizedReference = resolvedKind == LaunchTargetKind.RegisteredApplication
            ? originalReference.ToLowerInvariant()
            : originalReference;

        return new LaunchTarget
        {
            Kind = resolvedKind,
            OriginalReference = originalReference,
            NormalizedReference = normalizedReference
        };
    }

    private static LaunchTargetKind DetermineKind(string originalReference, LaunchTargetKind? preferredKind)
    {
        if (LooksLikeUrl(originalReference))
        {
            return LaunchTargetKind.Url;
        }

        if (LooksLikeFileSystemPath(originalReference))
        {
            return LooksLikeExecutablePath(originalReference)
                ? LaunchTargetKind.ExecutablePath
                : LaunchTargetKind.DocumentPath;
        }

        return preferredKind ?? LaunchTargetKind.RegisteredApplication;
    }

    private static bool LooksLikeUrl(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Scheme) &&
               uri.Scheme is not "file";
    }

    private static bool LooksLikeFileSystemPath(string input)
    {
        return input.Contains("\\", StringComparison.Ordinal) ||
               input.Contains("/", StringComparison.Ordinal) ||
               input.Contains(":", StringComparison.Ordinal) ||
               input.Contains("%", StringComparison.Ordinal) ||
               input.Contains("..", StringComparison.Ordinal) ||
               Path.HasExtension(input);
    }

    private static bool LooksLikeExecutablePath(string input)
    {
        var extension = Path.GetExtension(input);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".msc", StringComparison.OrdinalIgnoreCase);
    }
}
