using System.Text;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal interface IWorkflowSkill
{
    WorkflowSkillDomain Domain { get; }

    bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan);

    void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan);
}
