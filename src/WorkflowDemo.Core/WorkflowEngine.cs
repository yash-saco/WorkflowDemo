namespace WorkflowDemo.Core;

/// <summary>
/// Stateless engine: applies triggers to instances according to a definition.
/// Persistence is the caller's concern, which keeps the core free of I/O.
/// </summary>
public sealed class WorkflowEngine
{
    private readonly IReadOnlyDictionary<string, WorkflowDefinition> _definitions;

    public WorkflowEngine(IEnumerable<WorkflowDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<WorkflowDefinition> Definitions => _definitions.Values;

    public WorkflowDefinition GetDefinition(string definitionId) =>
        _definitions.TryGetValue(definitionId, out var d)
            ? d
            : throw new WorkflowException($"Unknown workflow definition '{definitionId}'.");

    public WorkflowInstance Start(string definitionId, Dictionary<string, string>? initialData = null)
    {
        var def = GetDefinition(definitionId);
        var instance = new WorkflowInstance
        {
            DefinitionId = def.Id,
            CurrentState = def.InitialState,
        };
        if (initialData is not null)
            foreach (var (k, v) in initialData)
                instance.Context.Set(k, v);
        return instance;
    }

    /// <summary>
    /// Applies <paramref name="trigger"/> to the instance.
    /// Throws <see cref="WorkflowException"/> if the trigger is invalid,
    /// the guard rejects it, or the instance is already completed.
    /// </summary>
    public WorkflowInstance Fire(WorkflowInstance instance, string trigger)
    {
        if (instance.IsCompleted)
            throw new WorkflowException(
                $"Instance {instance.Id} is completed (state '{instance.CurrentState}'); no further triggers accepted.");

        var def = GetDefinition(instance.DefinitionId);
        var transition = def.FindTransition(instance.CurrentState, trigger);
        if (transition is null)
        {
            var permitted = string.Join(", ", def.PermittedTriggers(instance.CurrentState));
            throw new WorkflowException(
                $"Trigger '{trigger}' is not valid from state '{instance.CurrentState}'. " +
                $"Permitted: [{permitted}].");
        }

        if (transition.Guard is not null && !transition.Guard(instance.Context))
            throw new WorkflowException(
                $"Guard rejected trigger '{trigger}' from state '{instance.CurrentState}'.");

        transition.OnTransition?.Invoke(instance.Context);

        instance.History.Add(new TransitionRecord(
            instance.CurrentState, trigger, transition.ToState, DateTimeOffset.UtcNow));
        instance.CurrentState = transition.ToState;
        instance.IsCompleted = def.FinalStates.Contains(transition.ToState);
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        return instance;
    }

    public IEnumerable<string> PermittedTriggers(WorkflowInstance instance) =>
        instance.IsCompleted
            ? Enumerable.Empty<string>()
            : GetDefinition(instance.DefinitionId).PermittedTriggers(instance.CurrentState);
}

public sealed class WorkflowException : Exception
{
    public WorkflowException(string message) : base(message) { }
}
