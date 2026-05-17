# Implementation Plan: T-063 — Integration tests for forward shape + validation + audit

## Task Reference
- **Task ID:** T-063 · **Type:** Testing · **Workflow:** standard · **Complexity:** M
- **Rationale:** Closes the AC loop. The brief explicitly asks for "Verified with a fake-executor integration test that asserts the JSON body byte-for-byte on the relevant subtree."

## Overview
One new xUnit class on the WorkItems side covering the forward-shape ACs, one on the Workspace side covering the project-validation + audit ACs. Both reuse the existing `WebApplicationFactory` + Testcontainers Postgres + Kestrel-hosted FakeExecutor harness.

## Implementation Steps

### Step 1: Identify or add a recorded-body helper on the FakeExecutor
**File:** `tests/DevHub.Modules.WorkItems.Tests/Acceptance/FakeExecutor.cs` (or similar) · Modify

The FakeExecutor likely already records inbound calls. Confirm by reading the file. If not, add a thread-safe `ConcurrentQueue<RecordedCall> Calls` with `Method, Path, BodyJson` (raw string of the request body) and an accessor `LastBody()` returning the most recent `/work-items` POST body as a string.

If the body isn't currently buffered, copy it once via `req.EnableBuffering()` + a `StreamReader` into the recorded call. Cost is negligible at test scale.

### Step 2: Create the WorkItems test class
**File:** `tests/DevHub.Modules.WorkItems.Tests/Acceptance/CodeSourceForwardTests.cs` · Create

```csharp
[Collection(WorkItemsAcceptanceCollection.Name)]
public class CodeSourceForwardTests(WorkItemsAcceptanceFixture fx)
{
    [Fact]
    public async Task Start_includes_codeSource_when_project_has_repo()
    {
        var (client, projectId) = await fx.SeedProjectAsync(repo: "acme/widgets", defaultBranch: "main");

        var resp = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/work-items",
            new { title = "Add CSV export", input = new { }, workBranch = "feat/abc" });
        resp.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(fx.FakeExecutor.LastBody())!;
        body["intake"]!["codeSource"]!.AsObject()
            .Should().BeEquivalentTo(new JsonObject
            {
                ["repo"] = "acme/widgets",
                ["baseBranch"] = "main",
                ["workBranch"] = "feat/abc",
            }, opts => opts.ComparingByMembers<JsonNode>());
    }

    [Fact]
    public async Task Start_omits_codeSource_block_entirely_when_project_repo_is_null()
    {
        var (client, projectId) = await fx.SeedProjectAsync(repo: null, defaultBranch: null);

        var resp = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/work-items",
            new { title = "X", input = new { } });
        resp.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(fx.FakeExecutor.LastBody())!;
        body.AsObject().ContainsKey("intake").Should().BeFalse();

        fx.Logs.Should().Contain(l => l.Message.Contains("codeSourceMissing=true"));
    }

    [Fact]
    public async Task Start_omits_workBranch_subfield_when_work_branch_is_null()
    {
        var (client, projectId) = await fx.SeedProjectAsync(repo: "a/b", defaultBranch: "main");

        var resp = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/work-items",
            new { title = "X", input = new { } });   // no workBranch
        resp.EnsureSuccessStatusCode();

        var cs = JsonNode.Parse(fx.FakeExecutor.LastBody())!["intake"]!["codeSource"]!.AsObject();
        cs.ContainsKey("workBranch").Should().BeFalse();
        cs["repo"]!.GetValue<string>().Should().Be("a/b");
        cs["baseBranch"]!.GetValue<string>().Should().Be("main");
    }

    [Fact]
    public async Task UpdateWorkItem_workBranch_writes_audit_entry()
    {
        var (client, projectId) = await fx.SeedProjectAsync(repo: "a/b", defaultBranch: "main");
        var workItemId = await fx.SeedWorkItemAsync(client, projectId, workBranch: null);

        var resp = await client.PatchAsJsonAsync(
            $"/api/projects/{projectId}/work-items/{workItemId}",
            new { workBranch = "feat/x" });
        resp.EnsureSuccessStatusCode();

        var audits = await fx.LoadAuditAsync(client, action: "workitem:update", targetId: workItemId);
        audits.Should().ContainSingle(a =>
            (string?)a.Details["workBranchBefore"] == null &&
            (string?)a.Details["workBranchAfter"] == "feat/x");
    }
}
```

The fixture's `SeedProjectAsync` helper is new — add it alongside the existing `SeedAsync` helpers, defaulting `repo` and `defaultBranch` to `null`.

### Step 3: Create the Workspace test class
**File:** `tests/DevHub.Modules.Workspace.Tests/Acceptance/CodeSourceProjectTests.cs` · Create

```csharp
[Collection(WorkspaceAcceptanceCollection.Name)]
public class CodeSourceProjectTests(WorkspaceAcceptanceFixture fx)
{
    [Theory]
    [InlineData("https://github.com/foo/bar", "repo.shape")]
    [InlineData("foo/bar.git", "repo.shape")]
    [InlineData("foo", "repo.shape")]
    [InlineData("foo/bar/baz", "repo.shape")]
    public async Task CreateProject_with_invalid_repo_returns_400(string repo, string expectedRuleHint)
    {
        var client = await fx.LoginOperatorAsync();
        var teamId = await fx.SeedTeamAsync(client);

        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "X", slug = "x", projectType = "feature-delivery",
            owningTeamId = teamId,
            repo,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().EndWith("/validation");
        problem.Detail.Should().Contain(expectedRuleHint);

        // No project row written.
        await fx.AssertProjectCountAsync(client, expected: 0);

        // Denied audit entry exists.
        var denied = await fx.LoadAuditAsync(client, action: "project:create", outcome: "Denied");
        denied.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("/main")]
    [InlineData("feat/..lol")]
    [InlineData("feat lol")]
    public async Task CreateProject_with_invalid_default_branch_returns_400(string branch)
    {
        var client = await fx.LoginOperatorAsync();
        var teamId = await fx.SeedTeamAsync(client);

        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Y", slug = "y", projectType = "feature-delivery",
            owningTeamId = teamId,
            repo = "a/b",
            defaultBranch = branch,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProject_repo_change_writes_audit_with_before_and_after()
    {
        var client = await fx.LoginOperatorAsync();
        var projectId = await fx.SeedProjectAsync(client, repo: "foo/bar");

        var resp = await client.PatchAsJsonAsync($"/api/projects/{projectId}",
            new { repo = "foo/baz" });
        resp.EnsureSuccessStatusCode();

        var audits = await fx.LoadAuditAsync(client, action: "project:update", targetId: projectId);
        audits.Should().ContainSingle(a =>
            (string)a.Details["repoBefore"] == "foo/bar" &&
            (string)a.Details["repoAfter"] == "foo/baz");
    }
}
```

### Step 4: Run the suite
**Bash:**

```bash
dotnet test
```

Both new classes green; existing acceptance tests remain green.

## Files Affected
| File | Action |
|------|--------|
| `tests/DevHub.Modules.WorkItems.Tests/Acceptance/CodeSourceForwardTests.cs` | Create |
| `tests/DevHub.Modules.Workspace.Tests/Acceptance/CodeSourceProjectTests.cs` | Create |
| `tests/DevHub.Modules.WorkItems.Tests/Acceptance/FakeExecutor.cs` | Modify (if recording helper missing) |
| Fixture helpers (`SeedProjectAsync`, `LoadAuditAsync` with details) | Modify |

## Edge Cases & Risks
- **Audit `Details` JSON shape varies.** Audit entries store details as JSONB (likely deserialized as `Dictionary<string, JsonElement>` or `object?`). The test helper `LoadAuditAsync` must return entries with details parsed enough to assert on. If it doesn't today, add a small projection.
- **JsonNode equivalence assertions.** `FluentAssertions` doesn't natively compare `JsonNode` — the example uses a small custom comparer. Alternatively, serialize both sides back to canonical-form strings and compare. Pick whichever lands shorter.
- **Theory `expectedRuleHint`.** The hint string is part of the `ValidationException` message format from T-056. If T-056 phrasing changes, this assertion needs an update — that's the desired coupling.

## Acceptance Verification
- [ ] Forward-shape ACs (5, 6, 7) covered by `CodeSourceForwardTests`.
- [ ] Validation deny + audit ACs (3, 4, 10 — project half) covered by `CodeSourceProjectTests`.
- [ ] Update-audit AC (10 — work item half) covered.
- [ ] `dotnet test` green; no flaky / order-dependent assertions.
