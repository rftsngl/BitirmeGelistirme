using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Core;

public sealed class ExecutionVerifier : IExecutionVerifier
{
    private readonly IReadOnlyDictionary<VerificationPrimitiveKind, IVerificationPrimitive> _primitivesByKind;

    public ExecutionVerifier(IEnumerable<IVerificationPrimitive> primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);

        _primitivesByKind = primitives
            .GroupBy(primitive => primitive.Kind)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public async Task<VerificationResult> VerifyAsync(
        VerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAtUtc = DateTimeOffset.UtcNow;
        if (request.Specifications.Count == 0)
        {
            return CreateFinalResult(
                VerificationStatus.Unsupported,
                "Verification request is unsupported because no primitive specification was provided.",
                [],
                startedAtUtc);
        }

        var evaluations = new List<(VerificationPrimitiveSpec Spec, VerificationPrimitiveResult Result)>(request.Specifications.Count);
        foreach (var specification in request.Specifications)
        {
            if (!_primitivesByKind.TryGetValue(specification.Kind, out var primitive))
            {
                evaluations.Add((
                    specification,
                    CreatePrimitiveResult(
                        specification.Kind,
                        VerificationStatus.Unsupported,
                        $"No verification primitive is registered for '{specification.Kind}'.",
                        "primitive_not_registered",
                        null,
                        startedAtUtc)));

                continue;
            }

            try
            {
                var primitiveResult = await primitive.EvaluateAsync(specification, request, cancellationToken);
                evaluations.Add((specification, primitiveResult));
            }
            catch (Exception ex)
            {
                evaluations.Add((
                    specification,
                    CreatePrimitiveResult(
                        specification.Kind,
                        VerificationStatus.Inconclusive,
                        $"Verification primitive '{specification.Kind}' failed during evaluation.",
                        "primitive_exception",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["exceptionType"] = ex.GetType().Name,
                            ["exceptionMessage"] = ex.Message
                        },
                        startedAtUtc)));
            }
        }

        var effectiveEvaluations = GetEffectiveEvaluations(evaluations);
        var finalStatus = DetermineStatus(effectiveEvaluations);
        var reason = BuildFinalReason(finalStatus, effectiveEvaluations);
        return CreateFinalResult(
            finalStatus,
            reason,
            evaluations.Select(item => item.Result).ToList(),
            startedAtUtc);
    }

    private static IReadOnlyList<(VerificationPrimitiveSpec Spec, VerificationPrimitiveResult Result)> GetEffectiveEvaluations(
        IReadOnlyList<(VerificationPrimitiveSpec Spec, VerificationPrimitiveResult Result)> evaluations)
    {
        var required = evaluations.Where(item => item.Spec.Required).ToList();
        return required.Count > 0 ? required : evaluations;
    }

    private static VerificationStatus DetermineStatus(
        IReadOnlyList<(VerificationPrimitiveSpec Spec, VerificationPrimitiveResult Result)> evaluations)
    {
        if (evaluations.Any(item => item.Result.Status == VerificationStatus.NotVerified))
        {
            return VerificationStatus.NotVerified;
        }

        if (evaluations.Any(item => item.Result.Status == VerificationStatus.Inconclusive))
        {
            return VerificationStatus.Inconclusive;
        }

        if (evaluations.Any(item => item.Result.Status == VerificationStatus.Unsupported))
        {
            return VerificationStatus.Unsupported;
        }

        return VerificationStatus.Verified;
    }

    private static string BuildFinalReason(
        VerificationStatus status,
        IReadOnlyList<(VerificationPrimitiveSpec Spec, VerificationPrimitiveResult Result)> evaluations)
    {
        return status switch
        {
            VerificationStatus.Verified => "All required verification primitives were verified.",
            VerificationStatus.NotVerified =>
                $"At least one required verification primitive returned '{VerificationStatus.NotVerified}'.",
            VerificationStatus.Inconclusive =>
                $"At least one required verification primitive returned '{VerificationStatus.Inconclusive}'.",
            VerificationStatus.Unsupported =>
                $"At least one required verification primitive returned '{VerificationStatus.Unsupported}'.",
            _ => $"Verification ended with status '{status}'."
        };
    }

    private static VerificationResult CreateFinalResult(
        VerificationStatus status,
        string reason,
        IReadOnlyList<VerificationPrimitiveResult> primitiveResults,
        DateTimeOffset startedAtUtc)
    {
        return new VerificationResult
        {
            Status = status,
            Reason = reason,
            PrimitiveResults = primitiveResults,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static VerificationPrimitiveResult CreatePrimitiveResult(
        VerificationPrimitiveKind primitiveKind,
        VerificationStatus status,
        string reason,
        string evidenceCode,
        IDictionary<string, string>? evidenceData,
        DateTimeOffset startedAtUtc)
    {
        return new VerificationPrimitiveResult
        {
            PrimitiveKind = primitiveKind,
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