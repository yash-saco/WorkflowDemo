using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowDemo.Core;

namespace WorkflowDemo.Api;

/// <summary>EF Core row for a workflow instance. Context and history stored as JSON.</summary>
public sealed class WorkflowInstanceEntity
{
    public Guid Id { get; set; }
    public string DefinitionId { get; set; } = "";
    public string CurrentState { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string ContextJson { get; set; } = "{}";
    public string HistoryJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static WorkflowInstanceEntity FromDomain(WorkflowInstance i) => new()
    {
        Id = i.Id,
        DefinitionId = i.DefinitionId,
        CurrentState = i.CurrentState,
        IsCompleted = i.IsCompleted,
        ContextJson = JsonSerializer.Serialize(i.Context.Data),
        HistoryJson = JsonSerializer.Serialize(i.History),
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };

    public WorkflowInstance ToDomain() => new()
    {
        Id = Id,
        DefinitionId = DefinitionId,
        CurrentState = CurrentState,
        IsCompleted = IsCompleted,
        Context = new WorkflowContext
        {
            // Rebuild with the case-insensitive comparer: JsonSerializer.Deserialize
            // would otherwise silently produce a case-sensitive dictionary.
            Data = new Dictionary<string, string>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(ContextJson)
                    ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
        },
        History = JsonSerializer.Deserialize<List<TransitionRecord>>(HistoryJson) ?? new(),
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };

    public void UpdateFrom(WorkflowInstance i)
    {
        CurrentState = i.CurrentState;
        IsCompleted = i.IsCompleted;
        ContextJson = JsonSerializer.Serialize(i.Context.Data);
        HistoryJson = JsonSerializer.Serialize(i.History);
        UpdatedAt = i.UpdatedAt;
    }
}

public sealed class WorkflowDbContext : DbContext
{
    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options) { }

    public DbSet<WorkflowInstanceEntity> Instances => Set<WorkflowInstanceEntity>();
    public DbSet<WorkflowTemplateEntity> Templates => Set<WorkflowTemplateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowInstanceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DefinitionId);
            e.Property(x => x.CurrentState).HasMaxLength(128);
            e.Property(x => x.DefinitionId).HasMaxLength(128);
        });

        modelBuilder.Entity<WorkflowTemplateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
        });
    }
}
