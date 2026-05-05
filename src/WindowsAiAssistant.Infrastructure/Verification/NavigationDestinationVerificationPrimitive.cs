using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class NavigationDestinationVerificationPrimitive : IVerificationPrimitive
{
    private const string ExpectedDestinationParameter = "expectedDestination";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.NavigationDestination;

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
                "Navigation destination primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var expectedDestination = ResolveExpectedDestination(specification);
        if (string.IsNullOrWhiteSpace(expectedDestination))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "Navigation destination verification is unsupported because expected destination is missing.",
                "expected_destination_missing",
                null,
                startedAtUtc));
        }

        var observedTitle = request.ExecutionContext?.Observation?.ActiveWindow?.Title
                            ?? request.ExecutionContext?.RichObservation?.ForegroundWindow?.Title;
        var observedProcess = request.ExecutionContext?.Observation?.ActiveProcessName
                              ?? request.ExecutionContext?.Observation?.ActiveWindow?.ProcessName
                              ?? request.ExecutionContext?.RichObservation?.ForegroundProcess?.Name;

        var titleMatched = !string.IsNullOrWhiteSpace(observedTitle) &&
                           observedTitle.Contains(expectedDestination, StringComparison.OrdinalIgnoreCase);
        var processMatched = !string.IsNullOrWhiteSpace(observedProcess) &&
                             observedProcess.Contains(expectedDestination, StringComparison.OrdinalIgnoreCase);

        if (titleMatched || processMatched)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Verified,
                "Navigation destination was verified from post-action foreground context.",
                "navigation_destination_verified",
                BuildEvidence(expectedDestination, observedTitle, observedProcess),
                startedAtUtc));
        }

        if (string.IsNullOrWhiteSpace(observedTitle) && string.IsNullOrWhiteSpace(observedProcess))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "Navigation destination verification is inconclusive because foreground context evidence is unavailable.",
                "navigation_evidence_missing",
                BuildEvidence(expectedDestination, observedTitle, observedProcess),
                startedAtUtc));
        }

        return Task.FromResult(CreateResult(
            Kind,
            VerificationStatus.NotVerified,
            "Navigation destination verification failed because foreground context does not match expected destination.",
            "navigation_destination_mismatch",
            BuildEvidence(expectedDestination, observedTitle, observedProcess),
            startedAtUtc));
    }

    private static string ResolveExpectedDestination(VerificationPrimitiveSpec specification)
    {
        if (specification.Parameters is null ||
            !specification.Parameters.TryGetValue(ExpectedDestinationParameter, out var expectedDestination) ||
            string.IsNullOrWhiteSpace(expectedDestination))
        {
            return string.Empty;
        }

        return expectedDestination.Trim();
    }

    private static Dictionary<string, string> BuildEvidence(
        string expectedDestination,
        string? observedTitle,
        string? observedProcess)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expectedDestination"] = expectedDestination
        };

        if (!string.IsNullOrWhiteSpace(observedTitle))
        {
            evidence["observedTitle"] = observedTitle;
        }

        if (!string.IsNullOrWhiteSpace(observedProcess))
        {
            evidence["observedProcess"] = observedProcess;
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
