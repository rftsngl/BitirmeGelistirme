using System.IO;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Infrastructure.Verification;

public sealed class FileOpenPostconditionVerificationPrimitive : IVerificationPrimitive
{
    private const string ExpectedFilePathParameter = "expectedFilePath";

    public VerificationPrimitiveKind Kind => VerificationPrimitiveKind.FileOpenPostcondition;

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
                "File-open postcondition primitive does not support the requested verification kind.",
                "unsupported_verification_kind",
                null,
                startedAtUtc));
        }

        var expectedFilePath = ResolveExpectedFilePath(specification);
        if (string.IsNullOrWhiteSpace(expectedFilePath))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Unsupported,
                "File-open postcondition verification is unsupported because expected file path is missing.",
                "expected_file_path_missing",
                null,
                startedAtUtc));
        }

        var outputData = request.ExecutionResult?.OutputData;
        var hasFileOpenedSignal = outputData is not null &&
                                  outputData.TryGetValue("fileOpened", out var fileOpenedText) &&
                                  bool.TryParse(fileOpenedText, out var fileOpened) &&
                                  fileOpened;

        var observedTitle = request.ExecutionContext?.Observation?.ActiveWindow?.Title
                            ?? request.ExecutionContext?.RichObservation?.ForegroundWindow?.Title;
        var observedProcess = request.ExecutionContext?.Observation?.ActiveProcessName
                              ?? request.ExecutionContext?.Observation?.ActiveWindow?.ProcessName
                              ?? request.ExecutionContext?.RichObservation?.ForegroundProcess?.Name;
        var expectedFileName = Path.GetFileName(expectedFilePath);

        var titleAligned = !string.IsNullOrWhiteSpace(observedTitle) &&
                           !string.IsNullOrWhiteSpace(expectedFileName) &&
                           observedTitle.Contains(expectedFileName, StringComparison.OrdinalIgnoreCase);

        if (!hasFileOpenedSignal)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.NotVerified,
                "File-open postcondition was not verified because execution output does not confirm that the file was opened.",
                "file_open_signal_missing",
                BuildEvidence(expectedFilePath, observedTitle, observedProcess, hasFileOpenedSignal),
                startedAtUtc));
        }

        if (titleAligned)
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Verified,
                "File-open postcondition was verified using open-signal and foreground title alignment.",
                "file_open_verified",
                BuildEvidence(expectedFilePath, observedTitle, observedProcess, hasFileOpenedSignal),
                startedAtUtc));
        }

        if (string.IsNullOrWhiteSpace(observedTitle))
        {
            return Task.FromResult(CreateResult(
                Kind,
                VerificationStatus.Inconclusive,
                "File-open postcondition is inconclusive because foreground title evidence is unavailable.",
                "foreground_title_unavailable",
                BuildEvidence(expectedFilePath, observedTitle, observedProcess, hasFileOpenedSignal),
                startedAtUtc));
        }

        return Task.FromResult(CreateResult(
            Kind,
            VerificationStatus.NotVerified,
            "File-open postcondition failed because foreground title does not match expected file target.",
            "foreground_title_mismatch",
            BuildEvidence(expectedFilePath, observedTitle, observedProcess, hasFileOpenedSignal),
            startedAtUtc));
    }

    private static string ResolveExpectedFilePath(VerificationPrimitiveSpec specification)
    {
        if (specification.Parameters is null ||
            !specification.Parameters.TryGetValue(ExpectedFilePathParameter, out var expectedFilePath) ||
            string.IsNullOrWhiteSpace(expectedFilePath))
        {
            return string.Empty;
        }

        return expectedFilePath.Trim().Trim('"', '\'');
    }

    private static Dictionary<string, string> BuildEvidence(
        string expectedFilePath,
        string? observedTitle,
        string? observedProcess,
        bool hasFileOpenedSignal)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expectedFilePath"] = expectedFilePath,
            ["fileOpenedSignal"] = hasFileOpenedSignal ? "true" : "false"
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
