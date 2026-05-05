using System.Diagnostics;

namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class ShellExecutablePathLaunchStrategy : ILaunchStrategy
{
    private readonly HashSet<string> _allowedExecutablePaths;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    public ShellExecutablePathLaunchStrategy(ExecutionPolicySettings settings)
        : this(settings, Process.Start)
    {
    }

    public ShellExecutablePathLaunchStrategy(
        ExecutionPolicySettings settings,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _allowedExecutablePaths = (settings.AllowedExecutablePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _processStarter = processStarter ?? Process.Start;
    }

    public string Name => "shell-executable-path";

    public bool CanHandle(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Target.Kind == LaunchTargetKind.ExecutablePath;
    }

    public Task<LaunchExecution> ExecuteAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rawPath = request.Target.OriginalReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.EmptyTarget));
        }

        if (request.Target.Kind != LaunchTargetKind.ExecutablePath)
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.UnsupportedTargetKind));
        }

        if (_allowedExecutablePaths.Count == 0)
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.AllowListMissing));
        }

        var normalizedPath = NormalizePath(request.Target.NormalizedReference);
        if (!_allowedExecutablePaths.Contains(normalizedPath))
        {
            return Task.FromResult(CreateFailure(
                CreateNormalizedRequest(request, normalizedPath),
                LaunchFailureReason.NotAllowlisted));
        }

        try
        {
            var process = _processStarter(new ProcessStartInfo
            {
                FileName = normalizedPath,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return Task.FromResult(CreateFailure(
                    CreateNormalizedRequest(request, normalizedPath),
                    LaunchFailureReason.LaunchReturnedNoProcess));
            }

            return Task.FromResult(new LaunchExecution
            {
                Success = true,
                StrategyName = Name,
                Request = CreateNormalizedRequest(request, normalizedPath),
                FailureReason = LaunchFailureReason.None,
                ExceptionMessage = null
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateFailure(
                CreateNormalizedRequest(request, normalizedPath),
                LaunchFailureReason.LaunchException,
                ex.Message));
        }
    }

    private static LaunchRequest CreateNormalizedRequest(LaunchRequest request, string normalizedPath)
    {
        return new LaunchRequest
        {
            TargetReference = request.TargetReference,
            Target = new LaunchTarget
            {
                Kind = request.Target.Kind,
                OriginalReference = request.Target.OriginalReference,
                NormalizedReference = normalizedPath
            }
        };
    }

    private static LaunchExecution CreateFailure(
        LaunchRequest request,
        LaunchFailureReason reason,
        string? exceptionMessage = null)
    {
        return new LaunchExecution
        {
            Success = false,
            StrategyName = "shell-executable-path",
            Request = request,
            FailureReason = reason,
            ExceptionMessage = exceptionMessage
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"', '\'');

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}
