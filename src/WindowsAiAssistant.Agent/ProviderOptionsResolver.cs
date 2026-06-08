using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Agent;

internal static class ProviderOptionsResolver
{
    internal static ProviderOptions ForExecutor(AgentOptions options) => options.Model;

    internal static ProviderOptions ForPlanner(AgentOptions options) =>
        string.IsNullOrWhiteSpace(options.PlannerModelOverride)
            ? options.Model
            : options.Model.WithModel(options.PlannerModelOverride);

    internal static ProviderOptions ForVerifier(AgentOptions options) =>
        string.IsNullOrWhiteSpace(options.VerifierModelOverride)
            ? options.Model
            : options.Model.WithModel(options.VerifierModelOverride);
}
