namespace WorkflowDemo.Core;

/// <summary>A running (or finished) occurrence of a workflow definition.</summary>
public sealed class WorkflowInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string DefinitionId { get; init; }
    public required string CurrentState { get; set; }
    public bool IsCompleted { get; set; }
    public WorkflowContext Context { get; init; } = new();
    public List<TransitionRecord> History { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Audit record of one applied transition.</summary>
public sealed record TransitionRecord(
    string FromState,
    string Trigger,
    string ToState,
    DateTimeOffset At);
