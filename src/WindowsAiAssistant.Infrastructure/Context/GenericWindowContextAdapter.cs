using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Context;

public sealed class GenericWindowContextAdapter : IContextAdapter
{
    public string Name => "generic-window";

    public bool CanHandle(ObservationSnapshot observation)
    {
        var window = observation.ActiveWindow;
        if (window is null)
        {
            return false;
        }

        return window.Handle.HasValue ||
               !string.IsNullOrWhiteSpace(window.Title) ||
               !string.IsNullOrWhiteSpace(window.ProcessName) ||
               !string.IsNullOrWhiteSpace(observation.ActiveProcessName);
    }

    public ContextAdapterContext BuildContext(ObservationSnapshot observation)
    {
        return new ContextAdapterContext
        {
            AdapterName = Name,
            ProcessName = observation.ActiveProcessName ?? observation.ActiveWindow?.ProcessName,
            WindowTitle = observation.ActiveWindow?.Title,
            WindowHandle = observation.ActiveWindow?.Handle,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "foreground-observation"
            }
        };
    }
}
