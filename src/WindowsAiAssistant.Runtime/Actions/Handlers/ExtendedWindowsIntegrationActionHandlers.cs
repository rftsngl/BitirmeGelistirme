using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ServiceControlActionHandler : IActionHandler
{
    private readonly IServiceControlService _service;

    public ServiceControlActionHandler(IServiceControlService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string ActionName => "service_control";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "list";
        var name = ActionParameterReader.GetTargetOrParameter(action, "name", "service", "serviceName");
        return Task.FromResult(_service.Execute(mode, name));
    }
}

public sealed class EventLogActionHandler : IActionHandler
{
    private readonly IEventLogService _eventLog;

    public EventLogActionHandler(IEventLogService eventLog) =>
        _eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));

    public string ActionName => "event_log";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "read";
        var logName = ActionParameterReader.GetTargetOrParameter(action, "log", "logName");
        var level = ActionParameterReader.GetTargetOrParameter(action, "level");
        var hours = ActionParameterReader.TryGetInt(action, "hours", out var h) ? h : 1;
        var max = ActionParameterReader.TryGetInt(action, "maxEntries", out var m) ? m : 50;
        return Task.FromResult(_eventLog.Execute(mode, logName, level, hours, max));
    }
}

public sealed class RegistryOpActionHandler : IActionHandler
{
    private readonly IRegistryOperationService _registry;

    public RegistryOpActionHandler(IRegistryOperationService registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public string ActionName => "registry_op";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "read";
        var hive = ActionParameterReader.GetTargetOrParameter(action, "hive");
        var path = ActionParameterReader.GetTargetOrParameter(action, "path", "key");
        var name = ActionParameterReader.GetTargetOrParameter(action, "name", "valueName");
        var value = ActionParameterReader.GetTargetOrParameter(action, "value", "data");
        var kind = ActionParameterReader.GetTargetOrParameter(action, "kind", "type");
        return Task.FromResult(_registry.Execute(mode, hive, path, name, value, kind));
    }
}

public sealed class ClipboardActionHandler : IActionHandler
{
    private readonly IClipboardIntegrationService _clipboard;

    public ClipboardActionHandler(IClipboardIntegrationService clipboard) =>
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));

    public string ActionName => "clipboard";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "read";
        var text = ActionParameterReader.GetTargetOrParameter(action, "text", "content");
        return Task.FromResult(_clipboard.Execute(mode, text));
    }
}

public sealed class InstallPackageActionHandler : IActionHandler
{
    private readonly IPackageInstallService _packages;

    public InstallPackageActionHandler(IPackageInstallService packages) =>
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));

    public string ActionName => "install_package";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "list";
        var id = ActionParameterReader.GetTargetOrParameter(action, "id", "package", "packageId");
        var source = ActionParameterReader.GetTargetOrParameter(action, "source");
        return Task.FromResult(_packages.Execute(mode, id, source));
    }
}

public sealed class NetworkStatusActionHandler : IActionHandler
{
    private readonly INetworkStatusService _network;

    public NetworkStatusActionHandler(INetworkStatusService network) =>
        _network = network ?? throw new ArgumentNullException(nameof(network));

    public string ActionName => "network_status";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "status";
        return Task.FromResult(_network.Execute(mode));
    }
}

public sealed class AudioPowerActionHandler : IActionHandler
{
    private readonly IAudioPowerService _audioPower;

    public AudioPowerActionHandler(IAudioPowerService audioPower) =>
        _audioPower = audioPower ?? throw new ArgumentNullException(nameof(audioPower));

    public string ActionName => "audio_power";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "get_volume";
        var level = ActionParameterReader.TryGetInt(action, "level", out var l) ? l : (int?)null;
        return Task.FromResult(_audioPower.Execute(mode, level));
    }
}

public sealed class PerfCounterActionHandler : IActionHandler
{
    private readonly IPerformanceCounterService _perf;

    public PerfCounterActionHandler(IPerformanceCounterService perf) =>
        _perf = perf ?? throw new ArgumentNullException(nameof(perf));

    public string ActionName => "perf_counter";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "snapshot";
        return Task.FromResult(_perf.Execute(mode));
    }
}

public sealed class FileSearchActionHandler : IActionHandler
{
    private readonly IFileSearchService _search;

    public FileSearchActionHandler(IFileSearchService search) =>
        _search = search ?? throw new ArgumentNullException(nameof(search));

    public string ActionName => "file_search";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "search";
        var query = ActionParameterReader.GetTargetOrParameter(action, "query", "q");
        var folder = ActionParameterReader.GetTargetOrParameter(action, "folder", "path");
        var max = ActionParameterReader.TryGetInt(action, "maxResults", out var m) ? m : 25;
        return _search.ExecuteAsync(mode, query, folder, max, cancellationToken);
    }
}

public sealed class NotificationListenActionHandler : IActionHandler
{
    private readonly INotificationListenerService _listener;

    public NotificationListenActionHandler(INotificationListenerService listener) =>
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));

    public string ActionName => "notification_listen";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "peek";
        var max = ActionParameterReader.TryGetInt(action, "maxEntries", out var m) ? m : 20;
        return _listener.ExecuteAsync(mode, max, cancellationToken);
    }
}

public sealed class ShellSessionActionHandler : IActionHandler
{
    private readonly IShellSessionService _sessions;

    public ShellSessionActionHandler(IShellSessionService sessions) =>
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    public string ActionName => "shell_session";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "start";
        var sessionId = ActionParameterReader.GetTargetOrParameter(action, "sessionId", "session");
        var command = ActionParameterReader.GetTargetOrParameter(action, "command", "cmd");
        var max = ActionParameterReader.TryGetInt(action, "maxOutputChars", out var m) ? m : 4000;
        return Task.FromResult(_sessions.Execute(mode, sessionId, command, max));
    }
}

public sealed class FileWatchActionHandler : IActionHandler
{
    private readonly IFileWatchService _watch;

    public FileWatchActionHandler(IFileWatchService watch) =>
        _watch = watch ?? throw new ArgumentNullException(nameof(watch));

    public string ActionName => "file_watch";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "start";
        var watchId = ActionParameterReader.GetTargetOrParameter(action, "watchId", "id");
        var path = ActionParameterReader.GetTargetOrParameter(action, "path", "folder");
        var filter = ActionParameterReader.GetTargetOrParameter(action, "filter");
        var recursiveRaw = ActionParameterReader.GetTargetOrParameter(action, "recursive");
        var recursive = recursiveRaw is not null &&
                        (recursiveRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         recursiveRaw.Equals("1", StringComparison.OrdinalIgnoreCase));
        var max = ActionParameterReader.TryGetInt(action, "maxEvents", out var m) ? m : 32;
        return Task.FromResult(_watch.Execute(mode, watchId, path, filter, recursive, max));
    }
}

public sealed class CredentialStoreActionHandler : IActionHandler
{
    private readonly ICredentialStoreService _credentials;

    public CredentialStoreActionHandler(ICredentialStoreService credentials) =>
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));

    public string ActionName => "credential_store";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "list";
        var target = ActionParameterReader.GetTargetOrParameter(action, "target", "name");
        var username = ActionParameterReader.GetTargetOrParameter(action, "username", "user");
        var secret = ActionParameterReader.GetTargetOrParameter(action, "secret", "password");
        return Task.FromResult(_credentials.Execute(mode, target, username, secret));
    }
}
