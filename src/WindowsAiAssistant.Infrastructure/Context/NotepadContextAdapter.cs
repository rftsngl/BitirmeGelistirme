using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Context;

public sealed class NotepadContextAdapter : IContextAdapter
{
    public string Name => "notepad";

    public bool CanHandle(ObservationSnapshot observation)
    {
        var process = observation.ActiveProcessName ?? observation.ActiveWindow?.ProcessName;
        if (string.IsNullOrWhiteSpace(process))
        {
            return false;
        }

        return process.Contains("notepad", StringComparison.OrdinalIgnoreCase);
    }

    public ContextAdapterContext BuildContext(ObservationSnapshot observation)
    {
        var process = observation.ActiveProcessName ?? observation.ActiveWindow?.ProcessName;
        return new ContextAdapterContext
        {
            AdapterName = Name,
            ProcessName = process,
            WindowTitle = observation.ActiveWindow?.Title,
            WindowHandle = observation.ActiveWindow?.Handle,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "foreground-observation"
            }
        };
    }
}
