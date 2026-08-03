using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowDemo.Core;

namespace WorkflowDemo.Api;

/// <summary>EF Core row for a designer-authored workflow template (model stored as JSON).</summary>
public sealed class WorkflowTemplateEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Json { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TemplateService
{
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDbContext _db;

    public TemplateService(WorkflowDbContext db) => _db = db;

    public async Task<List<WorkflowTemplate>> ListAsync() =>
        (await _db.Templates.AsNoTracking().OrderBy(t => t.Name).ToListAsync())
        .Select(Deserialize)
        .ToList();

    public async Task<WorkflowTemplate?> GetAsync(string id)
    {
        var entity = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        return entity is null ? null : Deserialize(entity);
    }

    /// <summary>Validates (compiles every rule) then upserts. Throws WorkflowException on invalid templates.</summary>
    public async Task SaveAsync(WorkflowTemplate template)
    {
        TemplateCompiler.Validate(template);

        var entity = await _db.Templates.FirstOrDefaultAsync(t => t.Id == template.Id);
        if (entity is null)
        {
            entity = new WorkflowTemplateEntity { Id = template.Id };
            _db.Templates.Add(entity);
        }
        entity.Name = template.Name;
        entity.Json = JsonSerializer.Serialize(template, JsonOpts);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var entity = await _db.Templates.FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null) return false;
        _db.Templates.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    private static WorkflowTemplate Deserialize(WorkflowTemplateEntity entity) =>
        JsonSerializer.Deserialize<WorkflowTemplate>(entity.Json, JsonOpts)
        ?? throw new WorkflowException($"Template '{entity.Id}' has corrupt JSON.");

    /// <summary>Seeds demo workflows if the table is empty.</summary>
    public static void Seed(WorkflowDbContext db)
    {
        if (db.Templates.Any()) return;

        static WorkflowStep Approve(string name, ApproverSpec spec) =>
            new() { Type = StepTypes.Approval, Name = name, Approver = spec };
        static WorkflowStep Notify(string name) =>
            new() { Type = StepTypes.Notification, Name = name };
        static ApproverSpec NPlus(int level) =>
            new() { Mode = ApproverModes.Hierarchy, Level = level };
        static ApproverSpec ByRole(string role) =>
            new() { Mode = ApproverModes.Role, Role = role };
        static ApproverSpec ByUser(string userId) =>
            new() { Mode = ApproverModes.User, UserId = userId };

        var templates = new[]
        {
            // The scenario from the requirements: team members need N+1 AND N+2,
            // managers / IT directors need a single approval.
            new WorkflowTemplate
            {
                Id = "purchase-request",
                Name = "Purchase Request",
                Rules =
                {
                    new ApprovalRule
                    {
                        Id = "team-member-dual", Name = "Team member — dual approval", Priority = 1,
                        Condition = RuleCondition.RoleIn("Team Member"),
                        Steps =
                        {
                            Approve("Manager approval", NPlus(1)),
                            Approve("Senior approval", NPlus(2)),
                            Notify("Notify requester"),
                        },
                    },
                    new ApprovalRule
                    {
                        Id = "leadership-single", Name = "Manager / IT Director — single approval",
                        Priority = 2,
                        Condition = RuleCondition.RoleIn("Manager", "IT Director"),
                        Steps = { Approve("Approval", NPlus(1)) },
                    },
                },
            },

            // Simplest possible flow: one rule for everyone, one approval, one auto step.
            new WorkflowTemplate
            {
                Id = "leave-request",
                Name = "Leave Request",
                Rules =
                {
                    new ApprovalRule
                    {
                        Id = "any-employee", Name = "All employees — manager approves", Priority = 1,
                        Condition = RuleCondition.Any(),
                        Steps =
                        {
                            Approve("Manager approval", NPlus(1)),
                            Notify("HR informed"),
                        },
                    },
                },
            },

            // Demonstrates a role-based approver (any IT Director) and a
            // person-specific approver (the CEO) for privileged requesters.
            new WorkflowTemplate
            {
                Id = "it-access-request",
                Name = "IT Access Request",
                Rules =
                {
                    new ApprovalRule
                    {
                        Id = "staff", Name = "Staff — manager then IT Director", Priority = 1,
                        Condition = RuleCondition.RoleIn("Team Member", "Manager", "Finance", "HR"),
                        Steps =
                        {
                            Approve("Manager approval", NPlus(1)),
                            Approve("IT sign-off", ByRole("IT Director")),
                            Notify("Provision access"),
                        },
                    },
                    new ApprovalRule
                    {
                        Id = "it-director", Name = "IT Director — CEO approves", Priority = 2,
                        Condition = RuleCondition.RoleIn("IT Director"),
                        Steps =
                        {
                            Approve("CEO approval", ByUser("u4")), // Dana Osei
                            Notify("Provision access"),
                        },
                    },
                },
            },

            // Demonstrates a second-line functional approval (Finance) after the manager.
            new WorkflowTemplate
            {
                Id = "expense-reimbursement",
                Name = "Expense Reimbursement",
                Rules =
                {
                    new ApprovalRule
                    {
                        Id = "team-member", Name = "Team member — manager then Finance", Priority = 1,
                        Condition = RuleCondition.RoleIn("Team Member"),
                        Steps =
                        {
                            Approve("Manager approval", NPlus(1)),
                            Approve("Finance approval", ByRole("Finance")),
                            Notify("Payment scheduled"),
                        },
                    },
                    new ApprovalRule
                    {
                        Id = "others", Name = "Everyone else — manager approves", Priority = 2,
                        Condition = RuleCondition.Any(),
                        Steps =
                        {
                            Approve("Manager approval", NPlus(1)),
                            Notify("Payment scheduled"),
                        },
                    },
                },
            },
        };

        foreach (var template in templates)
        {
            TemplateCompiler.Validate(template);
            db.Templates.Add(new WorkflowTemplateEntity
            {
                Id = template.Id,
                Name = template.Name,
                Json = JsonSerializer.Serialize(template, JsonOpts),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        db.SaveChanges();
    }
}
