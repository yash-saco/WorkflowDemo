using WorkflowDemo.Core;
using Xunit;

namespace WorkflowDemo.Tests;

public class TemplateCompilerTests
{
    private static WorkflowTemplate Template() => new()
    {
        Id = "purchase-request",
        Name = "Purchase Request",
        Rules =
        {
            new ApprovalRule
            {
                Id = "team-dual", Name = "Team member — dual", Priority = 1,
                Condition = RuleCondition.RoleIn("Team Member"),
                Steps =
                {
                    new WorkflowStep { Type = StepTypes.Approval, Name = "N+1",
                        Approver = new ApproverSpec { Mode = ApproverModes.Hierarchy, Level = 1 } },
                    new WorkflowStep { Type = StepTypes.Approval, Name = "N+2",
                        Approver = new ApproverSpec { Mode = ApproverModes.Hierarchy, Level = 2 } },
                    new WorkflowStep { Type = StepTypes.Notification, Name = "Notify" },
                },
            },
            new ApprovalRule
            {
                Id = "leader-single", Name = "Leadership — single", Priority = 2,
                Condition = RuleCondition.RoleIn("Manager", "IT Director"),
                Steps =
                {
                    new WorkflowStep { Type = StepTypes.Approval, Name = "N+1",
                        Approver = new ApproverSpec { Mode = ApproverModes.Hierarchy, Level = 1 } },
                },
            },
        },
    };

    private static Dictionary<string, string> Attrs(string role) =>
        new(StringComparer.OrdinalIgnoreCase) { ["role"] = role };

    [Fact]
    public void Team_member_selects_dual_approval_rule()
    {
        var rule = TemplateCompiler.SelectRule(Template(), Attrs("Team Member"));
        Assert.Equal("team-dual", rule!.Id);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("IT Director")]
    [InlineData("it director")] // case-insensitive
    public void Leadership_selects_single_approval_rule(string role)
    {
        var rule = TemplateCompiler.SelectRule(Template(), Attrs(role));
        Assert.Equal("leader-single", rule!.Id);
    }

    [Fact]
    public void Unmatched_role_selects_nothing()
    {
        Assert.Null(TemplateCompiler.SelectRule(Template(), Attrs("CEO")));
    }

    [Fact]
    public void Priority_orders_rule_selection()
    {
        var t = Template();
        t.Rules[1].Condition = RuleCondition.Any(); // now both match a Team Member
        t.Rules[1].Priority = 0;                    // and leadership rule wins
        var rule = TemplateCompiler.SelectRule(t, Attrs("Team Member"));
        Assert.Equal("leader-single", rule!.Id);
    }

    [Fact]
    public void Dual_approval_rule_compiles_to_expected_chain()
    {
        var t = Template();
        var def = TemplateCompiler.Compile(t, t.Rules[0]);

        // Draft -> Step1 -> Step2 -> Step3(auto) -> Approved; rejects from steps 1&2.
        Assert.Equal("Draft", def.InitialState);
        Assert.Contains("Approved", def.FinalStates);
        Assert.Equal("Step1", def.FindTransition("Draft", "submit")!.ToState);
        Assert.Equal("Step2", def.FindTransition("Step1", "approve")!.ToState);
        Assert.Equal("Step3", def.FindTransition("Step2", "approve")!.ToState);
        Assert.Equal("Approved", def.FindTransition("Step3", "continue")!.ToState);
        Assert.Equal("Rejected", def.FindTransition("Step1", "reject")!.ToState);
        Assert.Equal("Rejected", def.FindTransition("Step2", "reject")!.ToState);
        Assert.Null(def.FindTransition("Step3", "reject")); // auto step can't be rejected
        Assert.Equal("Draft", def.FindTransition("Rejected", "revise")!.ToState);
    }

    [Fact]
    public void Compiled_dual_approval_runs_end_to_end_on_engine()
    {
        var t = Template();
        var def = TemplateCompiler.Compile(t, t.Rules[0]);
        var engine = new WorkflowEngine(new[] { def });
        var instance = engine.Start(def.Id);

        engine.Fire(instance, "submit");
        engine.Fire(instance, "approve");   // N+1
        engine.Fire(instance, "approve");   // N+2
        engine.Fire(instance, "continue");  // notification auto-step
        Assert.True(instance.IsCompleted);
        Assert.Equal("Approved", instance.CurrentState);
    }

    [Fact]
    public void Single_approval_rule_compiles_to_one_step_chain()
    {
        var t = Template();
        var def = TemplateCompiler.Compile(t, t.Rules[1]);
        Assert.Equal("Approved", def.FindTransition("Step1", "approve")!.ToState);
        Assert.Equal("Rejected", def.FindTransition("Step1", "reject")!.ToState);
    }

    [Fact]
    public void Approval_step_without_approver_fails_validation()
    {
        var t = Template();
        t.Rules[0].Steps[0].Approver = null;
        Assert.Throws<WorkflowException>(() => TemplateCompiler.Validate(t));
    }

    [Fact]
    public void Rule_without_steps_fails_validation()
    {
        var t = Template();
        t.Rules[0].Steps.Clear();
        Assert.Throws<WorkflowException>(() => TemplateCompiler.Validate(t));
    }

    [Fact]
    public void Role_mode_without_role_fails_validation()
    {
        var t = Template();
        t.Rules[0].Steps[0].Approver = new ApproverSpec { Mode = ApproverModes.Role, Role = "  " };
        Assert.Throws<WorkflowException>(() => TemplateCompiler.Validate(t));
    }

    [Fact]
    public void StepIndexOf_parses_step_states_only()
    {
        Assert.Equal(2, TemplateCompiler.StepIndexOf("Step2"));
        Assert.Null(TemplateCompiler.StepIndexOf("Draft"));
        Assert.Null(TemplateCompiler.StepIndexOf("Approved"));
    }
}
