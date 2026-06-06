namespace WindowsAiAssistant.Runtime.Actions;

internal static class ActionParameterReader
{
    public static string? GetTargetOrParameter(AgentAction action, params string[] parameterKeys)
    {
        foreach (var key in parameterKeys)
        {
            if (action.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(action.Target) ? null : action.Target.Trim();
    }

    public static bool TryGetInt(AgentAction action, string key, out int value)
    {
        value = 0;
        if (!action.Parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return int.TryParse(raw.Trim(), out value);
    }
}
