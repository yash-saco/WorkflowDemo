using WorkflowDemo.Core;
using Xunit;

namespace WorkflowDemo.Tests;

public class WorkflowEngineTests
{
    private static WorkflowDefinition ApprovalDefinition() =>
        new WorkflowBuilder("approval")
            .StartAt("Draft")
            .Permit("Draft", "submit", "Submitted")
            .Permit("Submitted", "start_review", "InReview")
            .Permit("InReview", "approve", "Approved",
                guard: ctx => !string.IsNullOrWhiteSpace(ctx.Get("reviewer")))
            .Permit("InReview", "reject", "Rejected")
            .Permit("Rejected", "revise", "Draft")
            .FinalState("Approved")
            .Build();

    private static WorkflowEngine Engine() => new(new[] { ApprovalDefinition() });

    [Fact]
    public void Start_uses_initial_state()
    {
        var instance = Engine().Start("approval");
        Assert.Equal("Draft", instance.CurrentState);
        Assert.False(instance.IsCompleted);
        Assert.Empty(instance.History);
    }

    [Fact]
    public void Valid_trigger_moves_state_and_records_history()
    {
        var engine = Engine();
        var instance = engine.Start("approval");

        engine.Fire(instance, "submit");

        Assert.Equal("Submitted", instance.CurrentState);
        var record = Assert.Single(instance.History);
        Assert.Equal(("Draft", "submit", "Submitted"), (record.FromState, record.Trigger, record.ToState));
    }

    [Fact]
    public void Invalid_trigger_throws_and_lists_permitted()
    {
        var engine = Engine();
        var instance = engine.Start("approval");

        var ex = Assert.Throws<WorkflowException>(() => engine.Fire(instance, "approve"));
        Assert.Contains("submit", ex.Message);
        Assert.Equal("Draft", instance.CurrentState); // unchanged
    }

    [Fact]
    public void Guard_blocks_transition_until_condition_met()
    {
        var engine = Engine();
        var instance = engine.Start("approval");
        engine.Fire(instance, "submit");
        engine.Fire(instance, "start_review");

        Assert.Throws<WorkflowException>(() => engine.Fire(instance, "approve"));

        instance.Context.Set("reviewer", "yash");
        engine.Fire(instance, "approve");
        Assert.Equal("Approved", instance.CurrentState);
    }

    [Fact]
    public void Final_state_completes_instance_and_rejects_further_triggers()
    {
        var engine = Engine();
        var instance = engine.Start("approval");
        instance.Context.Set("reviewer", "yash");
        engine.Fire(instance, "submit");
        engine.Fire(instance, "start_review");
        engine.Fire(instance, "approve");

        Assert.True(instance.IsCompleted);
        Assert.Empty(engine.PermittedTriggers(instance));
        Assert.Throws<WorkflowException>(() => engine.Fire(instance, "submit"));
    }

    [Fact]
    public void Reject_then_revise_loops_back_to_draft()
    {
        var engine = Engine();
        var instance = engine.Start("approval");
        engine.Fire(instance, "submit");
        engine.Fire(instance, "start_review");
        engine.Fire(instance, "reject");
        engine.Fire(instance, "revise");

        Assert.Equal("Draft", instance.CurrentState);
        Assert.Equal(4, instance.History.Count);
    }

    [Fact]
    public void OnTransition_action_mutates_context()
    {
        var def = new WorkflowBuilder("wf")
            .StartAt("A")
            .Permit("A", "go", "B", onTransition: ctx => ctx.Set("touched", "yes"))
            .FinalState("B")
            .Build();
        var engine = new WorkflowEngine(new[] { def });
        var instance = engine.Start("wf");

        engine.Fire(instance, "go");

        Assert.Equal("yes", instance.Context.Get("touched"));
    }

    [Fact]
    public void Builder_rejects_outgoing_transition_from_final_state()
    {
        var builder = new WorkflowBuilder("bad")
            .StartAt("A")
            .Permit("A", "go", "B")
            .Permit("B", "back", "A")
            .FinalState("B");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Builder_rejects_duplicate_trigger_from_same_state()
    {
        var builder = new WorkflowBuilder("dup").StartAt("A").Permit("A", "go", "B");
        Assert.Throws<InvalidOperationException>(() => builder.Permit("A", "go", "C"));
    }

    [Fact]
    public void Builder_rejects_initial_state_that_is_final()
    {
        var builder = new WorkflowBuilder("bad").StartAt("A").FinalState("A");
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Unknown_definition_throws()
    {
        Assert.Throws<WorkflowException>(() => Engine().Start("nope"));
    }
}
