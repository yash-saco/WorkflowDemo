using Microsoft.EntityFrameworkCore;
using WorkflowDemo.Core;

namespace WorkflowDemo.Api;

/// <summary>
/// Orchestrates request lifecycle over the dynamic templates:
/// rule selection, approver resolution (N+1/N+2/role/user), authorization of the actor,
/// and auto-advancing through non-approval steps.
/// </summary>
public sealed class RequestService
{
    private readonly WorkflowDbContext _db;
    private readonly IDirectory _directory;
    private readonly TemplateService _templates;

    // Context keys
    private const string KeyRequesterId = "requesterId";
    private const string KeyTemplateId = "templateId";
    private const string KeyRuleId = "ruleId";
    private static string KeyApproverId(int step) => $"step{step}:approverId";
    private static string KeyApproverRole(int step) => $"step{step}:approverRole";
    private static string KeyDecision(int step) => $"step{step}:decision";
    private static string KeyNote(int step) => $"step{step}:note";

    public RequestService(WorkflowDbContext db, IDirectory directory, TemplateService templates)
    {
        _db = db;
        _directory = directory;
        _templates = templates;
    }

    public async Task<object> StartAsync(string templateId, string requesterId, Dictionary<string, string>? data)
    {
        var template = await _templates.GetAsync(templateId)
            ?? throw new WorkflowException($"Unknown template '{templateId}'.");
        var requester = _directory.Get(requesterId)
            ?? throw new WorkflowException($"Unknown employee '{requesterId}'.");

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["role"] = requester.Role,
        };
        var rule = TemplateCompiler.SelectRule(template, attributes)
            ?? throw new WorkflowException(
                $"No rule in '{template.Name}' matches role '{requester.Role}'. Add a rule for that role first.");

        var definition = TemplateCompiler.Compile(template, rule);
        var engine = new WorkflowEngine(new[] { definition });
        var instance = engine.Start(definition.Id, data);

        instance.Context.Set(KeyRequesterId, requester.Id);
        instance.Context.Set(KeyTemplateId, template.Id);
        instance.Context.Set(KeyRuleId, rule.Id);
        ResolveApprovers(instance, rule, requester);

        engine.Fire(instance, TemplateCompiler.SubmitTrigger);
        AutoAdvance(engine, rule, instance);

        _db.Instances.Add(WorkflowInstanceEntity.FromDomain(instance));
        await _db.SaveChangesAsync();
        return await ToDtoAsync(instance);
    }

    public Task<object> ApproveAsync(Guid id, string actorId, string? comment) =>
        DecideAsync(id, actorId, comment, approve: true);

    public Task<object> RejectAsync(Guid id, string actorId, string? comment) =>
        DecideAsync(id, actorId, comment, approve: false);

    private async Task<object> DecideAsync(Guid id, string actorId, string? comment, bool approve)
    {
        var (entity, instance, rule, engine) = await LoadAsync(id);
        var actor = _directory.Get(actorId)
            ?? throw new WorkflowException($"Unknown employee '{actorId}'.");

        var stepIndex = TemplateCompiler.StepIndexOf(instance.CurrentState)
            ?? throw new WorkflowException(
                $"Request is in state '{instance.CurrentState}'; there is nothing to approve.");
        if (stepIndex > rule.Steps.Count)
            throw new WorkflowException(
                $"This request is waiting at step {stepIndex}, but the rule now has only " +
                $"{rule.Steps.Count} step(s) — the template was edited after submission. " +
                "Recreate the request.");
        var step = rule.Steps[stepIndex - 1];
        if (!StepTypes.IsApproval(step.Type))
            throw new WorkflowException($"Step {stepIndex} is not an approval step.");

        EnsureActorMayApprove(instance, stepIndex, actor);

        instance.Context.Set(KeyDecision(stepIndex),
            $"{(approve ? "Approved" : "Rejected")} by {actor.Name}" +
            (string.IsNullOrWhiteSpace(comment) ? "" : $": {comment}"));

        engine.Fire(instance,
            approve ? TemplateCompiler.ApproveTrigger : TemplateCompiler.RejectTrigger);
        if (approve)
            AutoAdvance(engine, rule, instance);

        entity.UpdateFrom(instance);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(instance);
    }

    public async Task<object> ResubmitAsync(Guid id, string actorId)
    {
        var (entity, instance, rule, engine) = await LoadAsync(id);
        if (!string.Equals(instance.Context.Get(KeyRequesterId), actorId, StringComparison.OrdinalIgnoreCase))
            throw new WorkflowException("Only the requester can resubmit.");

        // Clear decisions/notes from the previous round so the audit trail isn't misleading
        // (the transition History still records the earlier rejection).
        foreach (var key in instance.Context.Data.Keys
                     .Where(k => k.EndsWith(":decision", StringComparison.OrdinalIgnoreCase)
                              || k.EndsWith(":note", StringComparison.OrdinalIgnoreCase))
                     .ToList())
            instance.Context.Data.Remove(key);

        engine.Fire(instance, TemplateCompiler.ReviseTrigger);
        engine.Fire(instance, TemplateCompiler.SubmitTrigger);
        AutoAdvance(engine, rule, instance);

        entity.UpdateFrom(instance);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(instance);
    }

    public async Task<object?> GetAsync(Guid id)
    {
        var entity = await _db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        return entity is null ? null : await ToDtoAsync(entity.ToDomain());
    }

    public async Task<List<object>> ListAsync()
    {
        var entities = await _db.Instances.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync();
        var result = new List<object>(entities.Count);
        foreach (var e in entities)
            result.Add(await ToDtoAsync(e.ToDomain()));
        return result;
    }

    // --- internals ---

    private async Task<(WorkflowInstanceEntity, WorkflowInstance, ApprovalRule, WorkflowEngine)> LoadAsync(Guid id)
    {
        var entity = await _db.Instances.FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new WorkflowException($"Unknown request '{id}'.");
        var instance = entity.ToDomain();

        var templateId = instance.Context.Get(KeyTemplateId)
            ?? throw new WorkflowException("Request is missing its template reference.");
        var ruleId = instance.Context.Get(KeyRuleId)
            ?? throw new WorkflowException("Request is missing its rule reference.");

        var template = await _templates.GetAsync(templateId)
            ?? throw new WorkflowException(
                $"Template '{templateId}' no longer exists; this request cannot proceed.");
        var rule = template.Rules.FirstOrDefault(r =>
                string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkflowException(
                $"Rule '{ruleId}' was removed from template '{templateId}'; this request cannot proceed.");

        var definition = TemplateCompiler.Compile(template, rule);
        return (entity, instance, rule, new WorkflowEngine(new[] { definition }));
    }

    private void ResolveApprovers(WorkflowInstance instance, ApprovalRule rule, Employee requester)
    {
        for (var i = 0; i < rule.Steps.Count; i++)
        {
            var step = rule.Steps[i];
            if (!StepTypes.IsApproval(step.Type)) continue;
            var spec = step.Approver!; // validated by the compiler
            switch (spec.Mode.ToLowerInvariant())
            {
                case ApproverModes.Hierarchy:
                    var manager = _directory.ManagerOf(requester.Id, spec.Level)
                        ?? throw new WorkflowException(
                            $"{requester.Name} has no N+{spec.Level} manager; " +
                            "this rule cannot apply to them.");
                    if (string.Equals(manager.Id, requester.Id, StringComparison.OrdinalIgnoreCase))
                        throw new WorkflowException("Requester cannot be their own approver.");
                    instance.Context.Set(KeyApproverId(i + 1), manager.Id);
                    break;
                case ApproverModes.User:
                    instance.Context.Set(KeyApproverId(i + 1), spec.UserId!);
                    break;
                case ApproverModes.Role:
                    instance.Context.Set(KeyApproverRole(i + 1), spec.Role!);
                    break;
            }
        }
    }

    private void EnsureActorMayApprove(WorkflowInstance instance, int stepIndex, Employee actor)
    {
        // Applies to all approver modes: a role/user-mode approver could otherwise
        // approve a request they submitted themselves.
        if (string.Equals(instance.Context.Get(KeyRequesterId), actor.Id, StringComparison.OrdinalIgnoreCase))
            throw new WorkflowException("Requesters cannot approve or reject their own request.");

        var expectedId = instance.Context.Get(KeyApproverId(stepIndex));
        var expectedRole = instance.Context.Get(KeyApproverRole(stepIndex));

        if (expectedId is null && expectedRole is null)
            throw new WorkflowException(
                $"No approver was recorded for step {stepIndex} — the template was likely " +
                "edited after this request was submitted. Recreate the request.");

        if (expectedId is not null &&
            string.Equals(expectedId, actor.Id, StringComparison.OrdinalIgnoreCase))
            return;
        if (expectedRole is not null &&
            string.Equals(expectedRole, actor.Role, StringComparison.OrdinalIgnoreCase))
            return;

        var expected = expectedId is not null
            ? _directory.Get(expectedId)?.Name ?? expectedId
            : $"anyone with role '{expectedRole}'";
        throw new WorkflowException(
            $"{actor.Name} is not the pending approver for this step (expected: {expected}).");
    }

    /// <summary>Runs through consecutive non-approval steps (notifications etc.) automatically.</summary>
    private static void AutoAdvance(WorkflowEngine engine, ApprovalRule rule, WorkflowInstance instance)
    {
        while (!instance.IsCompleted)
        {
            var stepIndex = TemplateCompiler.StepIndexOf(instance.CurrentState);
            if (stepIndex is null) break;
            var step = rule.Steps[stepIndex.Value - 1];
            if (StepTypes.IsApproval(step.Type)) break;

            // Extension point: dispatch by step.Type (send email, call webhook, ...).
            instance.Context.Set(KeyNote(stepIndex.Value),
                $"{step.Type} '{step.Name ?? "step"}' executed automatically.");
            engine.Fire(instance, TemplateCompiler.ContinueTrigger);
        }
    }

    private async Task<object> ToDtoAsync(WorkflowInstance instance)
    {
        var requester = _directory.Get(instance.Context.Get(KeyRequesterId) ?? "");
        var templateId = instance.Context.Get(KeyTemplateId);
        var ruleId = instance.Context.Get(KeyRuleId);

        string? stepName = null;
        object? pendingApprover = null;
        var stepIndex = TemplateCompiler.StepIndexOf(instance.CurrentState);
        if (stepIndex is not null && templateId is not null && ruleId is not null)
        {
            var template = await _templates.GetAsync(templateId);
            var rule = template?.Rules.FirstOrDefault(r =>
                string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            if (rule is not null && stepIndex.Value <= rule.Steps.Count)
            {
                var step = rule.Steps[stepIndex.Value - 1];
                stepName = step.Name ?? $"Step {stepIndex.Value}";
                var approverId = instance.Context.Get(KeyApproverId(stepIndex.Value));
                var approverRole = instance.Context.Get(KeyApproverRole(stepIndex.Value));
                if (approverId is not null)
                {
                    var approver = _directory.Get(approverId);
                    pendingApprover = new { id = approverId, name = approver?.Name ?? approverId };
                }
                else if (approverRole is not null)
                {
                    pendingApprover = new { role = approverRole };
                }
            }
        }

        return new
        {
            id = instance.Id,
            templateId,
            ruleId,
            requester = requester is null ? null : new { requester.Id, requester.Name, requester.Role },
            state = instance.CurrentState,
            stepName,
            pendingApprover,
            isCompleted = instance.IsCompleted,
            data = instance.Context.Data,
            history = instance.History,
            createdAt = instance.CreatedAt,
            updatedAt = instance.UpdatedAt,
        };
    }
}
