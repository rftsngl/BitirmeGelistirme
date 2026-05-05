using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IExecutionVerifier
{
    Task<VerificationResult> VerifyAsync(
        VerificationRequest request,
        CancellationToken cancellationToken = default);
}
