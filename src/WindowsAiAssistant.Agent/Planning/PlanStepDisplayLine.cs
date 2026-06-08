namespace WindowsAiAssistant.Agent.Planning;

public enum PlanStepMarker
{
    Pending,
    Active,
    Completed
}

public sealed class PlanStepDisplayLine
{
    public int Order { get; init; }
    public string Intent { get; init; } = string.Empty;
    public PlanStepMarker Marker { get; init; }

    public bool IsActive => Marker == PlanStepMarker.Active;
    public bool IsCompleted => Marker == PlanStepMarker.Completed;
    public bool IsPending => Marker == PlanStepMarker.Pending;

    public string StepLabel => $"{Order}. {Intent}";
}
