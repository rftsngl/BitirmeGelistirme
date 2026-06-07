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
