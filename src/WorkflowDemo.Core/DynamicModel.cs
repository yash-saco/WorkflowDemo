namespace WorkflowDemo.Core;

/// <summary>
/// Data-driven workflow template, authored by non-technical users through the designer UI
/// and stored as JSON. A template holds one or more rules; at submission time the first
/// matching rule (by priority) decides which approval chain the request follows.
/// </summary>
public sealed class WorkflowTemplate
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<ApprovalRule> Rules { get; set; } = new();
}

/// <summary>One routing rule: "if the requester matches <see cref="Condition"/>, run these steps".</summary>
public sealed class ApprovalRule
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Lower value wins when several rules match.</summary>
    public int Priority { get; set; }
    public RuleCondition Condition { get; set; } = RuleCondition.Any();
    public List<WorkflowStep> Steps { get; set; } = new();
}

/// <summary>
/// Declarative condition evaluated against requester attributes (e.g. role, department).
/// Deliberately not a scripting language: designers pick field/operator/values from a form.
/// </summary>
public sealed class RuleCondition
{
    public string Field { get; set; } = "role";
    /// <summary>"any" (always matches), "equals", or "in".</summary>
    public string Operator { get; set; } = "any";
    public List<string> Values { get; set; } = new();

    public static RuleCondition Any() => new();

    public static RuleCondition RoleIn(params string[] roles) =>
        new() { Field = "role", Operator = "in", Values = roles.ToList() };

    public bool Matches(IReadOnlyDictionary<string, string> attributes)
    {
        if (string.Equals(Operator, "any", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!attributes.TryGetValue(Field, out var actual))
            return false;
        return Operator.ToLowerInvariant() switch
        {
            "equals" => Values.Count > 0 &&
                        string.Equals(Values[0], actual, StringComparison.OrdinalIgnoreCase),
            "in" => Values.Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }
}

/// <summary>
/// A single step in a rule's chain. Extensible by <see cref="Type"/>:
/// "approval" steps wait for a human decision; any other type (e.g. "notification")
/// is treated as an automatic step the runtime advances through immediately.
/// </summary>
public sealed class WorkflowStep
{
    public string Type { get; set; } = StepTypes.Approval;
    public string? Name { get; set; }
    /// <summary>Required when <see cref="Type"/> is "approval".</summary>
    public ApproverSpec? Approver { get; set; }
}

public static class StepTypes
{
    public const string Approval = "approval";
    public const string Notification = "notification";

    public static bool IsApproval(string type) =>
        string.Equals(type, Approval, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Who must approve an approval step.</summary>
public sealed class ApproverSpec
{
    /// <summary>"hierarchy" (N+Level up the manager chain), "role", or "user".</summary>
    public string Mode { get; set; } = ApproverModes.Hierarchy;
    /// <summary>For hierarchy mode: 1 = direct manager (N+1), 2 = manager's manager (N+2), ...</summary>
    public int Level { get; set; } = 1;
    public string? Role { get; set; }
    public string? UserId { get; set; }

    public string Describe() => Mode.ToLowerInvariant() switch
    {
        ApproverModes.Hierarchy => $"N+{Level}",
        ApproverModes.Role => $"role: {Role}",
        ApproverModes.User => $"user: {UserId}",
        _ => Mode,
    };
}

public static class ApproverModes
{
    public const string Hierarchy = "hierarchy";
    public const string Role = "role";
    public const string User = "user";
}
