namespace WindowsAiAssistant.App.Audio;

public static class VoiceApprovalInterpreter
{
    private static readonly string[] ApproveWords =
        ["evet", "onayla", "onayliyorum", "onaylıyorum", "tamam", "kabul", "olur", "yes", "approve"];

    private static readonly string[] DenyWords =
        ["hayir", "hayır", "reddet", "iptal", "vazgec", "vazgeç", "olmaz", "no", "deny", "cancel"];

    public static bool? Interpret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (ApproveWords.Any(word => normalized.Contains(word, StringComparison.Ordinal)))
        {
            return true;
        }

        if (DenyWords.Any(word => normalized.Contains(word, StringComparison.Ordinal)))
        {
            return false;
        }

        return null;
    }
}
