using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Observation;

public sealed class NullObservationProvider : IObservationProvider
{
    public Task<ObservationSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new ObservationSnapshot
        {
            ActiveWindow = new WindowContext
            {
                IsForeground = false
            },
            ActiveProcessName = null,
            ClipboardTextPreview = null,
            HasSelection = false,
            SelectionTextPreview = null,
            DesktopStateSummary = "No real observation captured (null provider).",
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        return Task.FromResult(snapshot);
    }
}
