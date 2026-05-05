using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IVerificationPrimitive
{
    VerificationPrimitiveKind Kind { get; }

    Task<VerificationPrimitiveResult> EvaluateAsync(
        VerificationPrimitiveSpec specification,
        VerificationRequest request,
        CancellationToken cancellationToken = default);
}