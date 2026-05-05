using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class ProcessPresenceVerificationPrimitive : IVerificationPrimitive
{
    private const string ExpectedProcessNameParameter = "expectedProcessName";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.ProcessPresence;

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
                "Process presence primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var expectedProcessName = ResolveExpectedProcessName(specification, request);
        if (string.IsNullOrWhiteSpace(expectedProcessName))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "Process presence verification is unsupported because expected process name is missing.",
                "expected_process_name_missing",
                null,
                startedAtUtc));
        }

        var observedProcessNames = CollectObservedProcessNames(request);
        if (observedProcessNames.Count == 0)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "Process presence verification is inconclusive because post-action process observation is unavailable.",
                "post_action_observation_missing",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["expectedProcessName"] = expectedProcessName
                },
                startedAtUtc));
        }

        if (observedProcessNames.Contains(expectedProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Verified,
                "Expected process presence was verified from post-action evidence.",
                "process_presence_verified",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["expectedProcessName"] = expectedProcessName,
                    ["observedProcessNames"] = string.Join(",", observedProcessNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                },
                startedAtUtc));
        }

        return Task.FromResult(CreateResult(
            Kind,
            VerificationStatus.NotVerified,
            "Expected process presence could not be verified from post-action evidence.",
            "process_presence_not_verified",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expectedProcessName"] = expectedProcessName,
                ["observedProcessNames"] = string.Join(",", observedProcessNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            },
            startedAtUtc));
    }

    private static string? ResolveExpectedProcessName(
        VerificationPrimitiveSpec specification,
        VerificationRequest request)
    {
        if (specification.Parameters is not null &&
            specification.Parameters.TryGetValue(ExpectedProcessNameParameter, out var expectedFromParameters) &&
            !string.IsNullOrWhiteSpace(expectedFromParameters))
        {
            return NormalizeProcessName(expectedFromParameters);
        }

        var target = request.Action?.Target;
        if (target is null)
        {
            return null;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var expectedFromTargetMetadata) &&
            !string.IsNullOrWhiteSpace(expectedFromTargetMetadata))
        {
            return NormalizeProcessName(expectedFromTargetMetadata);
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue) &&
            !long.TryParse(target.NormalizedValue, out _))
        {
            return NormalizeProcessName(target.NormalizedValue);
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            return NormalizeProcessName(target.DisplayName);
        }

        return null;
    }

    private static HashSet<string> CollectObservedProcessNames(VerificationRequest request)
    {
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddObserved(observed, request.ExecutionContext?.Observation?.ActiveProcessName);
        TryAddObserved(observed, request.ExecutionContext?.RichObservation?.ForegroundProcess?.Name);

        var outputData = request.ExecutionResult?.OutputData;
        if (outputData is not null &&
            outputData.TryGetValue("isRunning", out var runningText) &&
            bool.TryParse(runningText, out var isRunning) &&
            isRunning &&
            outputData.TryGetValue("processName", out var outputProcessName))
        {
            TryAddObserved(observed, outputProcessName);
        }

        return observed;
    }

    private static void TryAddObserved(ISet<string> observed, string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            observed.Add(normalized);
        }
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
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