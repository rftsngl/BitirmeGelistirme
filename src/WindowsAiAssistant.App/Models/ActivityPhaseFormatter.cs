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
            "basladi" => ("Görev alındı", Truncate(detail, 120)),
            "gozlem" => ("Masaüstü inceleniyor", "Açık pencereler, arayüz öğeleri ve ekran görüntüsü toplanıyor."),
            "llm" => ("Düşünüyor", string.IsNullOrWhiteSpace(detail) ? "Sonraki adım planlanıyor…" : detail),
            "gate" => ("Güvenlik kontrolü", Truncate(detail, 140)),
            "onay" => ("Onayınız bekleniyor", Truncate(detail, 140)),
            "eylem" => FormatEylem(detail),
            "tamamlandi" => ("Yanıt hazırlanıyor", Truncate(detail, 160)),
            "limit" => ("Adım limitine ulaşıldı", Truncate(detail, 160)),
            _ => (Capitalize(phase), Truncate(detail, 140))
        };
    }

    private static (string Label, string Detail) FormatEylem(string raw)
    {
        if (raw.Contains("|fail|", StringComparison.Ordinal))
        {
            var parts = raw.Split("|fail|", 2, StringComparison.Ordinal);
            var action = parts[0];
            var message = parts.Length > 1 ? parts[1] : string.Empty;
            return ("İşlem başarısız, yeniden deneniyor", $"{FormatActionDetail(action)} — {Truncate(message, 100)}");
        }

        return ("İşlem yapılıyor", FormatActionDetail(raw));
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
}
