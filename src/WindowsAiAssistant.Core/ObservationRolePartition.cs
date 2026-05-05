using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Core;

internal static class ObservationRolePartition
{
    public const string OperationalFields =
        "ForegroundWindow,ForegroundProcess,RuntimeState,GroundingSummary,SafetySummary,CollectionStatus";

    public const string DescriptiveFields =
        "OriginalUserCommand,CollectionMessage,TimestampUtc";

    public static CommandObservationSnapshot? ToOperationalSnapshot(CommandObservationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new CommandObservationSnapshot
        {
            TimestampUtc = snapshot.TimestampUtc,
            OriginalUserCommand = string.Empty,
            ForegroundWindow = snapshot.ForegroundWindow,
            ForegroundProcess = snapshot.ForegroundProcess,
            GroundingSummary = snapshot.GroundingSummary,
            SafetySummary = snapshot.SafetySummary,
            RuntimeState = snapshot.RuntimeState,
            CollectionStatus = snapshot.CollectionStatus,
            CollectionMessage = null
        };
    }
}
