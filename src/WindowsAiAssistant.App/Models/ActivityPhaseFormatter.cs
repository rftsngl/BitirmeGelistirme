using WindowsAiAssistant.Agent;

namespace WindowsAiAssistant.App.Models;

public static class ActivityPhaseFormatter
{
    public static (string Label, string Detail) Format(AgentStepProgress progress)
    {
        var phase = progress.Phase.Trim().ToLowerInvariant();
        var detail = progress.Detail?.Trim() ?? string.Empty;

        return phase switch
        {
            "basladi" => ("Görevi aldım", Truncate(detail, 120)),
            "gozlem" => ("Ekranı okuyorum", "Pencereler, odak ve görünen kontroller taranıyor."),
            "llm" => ("Düşünüyorum", FriendlyLlmDetail(detail)),
            "gate" => ("Güvenlik süzgeci", Truncate(detail, 140)),
            "onay" => ("Onay bekliyor", Truncate(detail, 140)),
            "eylem" => FormatEylem(detail),
            "tamamlandi" => ("Sonuç toparlanıyor", Truncate(detail, 160)),
            "limit" => ("Adım bütçesi doldu", Truncate(detail, 160)),
            _ => (Capitalize(phase), Truncate(detail, 140))
        };
    }

    private static (string Label, string Detail) FormatEylem(string raw)
    {
        if (raw.Contains("|fail|", StringComparison.Ordinal))
        {
            var parts = raw.Split(new[] { "|fail|" }, 2, StringSplitOptions.None);
            var action = parts[0];
            var message = parts.Length > 1 ? parts[1] : string.Empty;
            return ("İşlem tutmadı, rota değişiyor", $"{FormatActionDetail(action)} - {Truncate(message, 100)}");
        }

        return ("Uygulanıyor", FormatActionDetail(raw));
    }

    private static string FormatActionDetail(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Bilgisayarınızda işlem uygulanıyor…";
        }

        var friendly = ActionDisplayHelper.ToUserFriendlyLabel(raw);
        return friendly == raw ? raw : friendly;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..Math.Max(1, maxLength - 1)] + "…";
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpper(value[0]) + value[1..];

    private static string FriendlyLlmDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "Sizin için en uygun yanıtı hazırlıyorum…";
        }

        if (detail.Contains("karar", StringComparison.OrdinalIgnoreCase))
        {
            return "Ne yapacağımı planlıyorum…";
        }

        return "Birazdan hazır olacak…";
    }

    public static string ForDeveloper(AgentStepProgress progress)
    {
        var step = Math.Min(progress.StepIndex + 1, progress.MaxSteps);
        return string.IsNullOrWhiteSpace(progress.Detail)
            ? $"Adım {step}/{progress.MaxSteps}: {progress.Phase}"
            : $"Adım {step}/{progress.MaxSteps}: {progress.Phase} — {progress.Detail}";
    }
}
