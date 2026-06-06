namespace WindowsAiAssistant.App.Models;

public sealed class HistoryViewItem
{
    public string RunId { get; init; } = string.Empty;
    public string LogFilePath { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SelectedTool { get; init; } = string.Empty;
    public string TriggerSource { get; init; } = string.Empty;
    public int StepCount { get; init; }
    public string LastResult { get; init; } = string.Empty;

    public string ConversationTitle => BuildConversationTitle(CommandText, LastResult);
    public string CommandPreview =>
        string.IsNullOrWhiteSpace(CommandText) ? "Komut bulunmuyor" : TrimSentence(CommandText, 84);
    public string TimestampDisplay => TimestampUtc.ToLocalTime().ToString("g");
    public string RelativeTimeDisplay => FormatRelativeTime(TimestampUtc);
    public string StepCountDisplay => StepCount == 1 ? "1 adım" : $"{StepCount} adım";
    public string StatusBadgeDisplay => StatusCategory switch
    {
        "ok" => "Başarılı",
        "fail" => "Başarısız",
        "gate" => "Onay",
        "limit" => "Limit",
        _ => string.IsNullOrWhiteSpace(FinalStatus) ? "Bilinmiyor" : FinalStatus
    };
    public string StatusCategory => CategorizeStatus(FinalStatus);
    public bool IsSuccessful => StatusCategory == "ok";
    public string SelectedToolDisplay =>
        string.IsNullOrWhiteSpace(SelectedTool) ? "Kayıtlı eylem yok" : ActionDisplayHelper.ToUserFriendlyLabel(SelectedTool);
    public string LastResultDisplay =>
        string.IsNullOrWhiteSpace(LastResult) ? "Bu kayıtta gösterilecek kısa sonuç bulunmuyor." : LastResult;
    public string TriggerDisplay => TriggerSource switch
    {
        "chat" => "Sohbet",
        "voice_overlay" => "Sesli asistan",
        "hotkey" => "Klavye kısayolu",
        _ => TriggerSource
    };

    private static string BuildConversationTitle(string command, string result)
    {
        var source = string.IsNullOrWhiteSpace(command) ? result : command;
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Adsız sohbet";
        }

        var normalized = source.Trim();
        var lower = normalized.ToLowerInvariant();
        if (lower.Contains("ekran") || lower.Contains("pencere"))
        {
            return "Ekran İncelemesi";
        }

        if (lower.Contains("not defteri") || lower.Contains("notepad"))
        {
            return "Not Defteri İşlemi";
        }

        if (lower.Contains("naber") || lower.Contains("selam") || lower.Contains("merhaba"))
        {
            return "Kısa Sohbet";
        }

        return TrimSentence(normalized, 54);
    }

    private static string TrimSentence(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..Math.Max(1, maxLength - 1)].TrimEnd() + "…";
    }

    private static string CategorizeStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "unknown";
        }

        var normalized = status.ToLowerInvariant();
        if (normalized.Contains("tamamlandi"))
        {
            return "ok";
        }

        if (normalized.Contains("onay") || normalized.Contains("gate"))
        {
            return "gate";
        }

        if (normalized.Contains("limit"))
        {
            return "limit";
        }

        if (normalized.Contains("basarisiz") || normalized.Contains("engel") || normalized.Contains("hata"))
        {
            return "fail";
        }

        return "unknown";
    }

    private static string FormatRelativeTime(DateTimeOffset timestampUtc)
    {
        var local = timestampUtc.ToLocalTime();
        var now = DateTimeOffset.Now;
        if (local.Date == now.Date)
        {
            return $"Bugün {local:HH:mm}";
        }

        if (local.Date == now.Date.AddDays(-1))
        {
            return $"Dün {local:HH:mm}";
        }

        return local.ToString("dd MMM yyyy HH:mm");
    }
}

public sealed class HistoryStepItem
{
    public int StepIndex { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string GateOutcome { get; init; } = string.Empty;
    public string GateRisk { get; init; } = string.Empty;
    public string? GateUserApproved { get; init; }
    public string ObservationSummary { get; init; } = string.Empty;
    public string WindowsSummary { get; init; } = string.Empty;
    public string UiTreeSummary { get; init; } = string.Empty;
    public string ParsedDecision { get; init; } = string.Empty;
    public string GateDecision { get; init; } = string.Empty;
    public string ActionResult { get; init; } = string.Empty;
    public string LlmRawOutput { get; init; } = string.Empty;
    public string? ScreenshotPath { get; init; }
    public string UiTreeRawJson { get; init; } = string.Empty;
    public IReadOnlyList<UiTreeElementRow> UiTreeElements { get; init; } = Array.Empty<UiTreeElementRow>();
    public bool HasUiTreeElements => UiTreeElements.Count > 0;
    public string UiTreeElementsHeader =>
        UiTreeElements.Count > 0 ? $"Elementler ({UiTreeElements.Count})" : "Elementler";
    public bool HasScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPath) && File.Exists(ScreenshotPath);
    public string StatusBadge => Success ? "OK" : "FAIL";
    public bool UiTreeTruncated => UiTreeRawJson.Contains("\"truncated\":true", StringComparison.Ordinal);
    public string UiTreeCharInfo => UiTreeRawJson.Length > 0 ? $"{UiTreeRawJson.Length} karakter" : string.Empty;
    public string GateRiskDisplay => GateRisk switch
    {
        "Safe" => "low",
        "Normal" => "low",
        "Sensitive" => "medium",
        "Destructive" => "critical",
        _ => GateRisk.ToLowerInvariant()
    };
}

public sealed class UiTreeElementRow
{
    public string ElementId { get; init; } = string.Empty;
    public string ControlType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;

    public string Display
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Name) ? "(adsiz)" : Name;
            var type = string.IsNullOrWhiteSpace(ControlType) ? "Element" : ControlType;
            return $"{type} · {label}";
        }
    }

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ElementId))
            {
                parts.Add($"id={ElementId}");
            }

            if (!string.IsNullOrWhiteSpace(Value))
            {
                parts.Add($"value={Value}");
            }

            if (!IsEnabled)
            {
                parts.Add("disabled");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasDetail => Detail.Length > 0;
}
