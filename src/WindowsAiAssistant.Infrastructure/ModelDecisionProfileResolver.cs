namespace WindowsAiAssistant.Infrastructure;

public sealed class ModelDecisionProfileResolver
{
    public bool TryResolveActiveProfile(
        ModelDecisionSettings settings,
        out ModelDecisionProfile? profile,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(settings);

        profile = null;

        if (!settings.Enabled)
        {
            reason = "feature-disabled";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveProfile))
        {
            reason = "missing-active-profile";
            return false;
        }

        var profiles = settings.Profiles ?? [];
        profile = profiles.FirstOrDefault(candidate =>
            candidate.Id.Equals(settings.ActiveProfile, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            reason = "unknown-active-profile";
            return false;
        }

        if (!profile.IsEnabled || profile.Kind == ModelProviderKind.Disabled)
        {
            reason = "profile-disabled";
            profile = null;
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
