using System.Text.Json;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

public sealed class FileCommandHistoryService : ICommandHistoryService
{
    private readonly string _filePath;

    public FileCommandHistoryService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "logs",
            "audit.jsonl"))
    {
    }

    public FileCommandHistoryService(string filePath)
    {
        _filePath = filePath;
    }

    public Task<IReadOnlyList<RecentCommandSummary>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        try
        {
            if (maxCount <= 0 || !File.Exists(_filePath))
            {
                return Task.FromResult<IReadOnlyList<RecentCommandSummary>>([]);
            }

            var parsed = new List<AuditEvent>();
            foreach (var line in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var evt = JsonSerializer.Deserialize<AuditEvent>(line);
                    if (evt is null ||
                        string.IsNullOrWhiteSpace(evt.CorrelationId) ||
                        string.IsNullOrWhiteSpace(evt.EventType))
                    {
                        continue;
                    }

                    parsed.Add(evt);
                }
                catch
                {
                    // Skip malformed lines safely.
                }
            }

            var summaries = parsed
                .GroupBy(e => e.CorrelationId)
                .Select(group =>
                {
                    var ordered = group.OrderBy(e => e.TimestampUtc).ToList();
                    var first = ordered.First();
                    var completed = ordered.LastOrDefault(e => e.EventType == "CommandCompleted") ?? ordered.Last();
                    var runtimeTerminated = ordered.LastOrDefault(e => e.EventType == "RuntimeTerminated");
                    var toolExecution = ordered.LastOrDefault(e => e.EventType == "ToolExecutionCompleted");
                    var stepExecuted = ordered.LastOrDefault(e => e.EventType == "StepExecuted");
                    var stepVerified = ordered.LastOrDefault(e => e.EventType == "StepVerified");
                    var approvalDecisionEvent = ordered.LastOrDefault(e => e.EventType == "ApprovalDecision");
                    var terminalEvent = runtimeTerminated ?? completed;
                    var haltReason = FirstNonEmpty(
                    [
                        GetMetadataValue(terminalEvent, "haltReason"),
                        GetMetadataValue(completed, "haltReason"),
                        GetMetadataValue(approvalDecisionEvent, "haltReason")
                    ], string.Empty);
                    var stepExecutionState = BuildStepExecutionState(stepExecuted, completed, ordered);
                    var stepVerificationState = BuildStepVerificationState(stepVerified);
                    var runtimeClosureState = BuildRuntimeClosureState(terminalEvent, completed, approvalDecisionEvent);
                    var runtimeTerminalState = BuildRuntimeTerminalState(terminalEvent);
                    var runtimeTerminationReason = BuildRuntimeTerminationReason(terminalEvent, haltReason);
                    var summaryMessage = BuildSummaryMessage(
                        terminalEvent.Message,
                        haltReason,
                        approvalDecisionEvent?.ApprovalDecision,
                        stepExecutionState,
                        stepVerificationState,
                        runtimeClosureState,
                        runtimeTerminationReason);

                    return new RecentCommandSummary
                    {
                        TimestampUtc = first.TimestampUtc,
                        CommandText = FirstNonEmpty(ordered.Select(e => e.CommandText), "(unknown command)"),
                        FinalStatus = FirstNonEmpty([completed.Outcome, completed.EventType], "Unknown"),
                        SafetyDisposition = FirstNonEmpty([completed.SafetyDisposition], "Unknown"),
                        RiskLevel = FirstNonEmpty([completed.RiskLevel], "Unknown"),
                        SelectedTool = FirstNonEmpty([completed.SelectedTool], "None"),
                        ExecutionMode = FirstNonEmpty([toolExecution?.ExecutionMode ?? string.Empty, completed.ExecutionMode], "not-executed"),
                        ApprovalDecision = FirstNonEmpty(
                        [
                            approvalDecisionEvent?.ApprovalDecision ?? string.Empty,
                            completed.ApprovalDecision
                        ], "-"),
                        StepExecutionState = stepExecutionState,
                        StepVerificationState = stepVerificationState,
                        RuntimeClosureState = runtimeClosureState,
                        RuntimeTerminalState = runtimeTerminalState,
                        RuntimeTerminationReason = runtimeTerminationReason,
                        Outcome = FirstNonEmpty([terminalEvent.Outcome, completed.Outcome], "Unknown"),
                        Message = FirstNonEmpty([summaryMessage], "-")
                    };
                })
                .OrderByDescending(x => x.TimestampUtc)
                .Take(maxCount)
                .ToList();

            return Task.FromResult<IReadOnlyList<RecentCommandSummary>>(summaries);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<RecentCommandSummary>>([]);
        }
    }

    private static string FirstNonEmpty(IEnumerable<string> values, string fallback)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static string GetMetadataValue(AuditEvent? auditEvent, string key)
    {
        if (auditEvent?.Metadata is null)
        {
            return string.Empty;
        }

        return auditEvent.Metadata.TryGetValue(key, out var value)
            ? value
            : string.Empty;
    }

    private static string BuildSummaryMessage(
        string completedMessage,
        string haltReason,
        string? approvalDecision,
        string stepExecutionState,
        string stepVerificationState,
        string runtimeClosureState,
        string runtimeTerminationReason)
    {
        if (string.Equals(haltReason, "approval_rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "Execution stopped because approval was rejected.";
        }

        if (string.Equals(haltReason, "approval_resume_failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Execution stopped because approved step could not be resumed.";
        }

        if (string.Equals(approvalDecision, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "Execution stopped because approval was rejected.";
        }

        if (stepExecutionState.StartsWith("decision-only", StringComparison.OrdinalIgnoreCase))
        {
            return "Decision recorded but no step execution was observed.";
        }

        if (stepVerificationState == "verified-failure")
        {
            return "Step executed but verification reported failure.";
        }

        if (stepVerificationState == "verified-inconclusive")
        {
            return "Step executed but verification remained inconclusive.";
        }

        if (runtimeClosureState == "rejected")
        {
            return "Execution ended as rejected.";
        }

        if (runtimeClosureState == "blocked" && runtimeTerminationReason == "verification_failed_stop")
        {
            return "Runtime stopped because verification failed.";
        }

        return completedMessage;
    }

    private static string BuildStepExecutionState(
        AuditEvent? stepExecuted,
        AuditEvent completed,
        IReadOnlyList<AuditEvent> orderedEvents)
    {
        if (stepExecuted is not null)
        {
            var mode = FirstNonEmpty([stepExecuted.ExecutionMode], "unknown");
            return $"executed:{mode}";
        }

        var executionMode = FirstNonEmpty([completed.ExecutionMode], "not-executed");
        var hasStepSubmission = orderedEvents.Any(e => e.EventType == "StepSubmitted");
        if (hasStepSubmission && string.Equals(executionMode, "not-executed", StringComparison.OrdinalIgnoreCase))
        {
            return "decision-only:not-executed";
        }

        return string.Equals(executionMode, "not-executed", StringComparison.OrdinalIgnoreCase)
            ? "not-executed"
            : $"executed:{executionMode}";
    }

    private static string BuildStepVerificationState(AuditEvent? stepVerified)
    {
        if (stepVerified is null)
        {
            return "not-verified";
        }

        return stepVerified.Outcome switch
        {
            "VerifiedSuccess" => "verified-success",
            "VerifiedFailure" => "verified-failure",
            "Inconclusive" => "verified-inconclusive",
            _ => "verified-unknown"
        };
    }

    private static string BuildRuntimeClosureState(
        AuditEvent terminalEvent,
        AuditEvent completedEvent,
        AuditEvent? approvalDecisionEvent)
    {
        var terminalState = GetMetadataValue(terminalEvent, "terminal_state");
        if (!string.IsNullOrWhiteSpace(terminalState))
        {
            return terminalState;
        }

        if (string.Equals(approvalDecisionEvent?.ApprovalDecision, "Rejected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completedEvent.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "rejected";
        }

        return GetMetadataValue(terminalEvent, "runtimeTerminalState") switch
        {
            "Completed" => "completed",
            "Aborted" => "aborted",
            _ => string.Equals(completedEvent.Outcome, "Accepted", StringComparison.OrdinalIgnoreCase)
                ? "completed"
                : "blocked"
        };
    }

    private static string BuildRuntimeTerminalState(AuditEvent terminalEvent)
    {
        return FirstNonEmpty(
        [
            GetMetadataValue(terminalEvent, "runtimeTerminalState"),
            GetMetadataValue(terminalEvent, "terminal_state")
        ], "Unknown");
    }

    private static string BuildRuntimeTerminationReason(AuditEvent terminalEvent, string haltReason)
    {
        if (!string.IsNullOrWhiteSpace(haltReason))
        {
            return haltReason;
        }

        return FirstNonEmpty(
        [
            GetMetadataValue(terminalEvent, "terminal_reason_code"),
            GetMetadataValue(terminalEvent, "runtimeTerminationReason")
        ], "Unknown");
    }
}
