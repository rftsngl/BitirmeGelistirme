using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

public sealed class BasicSafetyGate : ISafetyGate
{
    private readonly IReadOnlyList<string> _deniedPatterns;
    private readonly IReadOnlyList<string> _requiresApprovalTargets;

    public BasicSafetyGate(ExecutionPolicySettings settings)
    {
        _deniedPatterns = (settings.DeniedPatterns ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();

        _requiresApprovalTargets = (settings.RequiresApprovalTargets ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();
    }

    public Task<SafetyDecision> EvaluateAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var input = (request.UserInput ?? string.Empty).Trim();
        var normalized = input.ToLowerInvariant();

        if (_deniedPatterns.Count == 0 || _requiresApprovalTargets.Count == 0)
        {
            return Task.FromResult(new SafetyDecision
            {
                Disposition = SafetyDisposition.Denied,
                RiskLevel = SafetyRiskLevel.High,
                Reason = "Blocked by policy: configuration is missing or invalid (fail-closed)."
            });
        }

        if (_deniedPatterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal)))
        {
            return Task.FromResult(new SafetyDecision
            {
                Disposition = SafetyDisposition.Denied,
                RiskLevel = SafetyRiskLevel.High,
                Reason = "Blocked by policy: destructive or critical operation detected."
            });
        }

        return Task.FromResult(new SafetyDecision
        {
            Disposition = SafetyDisposition.Allowed,
            RiskLevel = SafetyRiskLevel.Low,
            Reason = string.IsNullOrWhiteSpace(input) ? "No command content." : "Allowed by command precheck policy."
        });
    }
}
