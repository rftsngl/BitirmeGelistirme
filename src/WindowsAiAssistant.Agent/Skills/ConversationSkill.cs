using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal sealed class ConversationSkill : IWorkflowSkill
{
    public WorkflowSkillDomain Domain => WorkflowSkillDomain.Conversation;

    public bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        if (plan is not null &&
            plan.GoalType.Contains("conversation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = FastPathGuard.Normalize(userGoal);
        return normalized.Length <= 80 &&
               FastPathGuard.MatchesAny(normalized, "merhaba", "selam", "nasilsin", "nasılsın", "tesekkur", "teşekkür", "naber") &&
               !FastPathGuard.MatchesAny(normalized, "word", "belge", "ac", "aç", "yaz", "tikla", "tıkla");
    }

    public void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = observation;
        _ = plan;
        builder.AppendLine("SKILL — CONVERSATION (sub-agent focus):");
        builder.AppendLine("- No desktop actions; decisionType=complete action=respond in ONE step.");
        builder.AppendLine("- parameters.message in Turkish.");
        builder.AppendLine();
    }
}
