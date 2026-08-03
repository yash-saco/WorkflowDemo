using Microsoft.EntityFrameworkCore;
using WorkflowDemo.Api;
using WorkflowDemo.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WorkflowDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Workflow") ?? "Data Source=workflow.db"));
builder.Services.AddSingleton<IDirectory, InMemoryDirectory>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
    db.Database.EnsureCreated();
    TemplateService.Seed(db);
}

app.UseDefaultFiles();   // serves wwwroot/index.html (the designer) at /
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

// ---- Directory ----
app.MapGet("/api/directory", (IDirectory dir) =>
    dir.All.Select(e => new { e.Id, e.Name, e.Role, e.ManagerId }));

// ---- Templates (designer CRUD) ----
app.MapGet("/api/templates", async (TemplateService templates) =>
    (await templates.ListAsync()).Select(t => new { t.Id, t.Name, ruleCount = t.Rules.Count }));

app.MapGet("/api/templates/{id}", async (string id, TemplateService templates) =>
    await templates.GetAsync(id) is { } t ? Results.Ok(t) : Results.NotFound());

app.MapPut("/api/templates/{id}", async (string id, WorkflowTemplate template, TemplateService templates) =>
{
    template.Id = id;
    try
    {
        await templates.SaveAsync(template);
        return Results.Ok(template);
    }
    catch (WorkflowException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/templates/{id}", async (string id, TemplateService templates) =>
    await templates.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

// ---- Requests (runtime) ----
app.MapPost("/api/requests", async (StartWorkflowRequest req, RequestService requests) =>
{
    try
    {
        return Results.Ok(await requests.StartAsync(req.TemplateId, req.RequesterId, req.Data));
    }
    catch (WorkflowException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/requests", async (RequestService requests) => await requests.ListAsync());

app.MapGet("/api/requests/{id:guid}", async (Guid id, RequestService requests) =>
    await requests.GetAsync(id) is { } dto ? Results.Ok(dto) : Results.NotFound());

app.MapPost("/api/requests/{id:guid}/approve", async (Guid id, ActionRequest req, RequestService requests) =>
{
    try
    {
        return Results.Ok(await requests.ApproveAsync(id, req.ActorId, req.Comment));
    }
    catch (WorkflowException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/api/requests/{id:guid}/reject", async (Guid id, ActionRequest req, RequestService requests) =>
{
    try
    {
        return Results.Ok(await requests.RejectAsync(id, req.ActorId, req.Comment));
    }
    catch (WorkflowException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/api/requests/{id:guid}/resubmit", async (Guid id, ActionRequest req, RequestService requests) =>
{
    try
    {
        return Results.Ok(await requests.ResubmitAsync(id, req.ActorId));
    }
    catch (WorkflowException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.Run();

public sealed record StartWorkflowRequest(string TemplateId, string RequesterId, Dictionary<string, string>? Data);
public sealed record ActionRequest(string ActorId, string? Comment);

public partial class Program { } // for WebApplicationFactory in tests
