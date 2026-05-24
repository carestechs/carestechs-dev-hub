using System.Text.Json;
using DevHub.Modules.WorkItems.Services.Orchestrator;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// <summary>
/// BUG-001 / T-096: regression tests for the realigned trace-projection helpers.
/// Pure unit tests — no Postgres, no HTTP. Builds trace records as JSON literals
/// matching the orchestrator's on-the-wire <c>{kind, data}</c> shape.
/// </summary>
public class ExecutorStateProjectionTests
{
    private static IReadOnlyList<JsonElement> Trace(params string[] lines) =>
        lines.Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();

    // ---------------------------------------------------------------------
    // LatestStepTaskId
    // ---------------------------------------------------------------------

    [Fact]
    public void LatestStepTaskId_empty_trace_returns_null()
    {
        ExecutorStateProjection.LatestStepTaskId(Array.Empty<JsonElement>()).Should().BeNull();
    }

    [Fact]
    public void LatestStepTaskId_returns_null_when_only_operator_signals_present()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"alice"}}}"""
        );
        ExecutorStateProjection.LatestStepTaskId(trace).Should().BeNull();
    }

    [Fact]
    public void LatestStepTaskId_returns_latest_step_taskId_last_write_wins()
    {
        var trace = Trace(
            """{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":"T-1"}}}""",
            """{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":"T-2"}}}"""
        );
        ExecutorStateProjection.LatestStepTaskId(trace).Should().Be("T-2");
    }

    [Fact]
    public void LatestStepTaskId_returns_null_when_latest_step_lacks_nodeInputs_taskId_does_not_walk_back()
    {
        var trace = Trace(
            """{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":"T-1"}}}""",
            """{"kind":"step","data":{"nodeName":"other","nodeInputs":{}}}"""
        );
        ExecutorStateProjection.LatestStepTaskId(trace).Should().BeNull();
    }

    [Fact]
    public void LatestStepTaskId_treats_empty_string_taskId_as_missing()
    {
        var trace = Trace(
            """{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":""}}}"""
        );
        ExecutorStateProjection.LatestStepTaskId(trace).Should().BeNull();
    }

    [Fact]
    public void LatestStepTaskId_skips_step_records_missing_data_field()
    {
        var trace = Trace(
            """{"kind":"step","data":{"nodeName":"confirm_assignment","nodeInputs":{"taskId":"T-1"}}}""",
            """{"kind":"step"}"""
        );
        // The defensive skip means the missing-data record is ignored entirely;
        // the helper returns the latest *eligible* step's task id.
        ExecutorStateProjection.LatestStepTaskId(trace).Should().Be("T-1");
    }

    // ---------------------------------------------------------------------
    // ParseAssignmentsFromTrace
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseAssignmentsFromTrace_empty_trace_returns_empty_dict()
    {
        ExecutorStateProjection.ParseAssignmentsFromTrace(Array.Empty<JsonElement>())
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseAssignmentsFromTrace_picks_up_assignment_confirmed_signals()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"alice"}}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace)
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["T-1"] = "alice" });
    }

    [Fact]
    public void ParseAssignmentsFromTrace_last_write_wins_for_same_task()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"alice"}}}""",
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"bob"}}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace)
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["T-1"] = "bob" });
    }

    [Fact]
    public void ParseAssignmentsFromTrace_excludes_unrelated_signal_names()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"tasks-confirmed","taskId":"T-1","payload":{"items":["T-1"]}}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace).Should().BeEmpty();
    }

    [Fact]
    public void ParseAssignmentsFromTrace_skips_signals_missing_taskId()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","payload":{"assignee":"alice"}}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace).Should().BeEmpty();
    }

    [Fact]
    public void ParseAssignmentsFromTrace_skips_non_string_or_empty_assignee()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":123}}}""",
            """{"kind":"operator_signal","data":{"name":"assignment-confirmed","taskId":"T-2","payload":{"assignee":""}}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace).Should().BeEmpty();
    }

    [Fact]
    public void ParseAssignmentsFromTrace_ignores_legacy_flat_signal_kind_anti_pattern()
    {
        // Regression guard: the pre-BUG-001 implementation read top-level fields off
        // a kind=="signal" record. After the realignment, those records must be
        // rejected — readmitting them would re-introduce the silent-mismatch class.
        var trace = Trace(
            """{"kind":"signal","name":"assignment-confirmed","taskId":"T-1","payload":{"assignee":"alice"}}"""
        );
        ExecutorStateProjection.ParseAssignmentsFromTrace(trace).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // BuildSteps (FEAT-011 / T-098)
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildSteps_empty_trace_returns_empty_list()
    {
        ExecutorStateProjection.BuildSteps(Array.Empty<JsonElement>(), null)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuildSteps_extracts_steps_with_checkpoint_keys_and_labels()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"load_work_item","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":2,"nodeName":"confirm_brief","status":"completed","nodeInputs":{}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, null);
        steps.Should().HaveCount(2);

        var s0 = steps[0];
        s0.GetProperty("nodeName").GetString().Should().Be("load_work_item");
        s0.GetProperty("key").ValueKind.Should().Be(JsonValueKind.Null);
        s0.GetProperty("label").GetString().Should().Be("load_work_item");
        s0.GetProperty("status").GetString().Should().Be("completed");

        var s1 = steps[1];
        s1.GetProperty("nodeName").GetString().Should().Be("confirm_brief");
        s1.GetProperty("key").GetString().Should().Be("brief-confirmed");
        s1.GetProperty("label").GetString().Should().Be("Brief Review");
        s1.GetProperty("status").GetString().Should().Be("completed");
    }

    [Fact]
    public void BuildSteps_marks_active_step_when_matching_checkpoint_key()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"confirm_brief","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":2,"nodeName":"confirm_tasks","status":"dispatched","nodeInputs":{}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, "tasks-confirmed");
        steps.Should().HaveCount(2);

        steps[0].GetProperty("status").GetString().Should().Be("completed");
        steps[1].GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public void BuildSteps_includes_nodeResult_with_memory_patch_stripped()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"load_work_item","status":"completed","nodeInputs":{},"nodeResult":{"title":"CSV Export","work_item_id":"FEAT-1","summary":"Add CSV","__memory_patch":{"lifecycle.v1":{"workItem":{"id":"FEAT-1"}}}}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, null);
        steps.Should().HaveCount(1);

        var result = steps[0].GetProperty("nodeResult");
        result.GetProperty("title").GetString().Should().Be("CSV Export");
        result.GetProperty("work_item_id").GetString().Should().Be("FEAT-1");
        result.GetProperty("summary").GetString().Should().Be("Add CSV");
        result.TryGetProperty("__memory_patch", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSteps_nodeResult_is_null_for_dispatched_steps_without_result()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"confirm_brief","status":"dispatched","nodeInputs":{}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, "brief-confirmed");
        steps.Should().HaveCount(1);
        steps[0].GetProperty("nodeResult").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void BuildSteps_ignores_non_step_records()
    {
        var trace = Trace(
            """{"kind":"operator_signal","data":{"name":"brief-confirmed","taskId":"","payload":{}}}""",
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"confirm_brief","status":"completed","nodeInputs":{}}}""",
            """{"kind":"executor_call","data":{"dispatchId":"abc","runId":"def","executorRef":"human:confirm_brief"}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, null);
        steps.Should().HaveCount(1);
        steps[0].GetProperty("nodeName").GetString().Should().Be("confirm_brief");
    }

    [Fact]
    public void BuildSteps_all_display_labels_are_mapped()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"confirm_brief","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":2,"nodeName":"confirm_tasks","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":3,"nodeName":"confirm_assignment","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":4,"nodeName":"confirm_plan","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":5,"nodeName":"request_implementation","status":"completed","nodeInputs":{}}}""",
            """{"kind":"step","data":{"stepNumber":6,"nodeName":"human_review_implementation","status":"completed","nodeInputs":{}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, null);
        steps.Should().HaveCount(6);

        steps[0].GetProperty("label").GetString().Should().Be("Brief Review");
        steps[1].GetProperty("label").GetString().Should().Be("Task List");
        steps[2].GetProperty("label").GetString().Should().Be("Task Assignment");
        steps[3].GetProperty("label").GetString().Should().Be("Plan Review");
        steps[4].GetProperty("label").GetString().Should().Be("Implementation Review");
        steps[5].GetProperty("label").GetString().Should().Be("Final Review");
    }

    // ---------------------------------------------------------------------
    // Build (FEAT-011 / T-098 — updated signature)
    // ---------------------------------------------------------------------

    [Fact]
    public void Build_produces_steps_array_in_output()
    {
        var trace = Trace(
            """{"kind":"step","data":{"stepNumber":1,"nodeName":"confirm_brief","status":"completed","nodeInputs":{}}}"""
        );
        var steps = ExecutorStateProjection.BuildSteps(trace, null);
        var result = ExecutorStateProjection.Build(
            Guid.NewGuid(), "lifecycle-agent@0.4.0-manual",
            steps, new Dictionary<string, string>(), null);

        result.GetProperty("steps").GetArrayLength().Should().Be(1);
        result.GetProperty("steps")[0].GetProperty("key").GetString().Should().Be("brief-confirmed");
        result.GetProperty("assignments").EnumerateObject().Should().BeEmpty();
        result.GetProperty("stopReason").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
