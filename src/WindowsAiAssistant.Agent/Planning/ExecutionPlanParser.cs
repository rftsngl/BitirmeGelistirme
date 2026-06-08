using System.Text.Json;

namespace WindowsAiAssistant.Agent.Planning;

public sealed class ExecutionPlanParser
{
    public ExecutionPlanParseResult Parse(string llmRawOutput)
    {
        if (string.IsNullOrWhiteSpace(llmRawOutput))
        {
            return ExecutionPlanParseResult.Fail("Plan cevabi bos.");
        }

        var jsonText = DecisionJsonNormalizer.ExtractJsonObject(llmRawOutput);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return ExecutionPlanParseResult.Fail("Plan JSON bulunamadi.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonText);
        }
        catch (JsonException ex)
        {
            return ExecutionPlanParseResult.Fail($"Plan JSON gecersiz: {ex.Message}");
        }

        using (document)
        {
            return ParseRoot(document.RootElement);
        }
    }

    private static ExecutionPlanParseResult ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ExecutionPlanParseResult.Fail("Plan tek bir JSON nesnesi olmali.");
        }

        var goalType = ReadString(root, "goalType") ?? "desktop_multi";
        if (string.Equals(goalType, "conversation", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionPlanParseResult.Fail("conversation plani yurutme dongusune eklenmez.");
        }

        var domain = ReadString(root, "domain") ?? "generic_desktop";
        var skill = ReadString(root, "skill") ?? string.Empty;
        var summary = ReadString(root, "summary") ?? string.Empty;
        var preconditions = ReadStringArray(root, "preconditions");
        var antiPatterns = ReadStringArray(root, "antiPatterns");
        var estimatedSteps = ReadInt(root, "estimatedSteps") ?? 0;

        var steps = new List<ExecutionPlanStep>();
        if (root.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array)
        {
            var order = 1;
            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                if (stepElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var intent = ReadString(stepElement, "intent");
                if (string.IsNullOrWhiteSpace(intent))
                {
                    continue;
                }

                steps.Add(new ExecutionPlanStep
                {
                    Order = ReadInt(stepElement, "order") ?? order,
                    Intent = intent.Trim(),
                    PreferredActions = ReadStringArray(stepElement, "preferredActions"),
                    SuccessCheck = ReadString(stepElement, "successCheck") ?? string.Empty
                });
                order++;
            }
        }

        if (steps.Count == 0)
        {
            return ExecutionPlanParseResult.Fail("Plan en az bir adim icermeli.");
        }

        return ExecutionPlanParseResult.Ok(new ExecutionPlan
        {
            GoalType = goalType.Trim(),
            Domain = domain.Trim(),
            Skill = skill.Trim(),
            Summary = summary.Trim(),
            Preconditions = preconditions,
            Steps = steps,
            AntiPatterns = antiPatterns,
            EstimatedSteps = estimatedSteps > 0 ? estimatedSteps : steps.Count
        });
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }
}
