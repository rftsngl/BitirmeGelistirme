using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public interface IGraphicsCaptureService
{
    ActionResult Capture(string runId, int stepIndex, string? monitor = null, string? method = null);
}

public interface IToastNotificationService
{
    ActionResult Show(string title, string body);
}

public interface IWmiQueryService
{
    ActionResult Query(string wql, string? wmiNamespace = null);
}

public interface ITaskSchedulerIntegrationService
{
    ActionResult Execute(string mode, string? taskName = null, string? command = null, string? trigger = null, string? arguments = null);
}

public interface IJumpListService
{
    ActionResult Update(string mode, string? tasks = null);
}

public interface IComAutomationService
{
    ActionResult Invoke(string progId, string method, string? arguments = null, bool closeInstance = false);
}

public interface IWindowsHelloService
{
    Task<ActionResult> VerifyAsync(string message, CancellationToken cancellationToken = default);
}

public interface IGlobalHookService
{
    ActionResult Execute(string mode, string? hookType = null, int maxEvents = 32);
}

public interface IServiceControlService
{
    ActionResult Execute(string mode, string? serviceName = null);
}

public interface IEventLogService
{
    ActionResult Execute(string mode, string? logName = null, string? level = null, int hours = 1, int maxEntries = 50);
}

public interface IRegistryOperationService
{
    ActionResult Execute(string mode, string? hive = null, string? path = null, string? name = null, string? value = null, string? kind = null);
}

public interface IClipboardIntegrationService
{
    ActionResult Execute(string mode, string? text = null);
}

public interface IPackageInstallService
{
    ActionResult Execute(string mode, string? packageId = null, string? source = null);
}

public interface INetworkStatusService
{
    ActionResult Execute(string mode = "status");
}

public interface IAudioPowerService
{
    ActionResult Execute(string mode, int? level = null);
}

public interface IPerformanceCounterService
{
    ActionResult Execute(string mode = "snapshot");
}

public interface IFileSearchService
{
    Task<ActionResult> ExecuteAsync(string mode, string? query = null, string? folder = null, int maxResults = 25, CancellationToken cancellationToken = default);
}

public interface INotificationListenerService
{
    Task<ActionResult> ExecuteAsync(string mode, int maxEntries = 20, CancellationToken cancellationToken = default);
}

public interface IShellSessionService
{
    ActionResult Execute(string mode, string? sessionId = null, string? command = null, int maxOutputChars = 4000);
}

public interface IFileWatchService
{
    ActionResult Execute(string mode, string? watchId = null, string? path = null, string? filter = null, bool recursive = false, int maxEvents = 32);
}

public interface ICredentialStoreService
{
    ActionResult Execute(string mode, string? target = null, string? username = null, string? secret = null);
}
