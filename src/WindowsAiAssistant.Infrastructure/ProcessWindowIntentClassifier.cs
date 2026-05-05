using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Infrastructure;

public static class ProcessWindowIntentClassifier
{
    private static readonly string[] AlignmentAppTargetPhrases =
    [
        "current app",
        "active app",
        "this app"
    ];

    private static readonly string[] AlignmentWindowTargetPhrases =
    [
        "current window",
        "active window",
        "this window"
    ];

    private static readonly string[] FocusCurrentWindowPhrases =
    [
        "current window",
        "focus window",
        "focus current window"
    ];

    private static readonly string[] FocusCurrentAppPhrases =
    [
        "current app",
        "focus app",
        "focus current app"
    ];

    public static ProcessWindowIntentClassification Classify(AiDecisionInput? aiDecisionInput, string fallbackInput)
    {
        var effectiveInput = GetEffectiveInput(aiDecisionInput, fallbackInput);
        if (string.IsNullOrWhiteSpace(effectiveInput))
        {
            return ProcessWindowIntentClassification.Unknown;
        }

        var trimmed = effectiveInput.Trim();

        if (FocusCurrentWindowPhrases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return new ProcessWindowIntentClassification(ProcessWindowIntentKind.FocusWindow, "current window");
        }

        if (FocusCurrentAppPhrases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return new ProcessWindowIntentClassification(ProcessWindowIntentKind.FocusWindow, "current app");
        }

        if (TryExtractRunningVerificationTarget(trimmed, out var processTarget))
        {
            return new ProcessWindowIntentClassification(ProcessWindowIntentKind.VerifyProcess, processTarget);
        }

        if (TryExtractForegroundAlignmentTarget(trimmed, out var alignmentTarget))
        {
            return new ProcessWindowIntentClassification(ProcessWindowIntentKind.VerifyForegroundAlignment, alignmentTarget);
        }

        return ProcessWindowIntentClassification.Unknown;
    }

    private static string GetEffectiveInput(AiDecisionInput? aiDecisionInput, string fallbackInput)
    {
        if (!string.IsNullOrWhiteSpace(aiDecisionInput?.RawInput))
        {
            return aiDecisionInput.RawInput.Trim();
        }

        if (!string.IsNullOrWhiteSpace(aiDecisionInput?.NormalizedInput))
        {
            return aiDecisionInput.NormalizedInput.Trim();
        }

        return fallbackInput.Trim();
    }

    private static bool TryExtractRunningVerificationTarget(string input, out string processTarget)
    {
        processTarget = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.StartsWith("is ", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(" running", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length <= 11)
            {
                return false;
            }

            processTarget = NormalizeProcessTarget(input[3..^8]);
            return !string.IsNullOrWhiteSpace(processTarget) && !LooksLikeServiceTarget(processTarget);
        }

        if (input.StartsWith("check ", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(" running", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length <= 14)
            {
                return false;
            }

            processTarget = NormalizeProcessTarget(input[6..^8]);
            return !string.IsNullOrWhiteSpace(processTarget) && !LooksLikeServiceTarget(processTarget);
        }

        if (input.StartsWith("verify ", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(" running", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length <= 15)
            {
                return false;
            }

            processTarget = NormalizeProcessTarget(input[7..^8]);
            return !string.IsNullOrWhiteSpace(processTarget) && !LooksLikeServiceTarget(processTarget);
        }

        return false;
    }

    private static bool TryExtractForegroundAlignmentTarget(string input, out string alignmentTarget)
    {
        alignmentTarget = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.StartsWith("is ", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(" foreground", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length <= 14)
            {
                return false;
            }

            alignmentTarget = NormalizeAlignmentTarget(input[3..^11]);
            return !string.IsNullOrWhiteSpace(alignmentTarget);
        }

        if (input.StartsWith("is ", StringComparison.OrdinalIgnoreCase) &&
            input.EndsWith(" active", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Length <= 10)
            {
                return false;
            }

            alignmentTarget = NormalizeAlignmentTarget(input[3..^7]);
            return !string.IsNullOrWhiteSpace(alignmentTarget);
        }

        return false;
    }

    private static string NormalizeProcessTarget(string rawTarget)
    {
        var normalized = rawTarget.Trim();
        if (normalized.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..].Trim();
        }

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static string NormalizeAlignmentTarget(string rawTarget)
    {
        var normalized = rawTarget.Trim();
        if (normalized.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        if (AlignmentAppTargetPhrases.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return "current app";
        }

        if (AlignmentWindowTargetPhrases.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return "current window";
        }

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static bool LooksLikeServiceTarget(string target)
    {
        return target.EndsWith(" service", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("service", StringComparison.OrdinalIgnoreCase) ||
               target.Equals("servis", StringComparison.OrdinalIgnoreCase);
    }
}

public readonly record struct ProcessWindowIntentClassification(ProcessWindowIntentKind Kind, string? Target)
{
    public static ProcessWindowIntentClassification Unknown =>
        new(ProcessWindowIntentKind.Unknown, null);
}