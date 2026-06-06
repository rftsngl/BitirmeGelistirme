using System.Text.Json;

namespace WindowsAiAssistant.Runtime.Logging;

public sealed class RunLogStep
{
    public int StepIndex { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? TriggerSource { get; init; }
    public string? LlmRawOutput { get; init; }
    public string? ObservationSummaryJson { get; init; }
    public string? ScreenshotPath { get; init; }
    public string? WindowsSummaryJson { get; init; }
    public string? UiTreeSummaryJson { get; init; }
    public string? ParsedDecisionJson { get; init; }
    public string? GateDecisionJson { get; init; }
    public string? ActionResultJson { get; init; }

    public string ActionName => ExtractNestedField(ParsedDecisionJson, "Action") ?? "(bilinmiyor)";
    public string? ActionTarget => ExtractNestedField(ParsedDecisionJson, "Target");
    public string ResultMessage => ExtractNestedField(ActionResultJson, "Message") ?? string.Empty;
    public bool ActionSuccess => ExtractNestedBool(ActionResultJson, "Success") ?? false;
    public string GateOutcome => ExtractNestedField(GateDecisionJson, "outcome") ?? "(yok)";
    public string GateRisk => ExtractNestedField(GateDecisionJson, "risk") ?? "(yok)";
    public string? GateUserApproved
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GateDecisionJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(GateDecisionJson);
                if (doc.RootElement.TryGetProperty("userApproved", out var value) &&
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return value.GetBoolean() ? "evet" : "hayir";
                }
            }
            catch (JsonException)
            {
                // ignore
            }

            return null;
        }
    }

    private static string? ExtractNestedField(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => value.GetRawText(),
                    _ => value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool? ExtractNestedBool(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}

public sealed class ParsedRunLog
{
    public required string RunId { get; init; }
    public required string LogFilePath { get; init; }
    public required string UserGoal { get; init; }
    public string TriggerSource { get; init; } = "bilinmiyor";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public required IReadOnlyList<RunLogStep> Steps { get; init; }
    public required string FinalStatus { get; init; }
    public string LastAction { get; init; } = string.Empty;
    public string LastResult { get; init; } = string.Empty;
}

public sealed class RunLogReader
{
    public ParsedRunLog? TryParseFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var steps = new List<RunLogStep>();
            string userGoal = "(bilinmiyor)";
            string runId = Path.GetFileNameWithoutExtension(filePath);
            string triggerSource = "bilinmiyor";
            DateTimeOffset startedAt = File.GetLastWriteTimeUtc(filePath);
            DateTimeOffset endedAt = startedAt;

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var step = ParseStep(root);
                    if (step is null)
                    {
                        continue;
                    }

                    steps.Add(step);
                    runId = ReadString(root, "RunId") ?? runId;
                    userGoal = ReadString(root, "UserGoal") ?? userGoal;
                    triggerSource = step.TriggerSource ?? triggerSource;
                    startedAt = steps.Count == 1 ? step.Timestamp : startedAt;
                    endedAt = step.Timestamp;
                }
                catch (JsonException)
                {
                    // skip malformed line
                }
            }

            if (steps.Count == 0)
            {
                return null;
            }

            steps.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            var last = steps[^1];
            return new ParsedRunLog
            {
                RunId = runId,
                LogFilePath = filePath,
                UserGoal = userGoal,
                TriggerSource = triggerSource,
                StartedAt = startedAt,
                EndedAt = endedAt,
                Steps = steps,
                FinalStatus = InferFinalStatus(steps),
                LastAction = last.ActionName,
                LastResult = last.ResultMessage
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<ParsedRunLog> ListRuns(string logsDirectory, int maxCount = 50)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return Array.Empty<ParsedRunLog>();
        }

        return Directory.EnumerateFiles(logsDirectory, "*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Clamp(maxCount, 1, 500))
            .Select(TryParseFile)
            .Where(item => item is not null)
            .Cast<ParsedRunLog>()
            .ToList();
    }

    public bool TryDeleteRunFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var parsed = TryParseFile(filePath);
            if (parsed is not null)
            {
                foreach (var step in parsed.Steps)
                {
                    TryDeleteFileIfExists(step.ScreenshotPath);
                }
            }

            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int DeleteAllRunFiles(string logsDirectory)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(logsDirectory, "*.jsonl").ToList())
        {
            if (TryDeleteRunFile(path))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private static void TryDeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private static RunLogStep? ParseStep(JsonElement root)
    {
        if (!root.TryGetProperty("StepIndex", out var stepIndexProp) ||
            stepIndexProp.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return new RunLogStep
        {
            StepIndex = stepIndexProp.GetInt32(),
            Timestamp = ReadTimestamp(root) ?? DateTimeOffset.UtcNow,
            TriggerSource = ReadString(root, "TriggerSource"),
            LlmRawOutput = ReadString(root, "LlmRawOutput"),
            ObservationSummaryJson = ReadString(root, "ObservationSummaryJson"),
            ScreenshotPath = ReadString(root, "ScreenshotPath"),
            WindowsSummaryJson = ReadString(root, "WindowsSummaryJson"),
            UiTreeSummaryJson = ReadString(root, "UiTreeSummaryJson"),
            ParsedDecisionJson = ReadString(root, "ParsedDecisionJson"),
            GateDecisionJson = ReadString(root, "GateDecisionJson"),
            ActionResultJson = ReadString(root, "ActionResultJson")
        };
    }

    private static string InferFinalStatus(IReadOnlyList<RunLogStep> steps)
    {
        var last = steps[^1];
        if (!last.ActionSuccess)
        {
            if (last.ResultMessage.StartsWith("Kullanici islemi reddetti", StringComparison.Ordinal))
            {
                return "Onay reddedildi";
            }

            if (last.ResultMessage.StartsWith("ActionGate engelledi", StringComparison.Ordinal))
            {
                return "Gate engeli";
            }

            return "Son adim basarisiz";
        }

        if (last.ActionName.Equals("respond", StringComparison.OrdinalIgnoreCase) ||
            last.ActionName.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return "Tamamlandi";
        }

        return steps.Count >= 5 ? "Adim limiti?" : "Devam ediyor olabilir";
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("Timestamp", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }
}
