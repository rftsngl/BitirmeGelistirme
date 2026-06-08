using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Integrations;

public static class ComAllowlistValidator
{
    public static bool IsAllowed(string progId, string method, IReadOnlyList<ComAllowedOperation> allowedOperations)
    {
        if (string.IsNullOrWhiteSpace(progId) || string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        if (allowedOperations.Count == 0)
        {
            return false;
        }

        var entry = allowedOperations.FirstOrDefault(operation =>
            operation.ProgId.Equals(progId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return false;
        }

        if (entry.Methods.Length == 0)
        {
            return true;
        }

        return entry.Methods.Any(candidate =>
            candidate.Equals(method.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
