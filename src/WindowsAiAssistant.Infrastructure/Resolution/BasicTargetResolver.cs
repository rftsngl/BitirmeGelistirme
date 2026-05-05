using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Infrastructure.Launch;
using WindowsAiAssistant.Infrastructure.Targeting;

namespace WindowsAiAssistant.Infrastructure.Resolution;

public sealed class BasicTargetResolver : ITargetResolver
{
    private const string LegacyDemoCommandPrefix = "demo ";
    private static readonly string[] AppContextPhrases = ["current app", "active app", "this app"];
    private static readonly string[] WindowContextPhrases = ["current window", "active window", "this window"];
    private static readonly ILaunchTargetResolver LiteralLaunchTargetResolver = new LaunchTargetResolver();
    private readonly ApplicationTargetCatalog _applicationCatalog;

    public BasicTargetResolver()
        : this(new ApplicationTargetCatalog(useFallbackDefaults: true))
    {
    }

    public BasicTargetResolver(ExecutionPolicySettings? settings)
        : this(new ApplicationTargetCatalog(settings))
    {
    }

    private BasicTargetResolver(ApplicationTargetCatalog applicationCatalog)
    {
        _applicationCatalog = applicationCatalog;
    }

    public Task<IReadOnlyList<TargetReference>> ResolveTargetsAsync(
        CommandRequest request,
        ObservationSnapshot? observation,
        CancellationToken cancellationToken = default)
    {
        var input = (request.UserInput ?? string.Empty).Trim();
        if (input.StartsWith(LegacyDemoCommandPrefix, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(input))
        {
            return Task.FromResult<IReadOnlyList<TargetReference>>([]);
        }

        var normalized = input.ToLowerInvariant();
        var targets = new List<TargetReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryCreateExplicitServiceTarget(normalized, input, out var serviceTarget))
        {
            AddTarget(targets, seen, serviceTarget);
        }

        if (TryCreateLiteralTarget(input, out var literalTarget))
        {
            AddTarget(targets, seen, literalTarget);
        }

        if (TryFindPhrase(normalized, AppContextPhrases, out var appPhrase) &&
            TryCreateCurrentAppTarget(input, observation, appPhrase, out var contextAppTarget))
        {
            AddTarget(targets, seen, contextAppTarget);
        }

        if (TryFindPhrase(normalized, WindowContextPhrases, out var windowPhrase) &&
            TryCreateCurrentWindowTarget(input, observation, windowPhrase, out var windowTarget))
        {
            AddTarget(targets, seen, windowTarget);
        }

        foreach (var appTarget in CreateCatalogMatchTargets(input))
        {
            AddTarget(targets, seen, appTarget);
        }

        return Task.FromResult<IReadOnlyList<TargetReference>>(targets);
    }

    private IEnumerable<TargetReference> CreateCatalogMatchTargets(string input)
    {
        foreach (var (matchedText, canonicalValue) in _applicationCatalog.FindMentionMatches(input))
        {
            var reason = TargetResolutionReasonPolicy.BuildCatalogMatchReason(matchedText);
            yield return new TargetReference
            {
                Kind = TargetKind.Application,
                OriginalText = input,
                NormalizedValue = canonicalValue,
                DisplayName = canonicalValue,
                Confidence = 0.85,
                ResolutionReasonKind = reason.ReasonKind,
                ResolutionSourceText = reason.SourceText,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["intentHint"] = "application-catalog-match",
                    ["catalogMatch"] = matchedText
                }
            };
        }
    }

    private static void AddTarget(ICollection<TargetReference> targets, ISet<string> seen, TargetReference target)
    {
        var targetKey = $"{target.Kind}:{target.NormalizedValue}";
        if (string.IsNullOrWhiteSpace(target.NormalizedValue) || !seen.Add(targetKey))
        {
            return;
        }

        targets.Add(target);
    }

    private static bool TryCreateExplicitServiceTarget(
        string normalizedInput,
        string originalInput,
        out TargetReference target)
    {
        target = new TargetReference();

        if (!TargetResolutionReasonPolicy.TryMatchExplicitService(
                normalizedInput,
                out var serviceValue,
                out var matchedKeyword))
        {
            return false;
        }

        var reason = TargetResolutionReasonPolicy.BuildExplicitKeywordReason(matchedKeyword);
        target = new TargetReference
        {
            Kind = TargetKind.Service,
            OriginalText = originalInput,
            NormalizedValue = serviceValue,
            DisplayName = serviceValue,
            Confidence = 0.8,
            ResolutionReasonKind = reason.ReasonKind,
            ResolutionSourceText = reason.SourceText
        };

        return true;
    }

    private static bool TryCreateLiteralTarget(string originalInput, out TargetReference target)
    {
        target = new TargetReference();

        if (!TryExtractQuotedLiteral(originalInput, out var literalValue) &&
            !TryExtractWholeInputLiteral(originalInput, out literalValue))
        {
            return false;
        }

        var launchTarget = LiteralLaunchTargetResolver.Resolve(new LaunchTargetReference
        {
            RawReference = literalValue
        });

        if (!TryMapLiteralTarget(launchTarget, literalValue, out var kind, out var resolutionSourceText, out var metadataKey))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = kind,
            OriginalText = originalInput,
            NormalizedValue = literalValue,
            DisplayName = literalValue,
            Confidence = 0.9,
            ResolutionReasonKind = TargetResolutionReasonKind.ExplicitKeyword,
            ResolutionSourceText = resolutionSourceText,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [metadataKey] = literalValue
            }
        };

        return true;
    }

    private static bool TryMapLiteralTarget(
        LaunchTarget launchTarget,
        string literalValue,
        out TargetKind kind,
        out string resolutionSourceText,
        out string metadataKey)
    {
        kind = TargetKind.Unknown;
        resolutionSourceText = string.Empty;
        metadataKey = string.Empty;

        switch (launchTarget.Kind)
        {
            case LaunchTargetKind.ExecutablePath:
                kind = TargetKind.Path;
                resolutionSourceText = "literal-path";
                metadataKey = "path";
                return true;
            case LaunchTargetKind.DocumentPath:
                kind = TargetKind.File;
                resolutionSourceText = "literal-file";
                metadataKey = "filePath";
                return true;
            case LaunchTargetKind.Url:
                kind = TargetKind.Url;
                resolutionSourceText = "literal-url";
                metadataKey = "url";
                return true;
            default:
                return false;
        }
    }

    private static bool TryExtractQuotedLiteral(string input, out string literalValue)
    {
        literalValue = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var startQuote = input.IndexOf('"');
        if (startQuote < 0)
        {
            return false;
        }

        var endQuote = input.IndexOf('"', startQuote + 1);
        if (endQuote <= startQuote + 1)
        {
            return false;
        }

        var candidate = NormalizeLiteralValue(input[(startQuote + 1)..endQuote]);
        if (!LooksLikeLiteralValue(candidate))
        {
            return false;
        }

        literalValue = candidate;
        return true;
    }

    private static bool TryExtractWholeInputLiteral(string input, out string literalValue)
    {
        literalValue = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = NormalizeLiteralValue(input);
        if (!LooksLikeLiteralValue(candidate) || candidate.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        literalValue = candidate;
        return true;
    }

    private static string NormalizeLiteralValue(string rawValue)
    {
        var normalized = rawValue.Trim().Trim('"', '\'');
        if (normalized.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        if (!normalized.Contains("\\\\", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var remainder = normalized[2..];
            while (remainder.Contains("\\\\", StringComparison.Ordinal))
            {
                remainder = remainder.Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            return "\\\\" + remainder;
        }

        while (normalized.Contains("\\\\", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static bool LooksLikeLiteralValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var launchTarget = LiteralLaunchTargetResolver.Resolve(new LaunchTargetReference
        {
            RawReference = value
        });

        if (launchTarget.Kind == LaunchTargetKind.Url)
        {
            return true;
        }

        return value.Contains("\\", StringComparison.Ordinal) ||
               value.Contains("/", StringComparison.Ordinal) ||
               value.Contains(":", StringComparison.Ordinal) ||
               Path.HasExtension(value);
    }

    private static bool LooksLikeServiceTarget(string value)
    {
        return value.Contains(" service", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("servis", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("service", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("servis", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindPhrase(string input, IReadOnlyList<string> phrases, out string matchedPhrase)
    {
        matchedPhrase = string.Empty;

        foreach (var phrase in phrases)
        {
            if (input.Contains(phrase, StringComparison.Ordinal))
            {
                matchedPhrase = phrase;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateCurrentAppTarget(
        string originalInput,
        ObservationSnapshot? observation,
        string matchedPhrase,
        out TargetReference target)
    {
        target = new TargetReference();

        var processName = observation?.ActiveProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = observation?.ActiveWindow?.ProcessName;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalizedProcess = processName.Trim().ToLowerInvariant();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "observation",
            ["processName"] = processName
        };

        if (!string.IsNullOrWhiteSpace(observation?.ActiveWindow?.Title))
        {
            metadata["windowTitle"] = observation.ActiveWindow.Title;
        }

        if (observation?.ActiveWindow?.Handle is long handle)
        {
            metadata["windowHandle"] = handle.ToString();
        }

        var reason = TargetResolutionReasonPolicy.BuildObservationContextReason(matchedPhrase);

        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = originalInput,
            NormalizedValue = normalizedProcess,
            DisplayName = processName,
            Confidence = 0.85,
            ResolutionReasonKind = reason.ReasonKind,
            ResolutionSourceText = reason.SourceText,
            Metadata = metadata
        };

        return true;
    }

    private static bool TryCreateCurrentWindowTarget(
        string originalInput,
        ObservationSnapshot? observation,
        string matchedPhrase,
        out TargetReference target)
    {
        target = new TargetReference();

        if (observation?.ActiveWindow?.Handle is not long handle)
        {
            return false;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "observation",
            ["windowHandle"] = handle.ToString()
        };

        if (!string.IsNullOrWhiteSpace(observation.ActiveWindow.Title))
        {
            metadata["windowTitle"] = observation.ActiveWindow.Title;
        }

        if (!string.IsNullOrWhiteSpace(observation.ActiveWindow.ProcessName))
        {
            metadata["processName"] = observation.ActiveWindow.ProcessName;
        }

        var reason = TargetResolutionReasonPolicy.BuildObservationContextReason(matchedPhrase);
        target = new TargetReference
        {
            Kind = TargetKind.Window,
            OriginalText = originalInput,
            NormalizedValue = handle.ToString(),
            DisplayName = observation.ActiveWindow.Title ?? handle.ToString(),
            Confidence = 0.85,
            ResolutionReasonKind = reason.ReasonKind,
            ResolutionSourceText = reason.SourceText,
            Metadata = metadata
        };

        return true;
    }
}
