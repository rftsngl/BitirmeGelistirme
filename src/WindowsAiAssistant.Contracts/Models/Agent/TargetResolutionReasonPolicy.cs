using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public static class TargetResolutionReasonPolicy
{
    public static bool TryMatchExplicitApplication(
        string normalizedInput,
        out string normalizedTarget,
        out string matchedKeyword)
    {
        normalizedTarget = string.Empty;
        matchedKeyword = string.Empty;

        if (normalizedInput.Contains("notepad", StringComparison.Ordinal))
        {
            normalizedTarget = "notepad";
            matchedKeyword = "notepad";
            return true;
        }

        if (normalizedInput.Contains("calculator", StringComparison.Ordinal))
        {
            normalizedTarget = "calc";
            matchedKeyword = "calculator";
            return true;
        }

        if (normalizedInput.Contains("calc", StringComparison.Ordinal))
        {
            normalizedTarget = "calc";
            matchedKeyword = "calc";
            return true;
        }

        return false;
    }

    public static bool TryMatchExplicitService(
        string normalizedInput,
        out string normalizedTarget,
        out string matchedKeyword)
    {
        normalizedTarget = string.Empty;
        matchedKeyword = string.Empty;

        if (normalizedInput.Contains("service", StringComparison.Ordinal))
        {
            normalizedTarget = "service";
            matchedKeyword = "service";
            return true;
        }

        if (normalizedInput.Contains("servis", StringComparison.Ordinal))
        {
            normalizedTarget = "service";
            matchedKeyword = "servis";
            return true;
        }

        return false;
    }

    public static (TargetResolutionReasonKind ReasonKind, string? SourceText) BuildExplicitKeywordReason(string matchedKeyword)
    {
        return (TargetResolutionReasonKind.ExplicitKeyword, matchedKeyword);
    }

    public static (TargetResolutionReasonKind ReasonKind, string? SourceText) BuildCatalogMatchReason(string matchedText)
    {
        return (TargetResolutionReasonKind.CatalogMatch, matchedText);
    }

    public static (TargetResolutionReasonKind ReasonKind, string? SourceText) BuildObservationContextReason(string matchedPhrase)
    {
        return (TargetResolutionReasonKind.ObservationContext, matchedPhrase);
    }

    public static bool TryPromoteObservationToAdapterContext(
        TargetReference target,
        string? adapterName,
        bool isAligned,
        out string? sourceText)
    {
        sourceText = target.ResolutionSourceText;

        if (!isAligned || string.IsNullOrWhiteSpace(adapterName))
        {
            return false;
        }

        if (target.ResolutionReasonKind != TargetResolutionReasonKind.ObservationContext)
        {
            return false;
        }

        sourceText = adapterName;
        return true;
    }

    public static bool TryNormalizeUnknownReason(
        TargetReference target,
        out TargetResolutionReasonKind reasonKind,
        out string? sourceText)
    {
        reasonKind = TargetResolutionReasonKind.Unknown;
        sourceText = target.ResolutionSourceText;

        if (target.ResolutionReasonKind != TargetResolutionReasonKind.Unknown)
        {
            return false;
        }

        if (TryNormalizeAsExplicitKeyword(target, out sourceText))
        {
            reasonKind = TargetResolutionReasonKind.ExplicitKeyword;
            return true;
        }

        if (TryNormalizeAsCatalogMatch(target, out sourceText))
        {
            reasonKind = TargetResolutionReasonKind.CatalogMatch;
            return true;
        }

        if (TryNormalizeAsObservationContext(target, out sourceText))
        {
            reasonKind = TargetResolutionReasonKind.ObservationContext;
            return true;
        }

        if (TryNormalizeAsAdapterContext(target, out sourceText))
        {
            reasonKind = TargetResolutionReasonKind.AdapterContext;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeAsExplicitKeyword(TargetReference target, out string? sourceText)
    {
        sourceText = target.ResolutionSourceText;

        if (target.Kind == TargetKind.Service &&
            target.NormalizedValue.Equals("service", StringComparison.OrdinalIgnoreCase))
        {
            sourceText = target.OriginalText.Contains("servis", StringComparison.OrdinalIgnoreCase)
                ? "servis"
                : "service";

            return true;
        }

        return false;
    }

    private static bool TryNormalizeAsObservationContext(TargetReference target, out string? sourceText)
    {
        sourceText = target.ResolutionSourceText;
        var metadata = target.Metadata;
        if (metadata is null)
        {
            return false;
        }

        if (metadata.TryGetValue("source", out var source) &&
            source.Equals("observation", StringComparison.OrdinalIgnoreCase))
        {
            sourceText = string.IsNullOrWhiteSpace(sourceText) ? "observation" : sourceText;
            return true;
        }

        if (metadata.ContainsKey("windowHandle") ||
            metadata.ContainsKey("windowTitle") ||
            metadata.ContainsKey("processName"))
        {
            sourceText = string.IsNullOrWhiteSpace(sourceText) ? "observation" : sourceText;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeAsCatalogMatch(TargetReference target, out string? sourceText)
    {
        sourceText = target.ResolutionSourceText;

        if (target.ResolutionReasonKind == TargetResolutionReasonKind.CatalogMatch)
        {
            return true;
        }

        if (target.Kind != TargetKind.Application)
        {
            return false;
        }

        var metadata = target.Metadata;
        if (metadata is null)
        {
            return false;
        }

        if (metadata.TryGetValue("catalogMatch", out var catalogMatch) &&
            !string.IsNullOrWhiteSpace(catalogMatch))
        {
            sourceText = catalogMatch;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeAsAdapterContext(TargetReference target, out string? sourceText)
    {
        sourceText = target.ResolutionSourceText;
        var metadata = target.Metadata;
        if (metadata is null)
        {
            return false;
        }

        if (metadata.TryGetValue("adapterContext", out var adapterContext) &&
            !string.IsNullOrWhiteSpace(adapterContext))
        {
            sourceText = adapterContext;
            return true;
        }

        if (metadata.TryGetValue("contextAdapter", out var contextAdapter) &&
            !string.IsNullOrWhiteSpace(contextAdapter))
        {
            sourceText = contextAdapter;
            return true;
        }

        return false;
    }
}
