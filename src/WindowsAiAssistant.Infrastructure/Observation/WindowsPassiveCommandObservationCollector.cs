using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Observation;

public interface ICommandObservationNativeApi
{
    nint GetForegroundWindow();
    string? GetWindowTitle(nint windowHandle);
    uint? GetWindowProcessId(nint windowHandle);
    string? GetProcessName(uint? processId);
}

public sealed class WindowsPassiveCommandObservationCollector : ICommandObservationCollector
{
    private readonly ICommandObservationNativeApi _nativeApi;
    private readonly Func<bool> _isWindowsPlatform;

    public WindowsPassiveCommandObservationCollector()
        : this(new CommandObservationNativeApi(), null)
    {
    }

    public WindowsPassiveCommandObservationCollector(
        ICommandObservationNativeApi nativeApi,
        Func<bool>? isWindowsPlatform = null)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _isWindowsPlatform = isWindowsPlatform ?? OperatingSystem.IsWindows;
    }

    public Task<CommandObservationSnapshot> CollectAsync(
        CommandObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_isWindowsPlatform())
        {
            return Task.FromResult(CreateSnapshotFromBaseline(
                request,
                ObservationCollectionStatus.Partial,
                "Foreground observation is unavailable on this platform."));
        }

        try
        {
            var windowHandle = _nativeApi.GetForegroundWindow();
            if (windowHandle == nint.Zero)
            {
                return Task.FromResult(CreateSnapshotFromBaseline(
                    request,
                    ObservationCollectionStatus.Partial,
                    "No foreground window detected."));
            }

            var title = NormalizeText(_nativeApi.GetWindowTitle(windowHandle));
            var processId = _nativeApi.GetWindowProcessId(windowHandle);
            var processName = NormalizeText(_nativeApi.GetProcessName(processId));

            var baselineWindow = request.BaselineObservation?.ActiveWindow;
            var baselineProcessName = NormalizeText(request.BaselineObservation?.ActiveProcessName)
                ?? NormalizeText(baselineWindow?.ProcessName);

            var normalizedProcessId = processId.HasValue && processId.Value <= int.MaxValue
                ? (int?)processId.Value
                : null;

            var windowObservation = new ForegroundWindowObservation
            {
                Title = title ?? NormalizeText(baselineWindow?.Title),
                Handle = windowHandle.ToInt64(),
                IsForeground = true
            };

            var processObservation = new ForegroundProcessObservation
            {
                Name = processName ?? baselineProcessName,
                ProcessId = normalizedProcessId
            };

            var status = DetermineCollectionStatus(windowObservation, processObservation);
            var message = status == ObservationCollectionStatus.Success
                ? "Foreground command observation captured."
                : "Foreground command observation partially captured.";

            return Task.FromResult(CreateSnapshot(request, windowObservation, processObservation, status, message));
        }
        catch
        {
            return Task.FromResult(CreateSnapshotFromBaseline(
                request,
                ObservationCollectionStatus.Failed,
                "Foreground command observation failed."));
        }
    }

    private static ObservationCollectionStatus DetermineCollectionStatus(
        ForegroundWindowObservation? window,
        ForegroundProcessObservation? process)
    {
        var hasWindowTitle = !string.IsNullOrWhiteSpace(window?.Title);
        var hasWindowHandle = window?.Handle.HasValue == true;
        var hasProcessName = !string.IsNullOrWhiteSpace(process?.Name);
        var hasProcessId = process?.ProcessId.HasValue == true;

        if (!hasWindowTitle && !hasWindowHandle && !hasProcessName && !hasProcessId)
        {
            return ObservationCollectionStatus.Failed;
        }

        if (hasWindowTitle && hasWindowHandle && hasProcessName && hasProcessId)
        {
            return ObservationCollectionStatus.Success;
        }

        return ObservationCollectionStatus.Partial;
    }

    private static CommandObservationSnapshot CreateSnapshotFromBaseline(
        CommandObservationRequest request,
        ObservationCollectionStatus status,
        string message)
    {
        var baselineWindow = request.BaselineObservation?.ActiveWindow;
        var baselineProcessName = NormalizeText(request.BaselineObservation?.ActiveProcessName)
            ?? NormalizeText(baselineWindow?.ProcessName);

        ForegroundWindowObservation? windowObservation = null;
        if (!string.IsNullOrWhiteSpace(baselineWindow?.Title) || baselineWindow?.Handle is not null)
        {
            windowObservation = new ForegroundWindowObservation
            {
                Title = NormalizeText(baselineWindow?.Title),
                Handle = baselineWindow?.Handle,
                IsForeground = baselineWindow?.IsForeground == true
            };
        }

        ForegroundProcessObservation? processObservation = null;
        if (!string.IsNullOrWhiteSpace(baselineProcessName))
        {
            processObservation = new ForegroundProcessObservation
            {
                Name = baselineProcessName,
                ProcessId = null
            };
        }

        return CreateSnapshot(request, windowObservation, processObservation, status, message);
    }

    private static CommandObservationSnapshot CreateSnapshot(
        CommandObservationRequest request,
        ForegroundWindowObservation? window,
        ForegroundProcessObservation? process,
        ObservationCollectionStatus status,
        string message)
    {
        return new CommandObservationSnapshot
        {
            TimestampUtc = request.TimestampUtc,
            OriginalUserCommand = request.OriginalUserCommand,
            ForegroundWindow = window,
            ForegroundProcess = process,
            GroundingSummary = BuildGroundingSummary(request.PrimaryTargetGrounding),
            SafetySummary = new SafetyObservationSummary
            {
                Disposition = request.SafetyDisposition,
                RiskLevel = request.SafetyRiskLevel,
                RequiresApproval = request.SafetyDisposition == SafetyDisposition.RequiresApproval
            },
            CollectionStatus = status,
            CollectionMessage = message
        };
    }

    private static GroundingObservationSummary? BuildGroundingSummary(TargetGroundingResult? grounding)
    {
        if (grounding is null)
        {
            return null;
        }

        return new GroundingObservationSummary
        {
            Disposition = grounding.Disposition,
            TargetKind = grounding.Target.Kind,
            CanonicalValue = string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue)
                ? null
                : grounding.Target.CanonicalValue,
            Reason = grounding.Reason
        };
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class CommandObservationNativeApi : ICommandObservationNativeApi
    {
        public nint GetForegroundWindow()
        {
            return NativeMethods.GetForegroundWindow();
        }

        public string? GetWindowTitle(nint windowHandle)
        {
            var length = NativeMethods.GetWindowTextLengthW(windowHandle);
            if (length <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length + 1);
            var copied = NativeMethods.GetWindowTextW(windowHandle, builder, builder.Capacity);
            if (copied <= 0)
            {
                return null;
            }

            return builder.ToString();
        }

        public uint? GetWindowProcessId(nint windowHandle)
        {
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
            return processId == 0 ? null : processId;
        }

        public string? GetProcessName(uint? processId)
        {
            if (!processId.HasValue || processId.Value == 0 || processId.Value > int.MaxValue)
            {
                return null;
            }

            try
            {
                return Process.GetProcessById((int)processId.Value).ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(nint hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    }
}