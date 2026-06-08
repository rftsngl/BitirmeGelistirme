namespace WindowsAiAssistant.Agent;

using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Runtime.Observation;

/// <summary>
/// Kullanici hedefinden deterministik action onerileri (LLM yanlis arac sectiginde guvenlik agi).
/// </summary>
internal static class GoalRoutingHints
{
    internal sealed record AudioRoute(string Mode, int? Level);

    internal static AgentDecision? TryBuildFastDecision(string userGoal)
    {
        if (!ShouldUseFastPath(userGoal))
        {
            return null;
        }

        return TryBuildFastAudioDecision(userGoal) ??
               TryBuildFastNetworkDecision(userGoal) ??
               TryBuildFastPerfDecision(userGoal);
    }

    internal static bool ShouldCompleteAfterFastRoute(string userGoal, string action) =>
        TryBuildFastDecision(userGoal) is { } fast &&
        string.Equals(fast.Action, action, StringComparison.OrdinalIgnoreCase);

    internal static AudioRoute? TryResolveAudioRoute(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return null;
        }

        var text = Normalize(userGoal);

        if (MatchesAny(text,
                "sesi kapat", "ses kapat", "sesi sustur", "ses sustur", "sessize al",
                "sustur", "mute", "sesi kapatir misin", "sesi kapatır mısın"))
        {
            return new AudioRoute("mute", null);
        }

        if (MatchesAny(text,
                "sesi ac", "sesi aç", "ses ac", "ses aç", "unmute",
                "sessizligi kaldir", "sessizliği kaldır", "sesi geri ac", "sesi geri aç"))
        {
            return new AudioRoute("unmute", null);
        }

        if (MatchesAny(text, "ses seviyesi", "ses yuzde", "ses yüzde", "volume"))
        {
            var level = TryExtractPercent(text);
            if (level is not null)
            {
                return new AudioRoute("set_volume", level);
            }
        }

        if (MatchesAny(text, "sesi dusur", "sesi düşür", "sesi azalt"))
        {
            return new AudioRoute("set_volume", 30);
        }

        if (MatchesAny(text, "sesi artir", "sesi artır", "sesi yukselt", "sesi yükselt"))
        {
            return new AudioRoute("set_volume", 70);
        }

        return null;
    }

    internal static void AppendPromptHints(StringBuilder builder, string userGoal, DesktopObservation observation, string? triggerSource)
    {
        builder.AppendLine("Runtime reminder: scan P1 integrations + P2 launch + P3 shell BEFORE any click_element/type_text.");
        builder.AppendLine("- UI automation is for explicit in-app manipulation, not for system-level goals the app already handles.");
        builder.AppendLine();

        var audio = TryResolveAudioRoute(userGoal);
        if (audio is not null)
        {
            builder.AppendLine("SUGGESTED — SYSTEM AUDIO GOAL (you decide):");
            builder.AppendLine($"- Strong option: audio_power with parameters.mode={audio.Mode}.");
            if (audio.Level is not null)
            {
                builder.AppendLine($"- Also set parameters.level={audio.Level} (0-100).");
            }

            builder.AppendLine("- Do NOT use click_element, mouse_click, focus_window or press_key on speaker/tray/overlay icons.");
            builder.AppendLine("- The voice overlay microphone icon is for listening — it does NOT mute system audio.");
            builder.AppendLine("- uiElements may be empty while overlay is open; audio_power still works.");
            builder.AppendLine();
        }

        if (TryBuildFastNetworkDecision(userGoal) is not null)
        {
            builder.AppendLine("SUGGESTED — NETWORK STATUS GOAL (you decide):");
            builder.AppendLine("- Strong option: network_status with parameters.mode=status (or adapters for adapter list).");
            builder.AppendLine("- Do NOT open Settings UI or click network tray icons for a simple status query.");
            builder.AppendLine();
        }

        if (TryBuildFastPerfDecision(userGoal) is not null)
        {
            builder.AppendLine("SUGGESTED — PERFORMANCE SNAPSHOT GOAL (you decide):");
            builder.AppendLine("- Strong option: perf_counter with parameters.mode=snapshot.");
            builder.AppendLine("- Do NOT open Task Manager UI for a quick CPU/RAM/disk summary.");
            builder.AppendLine();
        }

        FastRoutePromptHints.Append(builder, userGoal, observation);
        PlaybookPromptHints.Append(builder, userGoal, observation);

        if (observation.UiCaptureSkipReason is not null &&
            observation.UiCaptureSkipReason.Contains("atlandi", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("Observation note: UIA tree was skipped for the assistant overlay/window.");
            builder.AppendLine("- Do NOT retry click_element on missing elementIds from a previous step.");
            builder.AppendLine("- Use Windows integration actions (audio_power, shell, launch, network_status, ...) instead.");
            builder.AppendLine();
        }

        if (string.Equals(triggerSource, "voice_overlay", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("Voice overlay context:");
            builder.AppendLine("- Desktop goals (mute, open app, settings) MUST use backend actions — not the overlay UI.");
            builder.AppendLine("- After a successful audio_power/shell/launch, respond in Turkish with decisionType=complete.");
            builder.AppendLine();
        }
    }

    internal static AgentDecision? TryBuildFastAudioDecision(string userGoal)
    {
        if (!ShouldUseFastPath(userGoal))
        {
            return null;
        }

        var route = TryResolveAudioRoute(userGoal);
        if (route is null)
        {
            return null;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = route.Mode
        };

        if (route.Level is not null)
        {
            parameters["level"] = route.Level.Value.ToString();
        }

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "audio_power",
            Reason = "Fast route: system audio goal detected in user text.",
            Parameters = parameters
        };
    }

    internal static AgentDecision? TryBuildFastNetworkDecision(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal) || !ShouldUseFastPath(userGoal))
        {
            return null;
        }

        var text = Normalize(userGoal);
        if (!MatchesAny(text,
                "internet var mi", "internet var mı", "internet baglantisi", "internet bağlantısı",
                "ag durumu", "ağ durumu", "wifi durumu", "wi-fi durumu", "bagli mi", "bağlı mı",
                "ip adresim", "ip adresi", "network status", "online mi", "internete bagli"))
        {
            return null;
        }

        var mode = MatchesAny(text, "adaptor", "adapter", "ag karti", "ağ kartı")
            ? "adapters"
            : "status";

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "network_status",
            Reason = "Fast route: network status goal detected in user text.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = mode
            }
        };
    }

    internal static AgentDecision? TryBuildFastPerfDecision(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal) || !ShouldUseFastPath(userGoal))
        {
            return null;
        }

        var text = Normalize(userGoal);
        if (!MatchesAny(text,
                "cpu kullanimi", "cpu kullanımı", "ram kullanimi", "ram kullanımı", "bellek kullanimi",
                "bellek kullanımı", "disk doluluk", "performans ozeti", "performans özeti",
                "sistem yuku", "sistem yükü", "pc ne kadar yuklu", "pc ne kadar yüklü",
                "task manager", "gorev yoneticisi", "görev yöneticisi"))
        {
            return null;
        }

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "perf_counter",
            Reason = "Fast route: performance snapshot goal detected in user text.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "snapshot"
            }
        };
    }

    private static bool ShouldUseFastPath(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var normalized = Normalize(userGoal);
        if (normalized.Contains(" ve ", StringComparison.Ordinal) ||
            normalized.Contains(" sonra ", StringComparison.Ordinal) ||
            normalized.Contains(" ardindan ", StringComparison.Ordinal) ||
            normalized.Contains(" ardından ", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string Normalize(string text) =>
        text.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    private static bool MatchesAny(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));

    private static int? TryExtractPercent(string normalized)
    {
        for (var i = 0; i < normalized.Length; i++)
        {
            if (!char.IsDigit(normalized[i]))
            {
                continue;
            }

            var start = i;
            while (i < normalized.Length && char.IsDigit(normalized[i]))
            {
                i++;
            }

            if (int.TryParse(normalized[start..i], out var value) && value is >= 0 and <= 100)
            {
                return value;
            }
        }

        return null;
    }
}
