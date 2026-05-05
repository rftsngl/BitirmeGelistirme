using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public interface IWindowActivationNativeApi
{
    bool IsWindow(nint windowHandle);
    bool ShowWindow(nint windowHandle, int command);
    bool SetForegroundWindow(nint windowHandle);
}

public interface IMainWindowHandleResolver
{
    bool TryResolveMainWindowHandle(string processName, out long windowHandle);
}

public sealed class WindowProcessCapability : ICapability
{
    private const int SwRestore = 9;

    private readonly IMainWindowHandleResolver _mainWindowHandleResolver;
    private readonly IWindowActivationNativeApi _nativeApi;
    private readonly Func<bool> _isWindowsPlatform;

    public WindowProcessCapability()
        : this(new WindowActivationNativeApi(), new ProcessMainWindowHandleResolver(), null)
    {
    }

    public WindowProcessCapability(
        IWindowActivationNativeApi nativeApi,
        IMainWindowHandleResolver mainWindowHandleResolver,
        Func<bool>? isWindowsPlatform = null)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _mainWindowHandleResolver = mainWindowHandleResolver ?? throw new ArgumentNullException(nameof(mainWindowHandleResolver));
        _isWindowsPlatform = isWindowsPlatform ?? OperatingSystem.IsWindows;
    }

    public string Name => "WindowProcessCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Window,
        TargetKind.Process,
        TargetKind.Application
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        if (action.Target is null)
        {
            return false;
        }

        if (action.Target.Kind is not (TargetKind.Window or TargetKind.Process or TargetKind.Application))
        {
            return false;
        }

        if (!action.ActionName.Equals("FocusWindow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (action.Target.Kind == TargetKind.Window)
        {
            return TryExtractWindowHandle(action.Target, out _);
        }

        return TryExtractProcessName(action.Target, out _);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        if (!_isWindowsPlatform())
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "WindowProcessCapability blocked: focus is only available on Windows.",
                "Blocked execution: platform does not support window focus.",
                null,
                "unknown",
                false,
                startedAtUtc));
        }

        if (action.Target is null)
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "WindowProcessCapability blocked: no target was provided.",
                "Blocked execution: no focus target available.",
                null,
                "unknown",
                false,
                startedAtUtc));
        }

        if (!TryResolveWindowHandle(action.Target, out var windowHandle, out var resolvedFrom, out var mainWindowResolved))
        {
            var blockedMessage = action.Target.Kind is TargetKind.Process or TargetKind.Application
                ? "WindowProcessCapability blocked: main window could not be resolved for process/application target."
                : "WindowProcessCapability blocked: window handle is required.";

            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                blockedMessage,
                "Blocked execution: no valid window handle available.",
                null,
                action.Target.Kind.ToString().ToLowerInvariant(),
                false,
                startedAtUtc));
        }

        try
        {
            var nativeHandle = (nint)windowHandle;
            if (!_nativeApi.IsWindow(nativeHandle))
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Blocked,
                    "WindowProcessCapability blocked: target window handle is invalid.",
                    "Blocked execution: invalid window handle.",
                    windowHandle,
                        resolvedFrom,
                        mainWindowResolved,
                    startedAtUtc));
            }

            _nativeApi.ShowWindow(nativeHandle, SwRestore);
            if (_nativeApi.SetForegroundWindow(nativeHandle))
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "WindowProcessCapability executed real focus path.",
                    $"Real focus attempted for window handle: {windowHandle}",
                    windowHandle,
                        resolvedFrom,
                        mainWindowResolved,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "WindowProcessCapability failed: SetForegroundWindow returned false.",
                $"Real focus failed for window handle: {windowHandle}",
                windowHandle,
                resolvedFrom,
                mainWindowResolved,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "WindowProcessCapability failed: native focus operation threw an exception.",
                $"Real focus failed for window handle: {windowHandle}",
                windowHandle,
                resolvedFrom,
                mainWindowResolved,
                startedAtUtc));
        }
    }

    private bool TryResolveWindowHandle(
        TargetReference target,
        out long windowHandle,
        out string resolvedFrom,
        out bool mainWindowResolved)
    {
        windowHandle = 0;
        mainWindowResolved = false;
        resolvedFrom = target.Kind.ToString().ToLowerInvariant();

        if (TryExtractWindowHandle(target, out windowHandle))
        {
            resolvedFrom = "window";
            return true;
        }

        if (target.Kind is not (TargetKind.Process or TargetKind.Application))
        {
            return false;
        }

        if (!TryExtractProcessName(target, out var processName))
        {
            return false;
        }

        if (!_mainWindowHandleResolver.TryResolveMainWindowHandle(processName, out windowHandle) || windowHandle <= 0)
        {
            return false;
        }

        mainWindowResolved = true;
        resolvedFrom = target.Kind == TargetKind.Process ? "process" : "application";
        return true;
    }

    private static bool TryExtractWindowHandle(TargetReference target, out long windowHandle)
    {
        windowHandle = 0;

        if (long.TryParse(target.NormalizedValue, out var parsedNormalized) && parsedNormalized > 0)
        {
            windowHandle = parsedNormalized;
            return true;
        }

        if (target.Metadata is null)
        {
            return false;
        }

        if (target.Metadata.TryGetValue("windowHandle", out var metadataHandle) &&
            long.TryParse(metadataHandle, out var parsedMetadata) &&
            parsedMetadata > 0)
        {
            windowHandle = parsedMetadata;
            return true;
        }

        return false;
    }

    private static bool TryExtractProcessName(TargetReference target, out string processName)
    {
        processName = string.Empty;

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var metadataProcessName) &&
            !string.IsNullOrWhiteSpace(metadataProcessName))
        {
            processName = NormalizeProcessName(metadataProcessName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue) &&
            !long.TryParse(target.NormalizedValue, out _))
        {
            processName = NormalizeProcessName(target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(processName);
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            processName = NormalizeProcessName(target.DisplayName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        return false;
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static ActionExecutionResult CreateResult(
        ExecutionStatus status,
        string message,
        string outputText,
        long? windowHandle,
        string resolvedFrom,
        bool mainWindowResolved,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["focusTargetResolvedFrom"] = resolvedFrom,
            ["focusMainWindowResolved"] = mainWindowResolved ? "true" : "false"
        };

        if (windowHandle.HasValue)
        {
            outputData["windowHandle"] = windowHandle.Value.ToString();
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = message,
            IsVerified = false,
            OutputText = outputText,
            OutputData = outputData,
            ErrorCode = null,
            UsedFallback = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class ProcessMainWindowHandleResolver : IMainWindowHandleResolver
    {
        public bool TryResolveMainWindowHandle(string processName, out long windowHandle)
        {
            windowHandle = 0;

            var normalizedProcessName = NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(normalizedProcessName))
            {
                return false;
            }

            Process[]? processes = null;

            try
            {
                processes = Process.GetProcessesByName(normalizedProcessName);
                foreach (var process in processes.OrderBy(p => p.Id))
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle != nint.Zero)
                        {
                            windowHandle = handle.ToInt64();
                            return true;
                        }
                    }
                    catch
                    {
                        // Main-window probing is best-effort.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }

    private sealed class WindowActivationNativeApi : IWindowActivationNativeApi
    {
        public bool IsWindow(nint windowHandle)
        {
            return NativeMethods.IsWindow(windowHandle);
        }

        public bool ShowWindow(nint windowHandle, int command)
        {
            return NativeMethods.ShowWindow(windowHandle, command);
        }

        public bool SetForegroundWindow(nint windowHandle)
        {
            return NativeMethods.SetForegroundWindow(windowHandle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(nint hWnd);
    }
}
