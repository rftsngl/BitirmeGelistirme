using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public interface IFileOpenOperator
{
    bool TryOpen(string filePath);
}

public sealed class FileOpenCapability : ICapability
{
    private readonly IFileExistenceInspector _fileExistenceInspector;
    private readonly IFileOpenOperator _fileOpenOperator;

    public FileOpenCapability()
        : this(new FileExistenceInspector(), new ShellFileOpenOperator())
    {
    }

    public FileOpenCapability(
        IFileExistenceInspector fileExistenceInspector,
        IFileOpenOperator fileOpenOperator)
    {
        _fileExistenceInspector = fileExistenceInspector ?? throw new ArgumentNullException(nameof(fileExistenceInspector));
        _fileOpenOperator = fileOpenOperator ?? throw new ArgumentNullException(nameof(fileOpenOperator));
    }

    public string Name => "FileOpenCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.File
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        if (action.Target is null)
        {
            return false;
        }

        if (action.Target.Kind != TargetKind.File)
        {
            return false;
        }

        if (!action.ActionName.Equals("OpenExistingFile", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryExtractFilePath(action.Target, context.PrimaryTargetGrounding, out _);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        if (action.Target is null || !TryExtractFilePath(action.Target, context.PrimaryTargetGrounding, out var filePath))
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "FileOpenCapability blocked: file target is required.",
                "Blocked execution: no valid file path available.",
                null,
                false,
                false,
                startedAtUtc));
        }

        try
        {
            var exists = _fileExistenceInspector.FileExists(filePath);
            if (!exists)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.VerificationFailed,
                    "FileOpenCapability verification failed: file does not exist.",
                    $"File open verification failed: '{filePath}' does not exist.",
                    filePath,
                    false,
                    false,
                    startedAtUtc));
            }

            var opened = _fileOpenOperator.TryOpen(filePath);
            if (opened)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "FileOpenCapability executed open existing file action.",
                    $"Real open attempted for existing file: '{filePath}'.",
                    filePath,
                    true,
                    true,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "FileOpenCapability failed: open action returned no process.",
                $"File open failed: '{filePath}' could not be opened.",
                filePath,
                true,
                false,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "FileOpenCapability failed: open action threw an exception.",
                $"File open failed: '{filePath}' could not be opened.",
                filePath,
                true,
                false,
                startedAtUtc));
        }
    }

    private static bool TryExtractFilePath(
        TargetReference target,
        TargetGroundingResult? primaryTargetGrounding,
        out string filePath)
    {
        filePath = string.Empty;

        if (primaryTargetGrounding is not null &&
            primaryTargetGrounding.Disposition == TargetGroundingDisposition.Resolved &&
            primaryTargetGrounding.ExecutionSuitability == TargetExecutionSuitability.ResolvedButNotExecutableHere &&
            primaryTargetGrounding.Target.Kind == GroundedTargetKind.DocumentLike &&
            !string.IsNullOrWhiteSpace(primaryTargetGrounding.Target.CanonicalValue))
        {
            filePath = NormalizeFilePath(primaryTargetGrounding.Target.CanonicalValue);
            return !string.IsNullOrWhiteSpace(filePath);
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("filePath", out var metadataFilePath) &&
            !string.IsNullOrWhiteSpace(metadataFilePath))
        {
            filePath = NormalizeFilePath(metadataFilePath);
            return !string.IsNullOrWhiteSpace(filePath);
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            filePath = NormalizeFilePath(target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(filePath);
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            filePath = NormalizeFilePath(target.DisplayName);
            return !string.IsNullOrWhiteSpace(filePath);
        }

        return false;
    }

    private static string NormalizeFilePath(string filePath)
    {
        return filePath.Trim().Trim('"', '\'');
    }

    private static ActionExecutionResult CreateResult(
        ExecutionStatus status,
        string message,
        string outputText,
        string? filePath,
        bool fileExists,
        bool fileOpened,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fileExists"] = fileExists ? "true" : "false",
            ["fileOpened"] = fileOpened ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            outputData["filePath"] = filePath;
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = message,
            IsVerified = status == ExecutionStatus.Succeeded,
            OutputText = outputText,
            OutputData = outputData,
            ErrorCode = null,
            UsedFallback = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class FileExistenceInspector : IFileExistenceInspector
    {
        public bool FileExists(string filePath)
        {
            var normalizedPath = NormalizeFilePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            return File.Exists(normalizedPath);
        }
    }

    private sealed class ShellFileOpenOperator : IFileOpenOperator
    {
        public bool TryOpen(string filePath)
        {
            var normalizedPath = NormalizeFilePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = normalizedPath,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            return process is not null;
        }
    }
}
