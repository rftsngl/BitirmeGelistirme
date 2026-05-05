using WindowsAiAssistant.Contracts.Models;
using System.Text.RegularExpressions;

namespace WindowsAiAssistant.Infrastructure.Targeting;

internal sealed class ApplicationTargetCatalog
{
    private static readonly string[] FallbackApplications = ["notepad", "calc"];
    private static readonly IReadOnlyDictionary<string, string> FallbackAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["calculator"] = "calc"
        };

    private readonly HashSet<string> _allowedApplications;
    private readonly IReadOnlyDictionary<string, string> _appAliases;

    public ApplicationTargetCatalog(ExecutionPolicySettings? settings = null, bool useFallbackDefaults = false)
    {
        IEnumerable<string>? configuredApplications = settings?.AllowedRealApps;
        IEnumerable<string> allowedApplications = configuredApplications
            ?? (useFallbackDefaults ? FallbackApplications : Array.Empty<string>());

        _allowedApplications = allowedApplications
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _appAliases = (settings?.AppAliases ?? (useFallbackDefaults ? FallbackAliases : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(
                kvp => kvp.Key.Trim().ToLowerInvariant(),
                kvp => kvp.Value.Trim().ToLowerInvariant(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<(string MatchedText, string CanonicalValue)> FindMentionMatches(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetCandidates())
        {
            if (!ContainsWholeWord(input, candidate) ||
                !TryCanonicalize(candidate, out var canonicalValue) ||
                !seen.Add(canonicalValue))
            {
                continue;
            }

            yield return (candidate, canonicalValue);
        }
    }

    public bool TryCanonicalize(string? value, out string canonicalValue)
    {
        canonicalValue = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Trim('"', '\'').ToLowerInvariant();
        if (_appAliases.TryGetValue(normalized, out var alias))
        {
            normalized = alias;
        }

        if (!_allowedApplications.Contains(normalized))
        {
            return false;
        }

        canonicalValue = normalized;
        return true;
    }

    public bool TryCanonicalizeSingleValue(string? value, out string canonicalValue)
    {
        canonicalValue = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Trim('"', '\'');
        if (normalized.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        return TryCanonicalize(normalized, out canonicalValue);
    }

    private IEnumerable<string> GetCandidates()
    {
        return _appAliases.Keys
            .Concat(_allowedApplications)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => candidate.Length)
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsWholeWord(string input, string candidate)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(candidate)}(?![A-Za-z0-9])";
        return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
