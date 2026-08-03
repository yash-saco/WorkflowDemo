namespace WorkflowDemo.Core;

/// <summary>
/// An immutable description of a workflow: its states and the transitions between them.
/// Build one with <see cref="WorkflowBuilder"/>.
/// </summary>
public sealed class WorkflowDefinition
{
    public string Id { get; }
    public string InitialState { get; }
    public IReadOnlySet<string> States { get; }
    public IReadOnlySet<string> FinalStates { get; }
    public IReadOnlyList<Transition> Transitions { get; }

    internal WorkflowDefinition(
        string id,
        string initialState,
        IReadOnlySet<string> states,
        IReadOnlySet<string> finalStates,
        IReadOnlyList<Transition> transitions)
    {
        Id = id;
        InitialState = initialState;
        States = states;
        FinalStates = finalStates;
        Transitions = transitions;
    }

    public Transition? FindTransition(string fromState, string trigger) =>
        Transitions.FirstOrDefault(t =>
            t.FromState.Equals(fromState, StringComparison.OrdinalIgnoreCase) &&
            t.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));

    /// <summary>Triggers that are valid from the given state (before guard evaluation).</summary>
    public IEnumerable<string> PermittedTriggers(string fromState) =>
        Transitions
            .Where(t => t.FromState.Equals(fromState, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Trigger);
}

/// <summary>A single edge in the workflow graph.</summary>
public sealed record Transition(
    string FromState,
    string Trigger,
    string ToState,
    Func<WorkflowContext, bool>? Guard = null,
    Action<WorkflowContext>? OnTransition = null);

/// <summary>
/// Mutable data bag flowing through guards and transition actions.
/// Persisted with the instance so long-running workflows keep their data.
/// </summary>
public sealed class WorkflowContext
{
    public Dictionary<string, string> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => Data.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => Data[key] = value;
}

/// <summary>Fluent builder for <see cref="WorkflowDefinition"/>.</summary>
public sealed class WorkflowBuilder
{
    private readonly string _id;
    private string? _initialState;
    private readonly HashSet<string> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _finalStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Transition> _transitions = new();

    public WorkflowBuilder(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Definition id is required.", nameof(id));
        _id = id;
    }

    public WorkflowBuilder StartAt(string state)
    {
        _initialState = state;
        _states.Add(state);
        return this;
    }

    public WorkflowBuilder State(string state)
    {
        _states.Add(state);
        return this;
    }

    public WorkflowBuilder FinalState(string state)
    {
        _states.Add(state);
        _finalStates.Add(state);
        return this;
    }

    public WorkflowBuilder Permit(
        string fromState,
        string trigger,
        string toState,
        Func<WorkflowContext, bool>? guard = null,
        Action<WorkflowContext>? onTransition = null)
    {
        _states.Add(fromState);
        _states.Add(toState);
        if (_transitions.Any(t =>
                t.FromState.Equals(fromState, StringComparison.OrdinalIgnoreCase) &&
                t.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Definition '{_id}': duplicate transition '{trigger}' from state '{fromState}'.");
        _transitions.Add(new Transition(fromState, trigger, toState, guard, onTransition));
        return this;
    }

    public WorkflowDefinition Build()
    {
        if (_initialState is null)
            throw new InvalidOperationException($"Definition '{_id}': StartAt(...) was never called.");
        if (_finalStates.Contains(_initialState))
            throw new InvalidOperationException(
                $"Definition '{_id}': initial state '{_initialState}' must not be a final state.");
        foreach (var t in _transitions.Where(t => _finalStates.Contains(t.FromState)))
            throw new InvalidOperationException(
                $"Definition '{_id}': final state '{t.FromState}' must not have outgoing transitions.");
        // Defensive copies so later builder calls cannot mutate the built definition.
        return new WorkflowDefinition(
            _id,
            _initialState,
            new HashSet<string>(_states, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_finalStates, StringComparer.OrdinalIgnoreCase),
            _transitions.ToList());
    }
}
