using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class TextInputPostconditionVerificationPrimitive : IVerificationPrimitive
{
    private const string ExpectedTextParameter = "expectedText";
    private const string ExpectedTargetHintParameter = "expectedTargetHint";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.TextInputPostcondition;

    public Task<VerificationPrimitiveResult> EvaluateAsync(
        VerificationPrimitiveSpec specification,
        VerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(request);

        var startedAtUtc = DateTimeOffset.UtcNow;
        if (specification.Kind != Kind)
        {
            return Task.FromResult(CreateResult(
                specification.Kind,
                VerificationStatus.Unsupported,
                "Text-input postcondition primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var expectedText = ResolveRequiredParameter(specification, ExpectedTextParameter);
        if (string.IsNullOrWhiteSpace(expectedText))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "Text-input postcondition verification is unsupported because expected text is missing.",
                "expected_text_missing",
                null,
                startedAtUtc));
        }

        var expectedTargetHint = ResolveOptionalParameter(specification, ExpectedTargetHintParameter);
        var selectionText = request.ExecutionContext?.Observation?.SelectionTextPreview;
        var clipboardText = request.ExecutionContext?.Observation?.ClipboardTextPreview;
        var activeProcess = request.ExecutionContext?.Observation?.ActiveProcessName
                            ?? request.ExecutionContext?.Observation?.ActiveWindow?.ProcessName
                            ?? request.ExecutionContext?.RichObservation?.ForegroundProcess?.Name;
        var activeTitle = request.ExecutionContext?.Observation?.ActiveWindow?.Title
                          ?? request.ExecutionContext?.RichObservation?.ForegroundWindow?.Title;

        var textSignalAvailable = !string.IsNullOrWhiteSpace(selectionText) || !string.IsNullOrWhiteSpace(clipboardText);
        var textMatched = ContainsExpectedText(selectionText, expectedText) || ContainsExpectedText(clipboardText, expectedText);

        if (!textSignalAvailable)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "Text-input postcondition is inconclusive because no text observation signal is available.",
                "text_signal_unavailable",
                BuildEvidence(expectedText, expectedTargetHint, selectionText, clipboardText, activeProcess, activeTitle),
                startedAtUtc));
        }

        if (!textMatched)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.NotVerified,
                "Text-input postcondition failed because observed text does not match expected payload.",
                "text_payload_mismatch",
                BuildEvidence(expectedText, expectedTargetHint, selectionText, clipboardText, activeProcess, activeTitle),
                startedAtUtc));
        }

        if (!string.IsNullOrWhiteSpace(expectedTargetHint))
        {
            var targetMatched = ContainsHint(activeProcess, expectedTargetHint) || ContainsHint(activeTitle, expectedTargetHint);
            if (!targetMatched)
            {
                return Task.FromResult(CreateResult(
                    Kind,
                    VerificationStatus.NotVerified,
                    "Text-input postcondition failed because foreground target does not match expected target hint.",
                    "target_hint_mismatch",
                    BuildEvidence(expectedText, expectedTargetHint, selectionText, clipboardText, activeProcess, activeTitle),
                    startedAtUtc));
            }
        }

        return Task.FromResult(CreateResult(
            Kind,
            VerificationStatus.Verified,
            "Text-input postcondition was verified from observed text signals and target-bound context.",
            "text_input_verified",
            BuildEvidence(expectedText, expectedTargetHint, selectionText, clipboardText, activeProcess, activeTitle),
            startedAtUtc));
    }

    private static string ResolveRequiredParameter(VerificationPrimitiveSpec specification, string key)
    {
        if (specification.Parameters is null ||
            !specification.Parameters.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static string ResolveOptionalParameter(VerificationPrimitiveSpec specification, string key)
    {
        if (specification.Parameters is null ||
            !specification.Parameters.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static bool ContainsExpectedText(string? observedText, string expectedText)
    {
        return !string.IsNullOrWhiteSpace(observedText) &&
               observedText.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHint(string? observedValue, string expectedHint)
    {
        return !string.IsNullOrWhiteSpace(observedValue) &&
               observedValue.Contains(expectedHint, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildEvidence(
        string expectedText,
        string expectedTargetHint,
        string? selectionText,
        string? clipboardText,
        string? activeProcess,
        string? activeTitle)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expectedText"] = expectedText
        };

        if (!string.IsNullOrWhiteSpace(expectedTargetHint))
        {
            evidence["expectedTargetHint"] = expectedTargetHint;
        }

        if (!string.IsNullOrWhiteSpace(selectionText))
        {
            evidence["selectionText"] = selectionText;
        }

        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            evidence["clipboardText"] = clipboardText;
        }

        if (!string.IsNullOrWhiteSpace(activeProcess))
        {
            evidence["activeProcess"] = activeProcess;
        }

        if (!string.IsNullOrWhiteSpace(activeTitle))
        {
            evidence["activeTitle"] = activeTitle;
        }

        return evidence;
    }

    private static VerificationPrimitiveResult CreateResult(
        VerificationPrimitiveKind kind,
        VerificationStatus status,
        string reason,
        string evidenceCode,
        IDictionary<string, string>? evidenceData,
        DateTimeOffset startedAtUtc)
    {
        return new VerificationPrimitiveResult
        {
            PrimitiveKind = kind,
            Status = status,
            Reason = reason,
            Evidence =
            [
                new VerificationEvidence
                {
                    Code = evidenceCode,
                    Message = reason,
                    Data = evidenceData
                }
            ],
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
