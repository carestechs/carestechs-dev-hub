# Implementation Plan: T-031 — Executor Registry integration tests

## Task Reference
- **Task ID:** T-031
- **Type:** Testing
- **Workflow:** standard
- **Complexity:** L
- **Rationale:** FEAT-001 set the rule "every façade endpoint ships with a deny-path test." Five mutation endpoints + the router + the Project hook all need both paths.

## Overview
Three new test files in the ExecutorRegistry test project + one in the Workspace test project (the AC-2 project-binding-rejection lives there because that's where `ProjectService` is tested). Reuse `DevHubApiFactory` from T-020 and the workspace helpers from T-025 for operator login.

## Implementation Steps

### Step 1: Test harness extensions
**File:** `tests/DevHub.Modules.ExecutorRegistry.Tests/PostgresCollection.cs` · Verify (already exists from T-020)

**File:** `tests/DevHub.TestHarness/DevHubApiFactory.cs` · Modify
Add a `WithSeedExecutor()` opt-in knob that, on first call, seeds:
- An `ExecutorRegistration` with `key=feature-delivery-v1`, `displayName=Feature Delivery v1`, `baseUrl=http://localhost:9999`, `credentialsRef=TEST_EXEC_TOKEN`, `status=Active`.
- One `CheckpointContract` `(checkpoint_key=approve, required_role_key=approver, allowed_outcomes=["approve","reject"])`.
- One `ExecutorBinding` `(project_type=feature-delivery, executor_id=...)`.

Implementation: a `_seedExecutor = true` field set by the opt-in. In `ConfigureWebHost`, after migrations, run an `IHostedService` (or direct `using var scope` block) that inserts via the live `ExecutorRegistryDbContext`. Idempotent (`AnyAsync(b => b.ProjectType == "feature-delivery")` guard).

**File:** `tests/DevHub.Modules.ExecutorRegistry.Tests/Helpers/RegistryTestHelpers.cs` · Create
- `LoginOperatorAsync(HttpClient)` — delegate to existing workspace helper.
- `LoginFreshMemberAsync(HttpClient)` — delegate to existing workspace helper.
- `SeedExecutorViaApiAsync(HttpClient operatorClient, string key)` — POSTs a minimal executor and returns the deserialized DTO.
- `SeedBindingViaApiAsync(HttpClient operatorClient, string projectType, Guid executorId)`.
- `LatestAuditEntryAsync(IServiceProvider, string targetType, Guid? targetId)` — direct `AuditDbContext` query.

### Step 2: ExecutorsEndpointsTests
**File:** `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorsEndpointsTests.cs` · Create

Tests:
1. `Create_as_operator_returns_201_and_audits_granted`
2. `Create_as_non_operator_returns_403_and_audits_denied`
3. `Create_with_unknown_required_role_key_returns_400`
4. `Create_with_duplicate_key_returns_409`
5. `Patch_status_and_displayName_round_trip`
6. `Replace_contracts_atomically_replaces_set`
7. `Delete_with_active_binding_returns_409`
8. `Delete_without_active_binding_soft_deletes_and_disappears_from_list`
9. `Response_never_includes_resolved_secret_value` — set `TEST_EXEC_TOKEN=super-secret` via `Environment.SetEnvironmentVariable`, register an executor with `credentialsRef=TEST_EXEC_TOKEN`, hit every GET that returns the executor, assert response body does **not** contain `super-secret`.

Each grant test asserts an audit row via `LatestAuditEntryAsync`. Each deny test asserts the audit row with `outcome=Denied`.

### Step 3: ExecutorBindingsEndpointsTests
**File:** `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorBindingsEndpointsTests.cs` · Create

Tests:
1. `Create_as_operator_returns_201`
2. `Create_as_non_operator_returns_403`
3. `Create_with_duplicate_active_projectType_returns_409`
4. `Create_with_nonexistent_executor_returns_404`
5. `Delete_soft_deletes_and_is_idempotent` (second DELETE returns 404)

### Step 4: ExecutorRouterTests
**File:** `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorRouterTests.cs` · Create

Uses `WithSeedExecutor()` factory + a service-scope resolve to call `IExecutorRouter` directly (module-internal, not HTTP).

Tests:
1. `IsProjectTypeBoundAsync_returns_true_for_seeded_binding`
2. `IsProjectTypeBoundAsync_returns_false_for_unknown_type`
3. `IsProjectTypeBoundAsync_returns_false_after_soft_delete`
4. `ResolveAsync_returns_descriptor_for_bound_project` (create a project via API, then resolve)
5. `ResolveAsync_returns_null_for_project_whose_type_has_no_binding` (manually create a project bypassing validation — direct DbContext insert)
6. `ResolveAsync_includes_checkpoint_contracts`
7. `GetCheckpointContractAsync_returns_descriptor_for_known_key`
8. `GetCheckpointContractAsync_returns_null_for_unknown_key`

### Step 5: ProjectBindingValidationTests
**File:** `tests/DevHub.Modules.Workspace.Tests/ProjectBindingValidationTests.cs` · Create

Uses the `WithSeedExecutor()` factory.

Tests:
1. `Create_project_with_bound_projectType_returns_201`
2. `Create_project_with_unbound_projectType_returns_409` — assert problem-detail has `title="Conflict"` and `detail` mentions "no executor bound."
3. `Create_project_after_binding_deleted_returns_409`

Note: the existing FEAT-002 walkthrough tests need an update to use `WithSeedExecutor()` since they create projects with `projectType="feature-delivery"` — without a binding they'll start failing. Sweep `tests/DevHub.Modules.Workspace.Tests/` for `CreateProjectAsync` callers and switch their factory to `WithSeedExecutor()`. Document in the PR body.

## Files Affected
| File | Action |
|------|--------|
| `tests/DevHub.TestHarness/DevHubApiFactory.cs` | Modify (`WithSeedExecutor`) |
| `tests/DevHub.Modules.ExecutorRegistry.Tests/PostgresCollection.cs` | Verify |
| `tests/DevHub.Modules.ExecutorRegistry.Tests/Helpers/RegistryTestHelpers.cs` | Create |
| `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorsEndpointsTests.cs` | Create |
| `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorBindingsEndpointsTests.cs` | Create |
| `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorRouterTests.cs` | Create |
| `tests/DevHub.Modules.Workspace.Tests/ProjectBindingValidationTests.cs` | Create |
| `tests/DevHub.Modules.Workspace.Tests/{Projects,Walkthrough}*.cs` | Modify (opt into `WithSeedExecutor`) |
| `src/DevHub.Modules.ExecutorRegistry/DevHub.Modules.ExecutorRegistry.csproj` | Modify (`InternalsVisibleTo` for `ExecutorRegistry.Tests`) |

## Edge Cases & Risks
- **Existing Workspace test regressions** — adding the binding check to `ProjectService.Create` breaks any test that creates a project without seeding a binding. Mitigation: sweep at PR time. Confirm via `dotnet test` red before fixing.
- **Env-var leak test pollution** — `Environment.SetEnvironmentVariable("TEST_EXEC_TOKEN", ...)` is process-wide. Use a unique env-var name per test and `try { ... } finally { Environment.SetEnvironmentVariable(name, null); }` to clean up.
- **WithSeedExecutor idempotency under xUnit parallel** — `DevHubApiFactory` is cached per test class (`[Collection("postgres")]` provides one Postgres instance per test class). The seed runs once per factory. Verify with the FEAT-002 pattern (already proven).

## Acceptance Verification
- [ ] `dotnet test tests/DevHub.Modules.ExecutorRegistry.Tests` is green with all new tests.
- [ ] `dotnet test` for the full suite is green (no FEAT-002 regressions after the factory sweep).
- [ ] The leak test (Step 2 #9) genuinely fails when the secret string is intentionally placed in a response — verify by sabotaging the DTO mapper once, watching the test fail, then reverting.
