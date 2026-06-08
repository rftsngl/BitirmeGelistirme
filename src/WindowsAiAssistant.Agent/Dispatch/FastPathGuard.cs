namespace WindowsAiAssistant.Agent.Dispatch;

internal static class FastPathGuard
{
    internal static bool IsSingleStepGoal(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var normalized = Normalize(userGoal);
        return !normalized.Contains(" ve ", StringComparison.Ordinal) &&
               !normalized.Contains(" sonra ", StringComparison.Ordinal) &&
               !normalized.Contains(" ardindan ", StringComparison.Ordinal) &&
               !normalized.Contains(" ardından ", StringComparison.Ordinal);
    }

    internal static string Normalize(string text) =>
        text.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    internal static bool MatchesAny(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));
}
