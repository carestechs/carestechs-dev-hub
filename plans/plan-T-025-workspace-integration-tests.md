# Implementation Plan: T-025 — Workspace integration tests

## Task Reference
- **Task ID:** T-025
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** L
- **Rationale:** FEAT-001's "every façade endpoint needs a deny-path test" discipline scales to ~10 new mutation endpoints here. The end-to-end walkthrough proves AC-1.

## Overview
Per-controller `*EndpointsTests` class (Teams, Members, Projects, Memberships, Roles) with at least one grant and one deny test for each mutation. One `WorkspaceWalkthroughTests` end-to-end test proves the seed operator can drive every primitive via REST. Audit rows asserted at the DbContext level on at least one grant + one deny per controller.

## Implementation Steps

### Step 1: Test helpers
**File:** `tests/DevHub.Modules.Workspace.Tests/Helpers/AuthenticatedClientHelpers.cs`
**Action:** Create

```csharp
internal static class AuthenticatedClientHelpers
{
    public static async Task<HttpClient> LoginOperatorAsync(this DevHubApiFactory factory, HttpClient? client = null)
    {
        client ??= factory.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = factory.OperatorEmail, password = factory.OperatorPassword,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public static async Task<(HttpClient client, Guid memberId)> LoginFreshMemberAsync(
        this DevHubApiFactory factory,
        string email = "alice@test.local",
        string password = "AliceTest123!",
        string displayName = "Alice")
    {
        // Seed a non-operator member directly via the WorkspaceDbContext + IdentityDbContext
        // (we don't have a public "create member with password" path yet — IdentityService.AddCredentialAsync
        // is fine to call from a test helper).
        // ... full implementation in test code ...
    }

    public static async Task<AuditEntry[]> AuditEntriesForActionAsync(this DevHubApiFactory factory, string action, CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditEntries.Where(a => a.Action == action).ToArrayAsync(ct);
    }
}
```

### Step 2: Per-controller endpoint tests
**Files:** `tests/DevHub.Modules.Workspace.Tests/{Teams,Members,Projects,Memberships,Roles}EndpointsTests.cs`
**Action:** Create

Each follows the AuthEndpointsTests pattern from T-020. Per controller:

**TeamsEndpointsTests** (~6 tests):
- `Create_as_operator_returns_200_and_audits_granted`
- `Create_as_anonymous_returns_401`
- `Create_as_non_operator_returns_403_and_audits_denied`
- `Delete_team_with_projects_returns_409`
- `List_returns_paginated_envelope`
- `Update_changes_name_and_audits`

**MembersEndpointsTests** (~5 tests):
- Invite as operator → 200 + audit
- Invite as non-operator → 403 + audit
- Update status to Suspended → suspended member can't log in (auth integration)
- Suspended member fails authorization in any project
- List with `q=` filter

**ProjectsEndpointsTests** (~6 tests):
- Create as operator → 200 + audit
- Create with duplicate slug → 409
- Delete cascades memberships
- Read as project member → 200
- Read as non-member non-operator → 403 + audit deny
- List filtered by team

**MembershipsEndpointsTests** (~6 tests):
- Add as operator → 200 + audit
- Add duplicate → 409
- Add as non-operator → 403 + audit deny
- Update role assignments → 200
- Delete last operator → 409
- List for project → returns the seeded operator only when it's a member

**RolesEndpointsTests** (~2 tests):
- List returns the seeded `operator` role
- Anonymous request → 401

### Step 3: End-to-end walkthrough
**File:** `tests/DevHub.Modules.Workspace.Tests/WorkspaceWalkthroughTests.cs`
**Action:** Create

A single `[Fact] End_to_end_operator_flow_creates_a_visible_project`:
1. Login as seed operator.
2. POST /api/teams `"Engineering"` → captures `teamId`.
3. POST /api/members `{ email: "alice@test.local", displayName: "Alice" }` → captures `aliceMemberId`.
4. POST /api/projects `{ name: "Add CSV Export", slug: "add-csv-export", projectType: "feature-delivery", owningTeamId: teamId }` → captures `projectId`.
5. POST /api/projects/{projectId}/memberships `{ memberId: aliceMemberId, roleKeys: ["operator"] }` (we use `operator` since v1 ships only that role) → 200.
6. Alice logs in (helper that adds a credential to her member).
7. Alice GET /api/projects → sees one project (`add-csv-export`).
8. Alice GET /api/auth/me → memberships array contains the project + `operator` role.
9. Assert audit log has rows for: team:create, member:invite, project:create, membership:add (all `Granted`).

This single test exercises FEAT-002's AC-1 end-to-end.

### Step 4: Update test harness if needed
**File:** `tests/DevHub.TestHarness/DevHubApiFactory.cs`
**Action:** Modify

Add a public `IServiceProvider Services => Server.Services` shortcut for the helpers, and expose `OperatorEmail`/`OperatorPassword` as public (already public but confirm).

### Step 5: Run + iterate
**Action:** Verify

`dotnet test DevHub.slnx --no-build` should report ≥40 passing tests (was 20 from T-020 + 5 from T-023 + ~3 from T-022 + 25 new here).

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `tests/DevHub.Modules.Workspace.Tests/Helpers/AuthenticatedClientHelpers.cs` | Create | Login + audit-query helpers |
| `tests/DevHub.Modules.Workspace.Tests/TeamsEndpointsTests.cs` | Create | ~6 tests |
| `tests/DevHub.Modules.Workspace.Tests/MembersEndpointsTests.cs` | Create | ~5 tests |
| `tests/DevHub.Modules.Workspace.Tests/ProjectsEndpointsTests.cs` | Create | ~6 tests |
| `tests/DevHub.Modules.Workspace.Tests/MembershipsEndpointsTests.cs` | Create | ~6 tests |
| `tests/DevHub.Modules.Workspace.Tests/RolesEndpointsTests.cs` | Create | ~2 tests |
| `tests/DevHub.Modules.Workspace.Tests/WorkspaceWalkthroughTests.cs` | Create | 1 end-to-end test |
| `tests/DevHub.TestHarness/DevHubApiFactory.cs` | Modify | Expose `Services` (and confirm OperatorEmail/Password are public) |

## Edge Cases & Risks
- **Seeding a non-operator member from a test** — requires an IdentityService helper that takes a plaintext password and writes the Argon2id-hashed credential. Either add a small `IIdentitySeederPort` to TestHarness or call the DbContext directly from the helper. Pick the second; the helper is intentionally tied to the test harness, not production code.
- **Audit assertions racing with the response** — services write the audit row inside the same transaction as the mutation, so by the time the HTTP response comes back, the audit row is committed. Reads from a fresh scope see it.
- **Test runtime** — Testcontainers warmup dominates; one Postgres container per assembly serves ~30 tests well under 60s.

## Acceptance Verification
- [ ] Each controller has ≥1 grant + ≥1 deny test.
- [ ] `WorkspaceWalkthroughTests` end-to-end passes.
- [ ] Audit-row assertions cover at least one grant + one deny per controller.
- [ ] `dotnet test DevHub.slnx` reports ≥40 passing tests.
