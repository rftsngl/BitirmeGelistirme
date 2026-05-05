using System.Diagnostics;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class ShellApplicationLaunchStrategy : ILaunchStrategy
{
    private readonly HashSet<string> _allowedRealApps;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    public ShellApplicationLaunchStrategy(ExecutionPolicySettings settings)
        : this(settings, Process.Start)
    {
    }

    public ShellApplicationLaunchStrategy(ExecutionPolicySettings settings, Func<ProcessStartInfo, Process?> processStarter)
    {
        _allowedRealApps = (settings.AllowedRealApps ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _processStarter = processStarter ?? Process.Start;
    }

    public string Name => "shell";

    public bool CanHandle(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Target.Kind == LaunchTargetKind.RegisteredApplication;
    }

    public Task<LaunchExecution> ExecuteAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var originalTarget = request.Target.OriginalReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(originalTarget))
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.EmptyTarget));
        }

        if (request.Target.Kind != LaunchTargetKind.RegisteredApplication)
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.UnsupportedTargetKind));
        }

        var normalizedTarget = request.Target.NormalizedReference.Trim().ToLowerInvariant();

        if (_allowedRealApps.Count == 0)
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.AllowListMissing));
        }

        if (!_allowedRealApps.Contains(normalizedTarget))
        {
            return Task.FromResult(CreateFailure(request, LaunchFailureReason.NotAllowlisted));
        }

        var executable = $"{normalizedTarget}.exe";

        try
        {
            var process = _processStarter(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return Task.FromResult(CreateFailure(request, LaunchFailureReason.LaunchReturnedNoProcess));
            }

            return Task.FromResult(new LaunchExecution
            {
                Success = true,
                StrategyName = Name,
                Request = CreateNormalizedRequest(request, normalizedTarget),
                FailureReason = LaunchFailureReason.None,
                ExceptionMessage = null
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateFailure(
                CreateNormalizedRequest(request, normalizedTarget),
                LaunchFailureReason.LaunchException,
                ex.Message));
        }
    }

    private static LaunchRequest CreateNormalizedRequest(LaunchRequest request, string normalizedTarget)
    {
        return new LaunchRequest
        {
            TargetReference = request.TargetReference,
            Target = new LaunchTarget
            {
                Kind = request.Target.Kind,
                OriginalReference = request.Target.OriginalReference,
                NormalizedReference = normalizedTarget
            }
        };
    }

    private LaunchExecution CreateFailure(
        LaunchRequest request,
        LaunchFailureReason reason,
        string? exceptionMessage = null)
    {
        return new LaunchExecution
        {
            Success = false,
            StrategyName = Name,
            Request = request,
            FailureReason = reason,
            ExceptionMessage = exceptionMessage
        };
    }
}
