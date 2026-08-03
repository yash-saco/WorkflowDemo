namespace WorkflowDemo.Core;

/// <summary>
/// Turns a data-driven rule into an executable <see cref="WorkflowDefinition"/>.
/// Because compilation is total and validated, designer-authored rules can never
/// produce a structurally broken state machine.
///
/// Shape per rule:
///   Draft --submit--> Step1 --approve/continue--> ... --> Approved (final)
///   any approval step --reject--> Rejected --revise--> Draft
/// </summary>
public static class TemplateCompiler
{
    public const string DraftState = "Draft";
    public const string ApprovedState = "Approved";
    public const string RejectedState = "Rejected";

    public const string SubmitTrigger = "submit";
    public const string ApproveTrigger = "approve";
    public const string RejectTrigger = "reject";
    public const string ContinueTrigger = "continue";
    public const string ReviseTrigger = "revise";

    public static string DefinitionId(string templateId, string ruleId) => $"{templateId}::{ruleId}";

    public static string StepState(int oneBasedIndex) => $"Step{oneBasedIndex}";

    /// <summary>Parses "Step3" → 3, or null for non-step states.</summary>
    public static int? StepIndexOf(string state) =>
        state.StartsWith("Step", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(state.AsSpan(4), out var i) && i >= 1
            ? i
            : null;

    /// <summary>First matching rule by ascending priority, or null.</summary>
    public static ApprovalRule? SelectRule(
        WorkflowTemplate template, IReadOnlyDictionary<string, string> requesterAttributes) =>
        template.Rules
            .OrderBy(r => r.Priority)
            .FirstOrDefault(r => r.Condition.Matches(requesterAttributes));

    public static void Validate(WorkflowTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Id))
            throw new WorkflowException("Template id is required.");
        if (template.Rules.Count == 0)
            throw new WorkflowException($"Template '{template.Name}': at least one rule is required.");
        var duplicate = template.Rules
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new WorkflowException(
                $"Template '{template.Name}': duplicate rule id '{duplicate.Key}'.");
        foreach (var rule in template.Rules)
            Compile(template, rule); // compile performs per-rule validation
    }

    public static WorkflowDefinition Compile(WorkflowTemplate template, ApprovalRule rule)
    {
        ValidateRule(template, rule);

        var builder = new WorkflowBuilder(DefinitionId(template.Id, rule.Id)).StartAt(DraftState);

        var previousState = DraftState;
        var previousTrigger = SubmitTrigger;
        var anyApproval = false;

        for (var i = 0; i < rule.Steps.Count; i++)
        {
            var state = StepState(i + 1);
            builder.Permit(previousState, previousTrigger, state);

            if (StepTypes.IsApproval(rule.Steps[i].Type))
            {
                anyApproval = true;
                builder.Permit(state, RejectTrigger, RejectedState);
                previousTrigger = ApproveTrigger;
            }
            else
            {
                previousTrigger = ContinueTrigger;
            }
            previousState = state;
        }

        builder.Permit(previousState, previousTrigger, ApprovedState);
        if (anyApproval)
            builder.Permit(RejectedState, ReviseTrigger, DraftState);
        builder.FinalState(ApprovedState);

        return builder.Build();
    }

    private static void ValidateRule(WorkflowTemplate template, ApprovalRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
            throw new WorkflowException($"Template '{template.Name}': every rule needs an id.");
        if (rule.Steps.Count == 0)
            throw new WorkflowException(
                $"Rule '{rule.Name}': at least one step is required.");

        for (var i = 0; i < rule.Steps.Count; i++)
        {
            var step = rule.Steps[i];
            if (!StepTypes.IsApproval(step.Type))
                continue;
            var spec = step.Approver
                ?? throw new WorkflowException(
                    $"Rule '{rule.Name}', step {i + 1}: approval steps need an approver.");
            switch (spec.Mode.ToLowerInvariant())
            {
                case ApproverModes.Hierarchy when spec.Level < 1:
                    throw new WorkflowException(
                        $"Rule '{rule.Name}', step {i + 1}: hierarchy level must be at least 1 (N+1).");
                case ApproverModes.Role when string.IsNullOrWhiteSpace(spec.Role):
                    throw new WorkflowException(
                        $"Rule '{rule.Name}', step {i + 1}: role-based approval needs a role.");
                case ApproverModes.User when string.IsNullOrWhiteSpace(spec.UserId):
                    throw new WorkflowException(
                        $"Rule '{rule.Name}', step {i + 1}: user-based approval needs a user.");
                case ApproverModes.Hierarchy:
                case ApproverModes.Role:
                case ApproverModes.User:
                    break;
                default:
                    throw new WorkflowException(
                        $"Rule '{rule.Name}', step {i + 1}: unknown approver mode '{spec.Mode}'.");
            }
        }
    }
}
