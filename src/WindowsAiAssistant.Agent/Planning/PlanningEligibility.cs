namespace WindowsAiAssistant.Agent.Planning;

internal static class PlanningEligibility
{
    internal static bool ShouldPlan(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var normalized = Normalize(userGoal);
        if (normalized.Length < 12)
        {
            return false;
        }

        if (LooksConversationOnly(normalized))
        {
            return false;
        }

        if (GoalRoutingHints.TryBuildFastDecision(userGoal) is not null)
        {
            return false;
        }

        if (HasMultiStepMarker(normalized))
        {
            return true;
        }

        return ContainsAny(normalized,
            "ac ", " ac", "acik", "açik", "aç ", " aç",
            "kapat", "yaz", "tikla", "tıkla", "belge", "dosya", "pencere",
            "word", "excel", "chrome", "edge", "firefox", "notepad",
            "uygulama", "kaydet", "kopyala", "yapistir", "yapıştır",
            "baslik", "başlık", "form", "url", "site", "tarayici", "tarayıcı",
            "shell", "komut", "ayar", "settings", "klasor", "klasör");
    }

    private static bool LooksConversationOnly(string normalized)
    {
        if (normalized.Length > 80)
        {
            return false;
        }

        var greetingOnly = ContainsAny(normalized,
            "merhaba", "selam", "nasilsin", "nasılsın", "tesekkur", "teşekkür",
            "iyi gunler", "günaydin", "gunaydin", "naber", "ne haber");

        if (!greetingOnly)
        {
            return false;
        }

        return !ContainsAny(normalized,
            "ac", "aç", "yaz", "tikla", "tıkla", "belge", "word", "pencere", "dosya", "uygulama");
    }

    private static bool HasMultiStepMarker(string normalized) =>
        normalized.Contains(" ve ", StringComparison.Ordinal) ||
        normalized.Contains(" sonra ", StringComparison.Ordinal) ||
        normalized.Contains(" ardindan ", StringComparison.Ordinal) ||
        normalized.Contains(" ardından ", StringComparison.Ordinal) ||
        normalized.Contains(" daha sonra ", StringComparison.Ordinal);

    private static string Normalize(string text) =>
        text.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    private static bool ContainsAny(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));

    internal static bool LooksDesktopGoal(string userGoal) => ShouldPlan(userGoal);
}
