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
}
