using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class KeyInteractionPostconditionVerificationPrimitive : IVerificationPrimitive
{
    private const string InteractionSemanticParameter = "interactionSemantic";
    private const string ExpectedTargetHintParameter = "expectedTargetHint";
    private const string ExpectedKeyParameter = "expectedKey";
    private const string ExpectedShortcutParameter = "expectedShortcut";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.KeyInteractionPostcondition;

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
                "Key-interaction postcondition primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var semantic = ResolveParameter(specification, InteractionSemanticParameter);
        if (string.IsNullOrWhiteSpace(semantic))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "Key-interaction postcondition verification is unsupported because semantic is missing.",
                "interaction_semantic_missing",
                null,
                startedAtUtc));
        }

        var expectedTargetHint = ResolveParameter(specification, ExpectedTargetHintParameter);
        var expectedKey = ResolveParameter(specification, ExpectedKeyParameter);
        var expectedShortcut = ResolveParameter(specification, ExpectedShortcutParameter);

        var postProcess = request.ExecutionContext?.Observation?.ActiveProcessName
                          ?? request.ExecutionContext?.Observation?.ActiveWindow?.ProcessName
                          ?? request.ExecutionContext?.RichObservation?.ForegroundProcess?.Name;
        var postTitle = request.ExecutionContext?.Observation?.ActiveWindow?.Title
                        ?? request.ExecutionContext?.RichObservation?.ForegroundWindow?.Title;
        var postSelection = request.ExecutionContext?.Observation?.SelectionTextPreview;
        var postClipboard = request.ExecutionContext?.Observation?.ClipboardTextPreview;

        var preProcess = ResolveMetadata(request, "preActiveProcess");
        var preTitle = ResolveMetadata(request, "preActiveWindowTitle");
        var preSelection = ResolveMetadata(request, "preSelectionText");
        var preClipboard = ResolveMetadata(request, "preClipboardText");

        var contextDelta = !StringEquals(preProcess, postProcess) || !StringEquals(preTitle, postTitle);
        var textDelta = !StringEquals(preSelection, postSelection) || !StringEquals(preClipboard, postClipboard);
        var hasAnyEvidence = contextDelta || textDelta;

        if (!hasAnyEvidence)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "Key-interaction postcondition is inconclusive because no post-action state delta is observable.",
                "postcondition_evidence_missing",
                BuildEvidence(semantic, expectedTargetHint, expectedKey, expectedShortcut, preProcess, preTitle, postProcess, postTitle, preSelection, postSelection, preClipboard, postClipboard),
                startedAtUtc));
        }

        var targetMatched = string.IsNullOrWhiteSpace(expectedTargetHint) ||
                            Contains(postProcess, expectedTargetHint) ||
                            Contains(postTitle, expectedTargetHint);

        var verified = semantic.ToLowerInvariant() switch
        {
            "confirm" => targetMatched && (contextDelta || textDelta),
            "cancel" => !targetMatched || contextDelta,
            "presskey" => contextDelta || textDelta,
            "pressshortcut" => contextDelta || textDelta,
            _ => false
        };

        if (!verified)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.NotVerified,
                "Key-interaction postcondition failed: observed state effect does not satisfy action-specific semantic expectations.",
                "postcondition_mismatch",
                BuildEvidence(semantic, expectedTargetHint, expectedKey, expectedShortcut, preProcess, preTitle, postProcess, postTitle, preSelection, postSelection, preClipboard, postClipboard),
                startedAtUtc));
        }

        return Task.FromResult(CreateResult(
            Kind,
            VerificationStatus.Verified,
            "Key-interaction postcondition verified with action-specific semantic evidence.",
            "key_interaction_verified",
            BuildEvidence(semantic, expectedTargetHint, expectedKey, expectedShortcut, preProcess, preTitle, postProcess, postTitle, preSelection, postSelection, preClipboard, postClipboard),
            startedAtUtc));
    }

    private static string ResolveParameter(VerificationPrimitiveSpec specification, string key)
    {
        if (specification.Parameters is null ||
            !specification.Parameters.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static string ResolveMetadata(VerificationRequest request, string key)
    {
        if (request.Metadata is null ||
            !request.Metadata.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value;
    }

    private static bool Contains(string? text, string candidate)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildEvidence(
        string semantic,
        string expectedTargetHint,
        string expectedKey,
        string expectedShortcut,
        string preProcess,
        string preTitle,
        string? postProcess,
        string? postTitle,
        string preSelection,
        string? postSelection,
        string preClipboard,
        string? postClipboard)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["interactionSemantic"] = semantic
        };

        if (!string.IsNullOrWhiteSpace(expectedTargetHint))
        {
            evidence["expectedTargetHint"] = expectedTargetHint;
        }

        if (!string.IsNullOrWhiteSpace(expectedKey))
        {
            evidence["expectedKey"] = expectedKey;
        }

        if (!string.IsNullOrWhiteSpace(expectedShortcut))
        {
            evidence["expectedShortcut"] = expectedShortcut;
        }

        evidence["preActiveProcess"] = preProcess;
        evidence["preActiveWindowTitle"] = preTitle;
        evidence["preSelectionText"] = preSelection;
        evidence["preClipboardText"] = preClipboard;
        evidence["postActiveProcess"] = postProcess ?? string.Empty;
        evidence["postActiveWindowTitle"] = postTitle ?? string.Empty;
        evidence["postSelectionText"] = postSelection ?? string.Empty;
        evidence["postClipboardText"] = postClipboard ?? string.Empty;

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
