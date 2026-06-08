namespace WindowsAiAssistant.Agent.Planning;

public sealed class CompletionVerificationResult
{
    public bool IsComplete { get; init; }
    public string? MissingWork { get; init; }
    public string? SuggestedNextAction { get; init; }
    public string? RawResponse { get; init; }

    public static CompletionVerificationResult Complete() =>
        new() { IsComplete = true };

    public static CompletionVerificationResult Incomplete(string missing, string? suggested = null) =>
        new() { IsComplete = false, MissingWork = missing, SuggestedNextAction = suggested };
}
