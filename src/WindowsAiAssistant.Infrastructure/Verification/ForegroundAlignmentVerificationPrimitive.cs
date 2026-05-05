using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Verification;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class ForegroundAlignmentVerificationPrimitive : IVerificationPrimitive
{
    private const string ExpectedProcessNameParameter = "expectedProcessName";
    private const string ExpectedWindowHandleParameter = "expectedWindowHandle";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.ForegroundAlignment;

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
                "Foreground alignment primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var expected = ResolveExpectedAlignment(specification, request.Action);
        if (!expected.HasExpectedWindowHandle && string.IsNullOrWhiteSpace(expected.ExpectedProcessName))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "Foreground alignment verification is unsupported because no comparable target signal was provided.",
                "foreground_target_missing",
                null,
                startedAtUtc));
        }

        var observed = ResolveObservedAlignment(request.ExecutionContext);
        if (expected.HasExpectedWindowHandle)
        {
            if (!observed.HasObservedWindowHandle)
            {
                return Task.FromResult(CreateResult(
                    Kind,
                    VerificationStatus.Inconclusive,
                    "Foreground alignment verification is inconclusive because foreground window handle observation is unavailable.",
                    "foreground_window_handle_unavailable",
                    BuildEvidenceData(expected, observed),
                    startedAtUtc));
            }

            var matched = observed.ObservedWindowHandle == expected.ExpectedWindowHandle;
            return Task.FromResult(CreateResult(
                Kind,
                matched ? VerificationStatus.Verified : VerificationStatus.NotVerified,
                matched
                    ? "Foreground alignment was verified by matching window handles."
                    : "Foreground alignment failed because window handles do not match.",
                matched ? "foreground_alignment_verified" : "foreground_alignment_not_verified",
                BuildEvidenceData(expected, observed),
                startedAtUtc));
        }

        if (!observed.HasObservedProcessName)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "Foreground alignment verification is inconclusive because foreground process observation is unavailable.",
                "foreground_process_unavailable",
                BuildEvidenceData(expected, observed),
                startedAtUtc));
        }

        var processMatched = observed.ObservedProcessName!.Equals(expected.ExpectedProcessName, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(CreateResult(
            Kind,
            processMatched ? VerificationStatus.Verified : VerificationStatus.NotVerified,
            processMatched
                ? "Foreground alignment was verified by matching process names."
                : "Foreground alignment failed because process names do not match.",
            processMatched ? "foreground_alignment_verified" : "foreground_alignment_not_verified",
            BuildEvidenceData(expected, observed),
            startedAtUtc));
    }

    private static ExpectedAlignment ResolveExpectedAlignment(
        VerificationPrimitiveSpec specification,
        AgentAction? action)
    {
        var expectedProcessName = string.Empty;
        var expectedWindowHandle = 0L;
        var hasExpectedWindowHandle = false;

        if (specification.Parameters is not null)
        {
            if (specification.Parameters.TryGetValue(ExpectedProcessNameParameter, out var processParameter) &&
                !string.IsNullOrWhiteSpace(processParameter))
            {
                expectedProcessName = NormalizeProcessName(processParameter);
            }

            if (specification.Parameters.TryGetValue(ExpectedWindowHandleParameter, out var windowHandleParameter) &&
                long.TryParse(windowHandleParameter, out var parsedWindowHandle) &&
                parsedWindowHandle > 0)
            {
                expectedWindowHandle = parsedWindowHandle;
                hasExpectedWindowHandle = true;
            }
        }

        if (action?.Target is null)
        {
            return new ExpectedAlignment
            {
                ExpectedProcessName = expectedProcessName,
                ExpectedWindowHandle = expectedWindowHandle,
                HasExpectedWindowHandle = hasExpectedWindowHandle
            };
        }

        if (!hasExpectedWindowHandle &&
            TryResolveTargetWindowHandle(action.Target, out var targetWindowHandle))
        {
            expectedWindowHandle = targetWindowHandle;
            hasExpectedWindowHandle = true;
        }

        if (string.IsNullOrWhiteSpace(expectedProcessName))
        {
            expectedProcessName = ResolveTargetProcessName(action.Target);
        }

        return new ExpectedAlignment
        {
            ExpectedProcessName = expectedProcessName,
            ExpectedWindowHandle = expectedWindowHandle,
            HasExpectedWindowHandle = hasExpectedWindowHandle
        };
    }

    private static ObservedAlignment ResolveObservedAlignment(AgentExecutionContext? context)
    {
        var observedWindowHandle = 0L;
        var hasObservedWindowHandle = false;

        var observedProcessName = string.Empty;
        var hasObservedProcessName = false;

        var observationWindowHandle = context?.Observation?.ActiveWindow?.Handle;
        if (observationWindowHandle is long activeWindowHandle && activeWindowHandle > 0)
        {
            observedWindowHandle = activeWindowHandle;
            hasObservedWindowHandle = true;
        }
        else if (context?.RichObservation?.ForegroundWindow?.Handle is long foregroundWindowHandle &&
                 foregroundWindowHandle > 0)
        {
            observedWindowHandle = foregroundWindowHandle;
            hasObservedWindowHandle = true;
        }

        if (!string.IsNullOrWhiteSpace(context?.Observation?.ActiveProcessName))
        {
            observedProcessName = NormalizeProcessName(context.Observation.ActiveProcessName);
            hasObservedProcessName = !string.IsNullOrWhiteSpace(observedProcessName);
        }
        else if (!string.IsNullOrWhiteSpace(context?.Observation?.ActiveWindow?.ProcessName))
        {
            observedProcessName = NormalizeProcessName(context.Observation.ActiveWindow.ProcessName);
            hasObservedProcessName = !string.IsNullOrWhiteSpace(observedProcessName);
        }
        else if (!string.IsNullOrWhiteSpace(context?.RichObservation?.ForegroundProcess?.Name))
        {
            observedProcessName = NormalizeProcessName(context.RichObservation.ForegroundProcess.Name);
            hasObservedProcessName = !string.IsNullOrWhiteSpace(observedProcessName);
        }

        return new ObservedAlignment
        {
            ObservedProcessName = observedProcessName,
            HasObservedProcessName = hasObservedProcessName,
            ObservedWindowHandle = observedWindowHandle,
            HasObservedWindowHandle = hasObservedWindowHandle
        };
    }

    private static bool TryResolveTargetWindowHandle(TargetReference target, out long windowHandle)
    {
        windowHandle = 0;

        if (long.TryParse(target.NormalizedValue, out var parsedNormalized) && parsedNormalized > 0)
        {
            windowHandle = parsedNormalized;
            return true;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("windowHandle", out var metadataWindowHandle) &&
            long.TryParse(metadataWindowHandle, out var parsedMetadata) &&
            parsedMetadata > 0)
        {
            windowHandle = parsedMetadata;
            return true;
        }

        return false;
    }

    private static string ResolveTargetProcessName(TargetReference target)
    {
        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var processFromMetadata) &&
            !string.IsNullOrWhiteSpace(processFromMetadata))
        {
            return NormalizeProcessName(processFromMetadata);
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

        return string.Empty;
    }

    private static IDictionary<string, string> BuildEvidenceData(
        ExpectedAlignment expected,
        ObservedAlignment observed)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(expected.ExpectedProcessName))
        {
            data["expectedProcessName"] = expected.ExpectedProcessName;
        }

        if (expected.HasExpectedWindowHandle)
        {
            data["expectedWindowHandle"] = expected.ExpectedWindowHandle.ToString();
        }

        if (!string.IsNullOrWhiteSpace(observed.ObservedProcessName))
        {
            data["observedProcessName"] = observed.ObservedProcessName;
        }

        if (observed.HasObservedWindowHandle)
        {
            data["observedWindowHandle"] = observed.ObservedWindowHandle.ToString();
        }

        return data;
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

    private sealed class ExpectedAlignment
    {
        public string ExpectedProcessName { get; init; } = string.Empty;
        public long ExpectedWindowHandle { get; init; }
        public bool HasExpectedWindowHandle { get; init; }
    }

    private sealed class ObservedAlignment
    {
        public string ObservedProcessName { get; init; } = string.Empty;
        public bool HasObservedProcessName { get; init; }
        public long ObservedWindowHandle { get; init; }
        public bool HasObservedWindowHandle { get; init; }
    }
}
