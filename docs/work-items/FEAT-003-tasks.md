# FEAT-003 Task Breakdown — Executor Registry & Bindings

> Generated from `docs/work-items/FEAT-003-executor-registry.md` using `.ai-framework/prompts/feature-tasks.md`. 5 tasks across Backend, Testing, and Frontend.

## Scope choices locked in before generation

- **Module home:** all new entities, services, and endpoints live in `DevHub.Modules.ExecutorRegistry` (T-009 already created the project + stub migration; this FEAT lands the real one).
- **`credentialsRef`:** stored as a literal env-var name (e.g. `EXEC_FEATURE_DELIVERY_TOKEN`). The Registry exposes a `IExecutorCredentialResolver` that reads `Environment.GetEnvironmentVariable(...)` at call time — never returned by any API, never logged. Resolution itself ships with this FEAT; the *use* of resolved credentials is FEAT-004's concern.
- **Router contract:** `IExecutorRouter.ResolveAsync(projectId)` is published from `DevHub.Contracts`. Returns a small DTO (`ExecutorRegistrationDescriptor`) — never the entity, never the credentials.
- **Project creation hook:** the TODO log line in `ProjectService.Create` from T-024 becomes a real check against `IExecutorRouter.IsProjectTypeBoundAsync(projectType)`. Backward-compatible: existing projects keep working; new creates with an unbound `projectType` return 409.
- **Soft delete:** ExecutorRegistration + ExecutorBinding follow the existing `deleted_at` pattern. CheckpointContract is owned by its parent `ExecutorRegistration` and gets hard-replaced on `POST /checkpoint-contracts` (replace semantics).
- **Authorization:** every endpoint under `/api/admin/executors/*` and `/api/admin/executor-bindings/*` is operator-only via `IProjectAuthorizationService.AuthorizeWorkspaceOperatorAsync(...)` (new method — workspace-scoped twin of the existing project authorize). Audit row per call.

---

## Backend

### T-029: Foundation — entities, DbContext, real migration

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-009 (stub migration), T-022 (audit module)

**Description:**
Replace T-009's empty ExecutorRegistry migration with a real one. Land `ExecutorRegistration`, `ExecutorBinding`, `CheckpointContract` entities per `docs/data-model.md` §189–243, wire `ExecutorRegistryDbContext`, and ship the `ExecutorStatus` enum (`Active`, `Paused`, `Retired`).

**Rationale:**
Nothing else in FEAT-003 can be built without these three tables. The migration replacement mirrors the T-022 pattern (audit module did the same).

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.ExecutorRegistry` creates `executor_registry.executor_registrations`, `executor_bindings`, `checkpoint_contracts` with all columns from data-model.md.
- [ ] Unique partial index on `executor_registrations(key)` where `deleted_at IS NULL`.
- [ ] Unique partial index on `executor_bindings(project_type)` where `deleted_at IS NULL`.
- [ ] Unique index on `checkpoint_contracts(executor_id, checkpoint_key)`.
- [ ] `ExecutorStatus` enum is stored as `varchar(20)` (string conversion) — not an int.
- [ ] `CheckpointContract.allowed_outcomes` is a `jsonb` column storing a string array.
- [ ] `CheckpointContract` cascades soft-delete with its parent `ExecutorRegistration`.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.ExecutorRegistry/Entities/ExecutorRegistration.cs`, `ExecutorBinding.cs`, `CheckpointContract.cs`
- Create: `src/DevHub.Modules.ExecutorRegistry/Enums/ExecutorStatus.cs`
- Modify: `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryDbContext.cs` (DbSets + mappings + indexes)
- Replace: `src/DevHub.Modules.ExecutorRegistry/Migrations/*` (real `Initial`, replacing T-009)
- Modify: `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryModuleExtensions.cs` (no behavior change yet; register DbContext only)

**Technical Notes:**
All three entities are `BaseEntity` + `ISoftDeletable` (except `CheckpointContract`, which is hard-replaced — it has no `deleted_at`). `ExecutorRegistration` has a navigation collection of `CheckpointContract` for ergonomic loads; `ExecutorBinding` references `ExecutorRegistration` by FK but **no cross-module navigation property** to `Project` (FK only — Workspace and ExecutorRegistry don't share a DbContext). Indexes on `(executor_id)` for both bindings + contracts to keep cascades cheap.

---

### T-030: Backend services + controllers + IExecutorRouter + Project hook

**Type:** Backend · **Workflow:** standard · **Complexity:** XL · **Dependencies:** T-029, T-023 (auth contracts)

**Description:**
Land the operator-only Registry surface and the cross-module router contract:
- `IExecutorRouter` (published from `DevHub.Contracts`): `ResolveAsync(projectId)`, `IsProjectTypeBoundAsync(projectType)`, `GetCheckpointContractAsync(executorId, checkpointKey)`. Returns descriptors only — never entities, never credentials.
- `IExecutorCredentialResolver` (published from `DevHub.Contracts`): `ResolveAsync(executorId)` reads the env var named by `credentialsRef` and returns the literal. **Module-internal callers only** — FEAT-004 will consume.
- `ExecutorRegistrationService`, `ExecutorBindingService` with full CRUD + soft delete + replace-contracts semantics. Every mutation runs through `IProjectAuthorizationService.AuthorizeWorkspaceOperatorAsync(action)` and writes an audit row inside the same transaction.
- `ExecutorsController` + `ExecutorBindingsController` — thin façades per `docs/api-spec.md` §Executor Registry.
- Wire `ProjectService.Create` (in Workspace) to call `IExecutorRouter.IsProjectTypeBoundAsync(projectType)` and return `409 /probs/conflict` when unbound. Replaces the FEAT-002 TODO log line.

**Rationale:**
FEAT-003 AC-1..AC-5 + Edge cases all land here. This is the keystone — every later FEAT (work items, notifications) reads from this registry.

**Acceptance Criteria:**
- [ ] `POST /api/admin/executors` returns `201` with the new executor + all contracts; `credentialsRef` is **echoed back** as a literal in the response (it's not a secret value, just a reference to one).
- [ ] `POST /api/admin/executors` with a `requiredRoleKey` that does not match an existing `Role.key` returns `400 /probs/validation`.
- [ ] `POST /api/admin/executors` with a duplicate non-deleted `key` returns `409 /probs/conflict`.
- [ ] `POST /api/admin/executors/{id}/checkpoint-contracts` replaces the executor's contract list atomically (delete-then-insert in one tx).
- [ ] `PATCH /api/admin/executors/{id}` accepts `status`, `displayName`, `baseUrl`, `credentialsRef`; status transitions Active↔Paused↔Retired are all allowed.
- [ ] `DELETE /api/admin/executors/{id}` returns `409` when any non-deleted `ExecutorBinding` references it; otherwise soft-deletes.
- [ ] `POST /api/admin/executor-bindings` returns `201`; duplicate active `projectType` returns `409`.
- [ ] `DELETE /api/admin/executor-bindings/{id}` soft-deletes; idempotent (re-DELETE returns `404`).
- [ ] `IExecutorRouter.ResolveAsync(projectId)` returns the bound executor regardless of executor status; the caller (FEAT-004) decides whether to forward based on `ExecutorStatus`.
- [ ] `IExecutorRouter.ResolveAsync` returns `null` (or throws `NotFoundException`) when the project's `projectType` has no active binding.
- [ ] `ProjectService.Create` rejects a `projectType` with no active binding with `409 /probs/conflict`, "no executor bound for this project type."
- [ ] No endpoint returns `credentialsRef` in a serialized form that exposes the underlying env var **value**. The `credentialsRef` *reference itself* is fine; the resolved secret never appears in any response.
- [ ] Every mutation (and every deny) writes an `AuditEntry` with `target_type` in `{ExecutorRegistration, ExecutorBinding, CheckpointContract}`.

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Executors/IExecutorRouter.cs`, `ExecutorRegistrationDescriptor.cs`, `CheckpointContractDescriptor.cs`
- Create: `src/DevHub.Contracts/Executors/IExecutorCredentialResolver.cs`
- Create: `src/DevHub.Modules.ExecutorRegistry/DTOs/*.cs` (ExecutorDto, CheckpointContractDto, CreateExecutorRequest, UpdateExecutorRequest, ReplaceContractsRequest, ExecutorBindingDto, CreateBindingRequest)
- Create: `src/DevHub.Modules.ExecutorRegistry/Services/ExecutorRegistrationService.cs`, `ExecutorBindingService.cs`, `ExecutorRouter.cs`, `ExecutorCredentialResolver.cs`
- Create: `src/DevHub.Modules.ExecutorRegistry/Controllers/ExecutorsController.cs`, `ExecutorBindingsController.cs`
- Modify: `src/DevHub.Modules.ExecutorRegistry/ExecutorRegistryModuleExtensions.cs` (register services + router + resolver + ApplicationPart)
- Modify: `src/DevHub.Contracts/Authorization/IProjectAuthorizationService.cs` (add `AuthorizeWorkspaceOperatorAsync(action, ...)` method)
- Modify: `src/DevHub.Modules.Workspace/Services/ProjectAuthorizationService.cs` (implement the new method)
- Modify: `src/DevHub.Modules.Workspace/Services/ProjectService.cs` (replace TODO with real `IExecutorRouter.IsProjectTypeBoundAsync` check)
- Modify: `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` (add `IExecutorRouter` dependency to Workspace registration order so DI resolves correctly)
- Modify: `src/DevHub.Api/Program.cs` (`AddApplicationPart(typeof(ExecutorsController).Assembly)` was already added in T-024; verify it picks up both controllers)

**Technical Notes:**
- `requiredRoleKey` validation in `CreateExecutorRequest` is a single query: `roleService.FindByKeyAsync(key)`; reject with `400` if null. To avoid an N+1 on the contracts list, batch the lookup against `Set<Role>().Where(r => keys.Contains(r.Key))`.
- Cross-module DI: `ExecutorRouter` reads from `ExecutorRegistryDbContext` directly; `ProjectService` resolves `IExecutorRouter` through DI. Both modules avoid each other's entity types — the descriptor types in Contracts are the bridge.
- `IExecutorRouter.GetCheckpointContractAsync(executorId, checkpointKey)` is the **single source of truth** for `requiredRoleKey` consumed by FEAT-004's checkpoint authorization. Document this contract on the interface so reviewers catch hardcoded role checks.
- Replace-contracts uses a single transaction: `db.CheckpointContracts.Where(c => c.ExecutorId == id).ExecuteDeleteAsync()` then `AddRange(...)` then `SaveChangesAsync()`.

---

### T-031: Integration tests — Registry + Bindings + Router + Project hook

**Type:** Testing · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-030

**Description:**
Per-controller `*EndpointsTests` with grant + deny per mutation, plus `ExecutorRouterTests` for the resolution paths, plus a `ProjectBindingValidationTests` that covers FEAT-003 AC-2 (project creation rejects an unbound `projectType`).

**Rationale:**
FEAT-001 set the discipline ("every façade endpoint requires a deny-path test"). Five mutation endpoints land here, each gets both paths.

**Acceptance Criteria:**
- [ ] `ExecutorsEndpointsTests`: create grant + deny, duplicate-key conflict, unknown-`requiredRoleKey` validation, patch, soft-delete with active binding (409), soft-delete without bindings.
- [ ] `ExecutorBindingsEndpointsTests`: create grant + deny, duplicate-active-projectType conflict, delete idempotency.
- [ ] `ExecutorRouterTests` (module-internal, uses `PostgresFixture`): `IsProjectTypeBoundAsync` true/false, `ResolveAsync` returns descriptor for bound project, returns null for unbound, returns the Retired executor for in-flight projects.
- [ ] `ProjectBindingValidationTests` (in Workspace tests): creating a project with `projectType="unbound"` → 409; with `projectType="feature-delivery"` (bound in seed) → 201.
- [ ] Audit assertions: at least one test per controller queries `AuditDbContext` and confirms the row landed.
- [ ] `dotnet test` reports the new tests in addition to the FEAT-002 set (no regressions).

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorsEndpointsTests.cs`
- Create: `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorBindingsEndpointsTests.cs`
- Create: `tests/DevHub.Modules.ExecutorRegistry.Tests/ExecutorRouterTests.cs`
- Create: `tests/DevHub.Modules.ExecutorRegistry.Tests/Helpers/RegistryTestHelpers.cs` (seed-a-feature-delivery-executor helper, seed-a-binding helper)
- Modify: `tests/DevHub.Modules.ExecutorRegistry.Tests/PostgresCollection.cs` (no-op if already present; verify the per-assembly collection exists)
- Create: `tests/DevHub.Modules.Workspace.Tests/ProjectBindingValidationTests.cs`
- Modify: `tests/DevHub.TestHarness/DevHubApiFactory.cs` if needed to seed a default `feature-delivery` executor + binding for tests that need a bound project type (gated behind a `WithSeedExecutor()` knob; opt-in to avoid touching existing tests).

**Technical Notes:**
Add `InternalsVisibleTo("DevHub.Modules.ExecutorRegistry.Tests")` on the ExecutorRegistry assembly so `ExecutorRouterTests` can reach internal helpers. For the seed-executor helper, prefer calling the REST API as the operator (keeps tests black-box) rather than direct DbContext writes — this also exercises the `POST /api/admin/executors` path under load.

---

## Frontend

### T-032: Frontend — workspace.service extensions + Executors admin screen

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-030, T-026 (shared components), T-028 (operator guard)

**Description:**
Stand up the operator-only `/admin/executors` screen per `docs/ui-specification.md` §9–13 + §12. Lists executors with their checkpoint contracts (expandable row or detail drawer — decide in the mockup), supports register / edit / soft-delete / replace-contracts. Extends the existing `workspace.service.ts` (or splits into a sibling `executor-registry.service.ts` — recommended) for the new endpoints.

**Rationale:**
FEAT-003 AC-5: "Admin UI lists executors with their `CheckpointContract`s, showing per-contract `requiredRoleKey`." Without this screen the registry can only be driven via REST — defeating the front-door rule.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/admin-executors.html` showing: list with status pills (`Active`/`Paused`/`Retired`), per-row checkpoint-contracts summary, register modal, edit modal, replace-contracts modal, delete confirmation.
- [ ] `/admin/executors` route guarded by `operatorGuard` (existing).
- [ ] Register modal: `key`, `displayName`, `baseUrl`, `credentialsRef`, dynamic list of checkpoint contracts with `(checkpointKey, displayName, requiredRoleKey, allowedOutcomes)` — `requiredRoleKey` is a dropdown populated from `GET /api/roles`.
- [ ] Edit modal updates only `status`, `displayName`, `baseUrl`, `credentialsRef` (key is immutable — show as read-only).
- [ ] "Replace contracts" modal lets the operator rewrite the contract list atomically.
- [ ] 400 (`unknown requiredRoleKey`) and 409 (`duplicate key`, `cannot delete with active bindings`) render as inline `AppErrorBanner` inside the modal.
- [ ] `credentialsRef` displays as a monospace chip with an info tooltip: "Reference to an env var on the API host. The actual secret value is never sent to the browser."
- [ ] Specs: page (loads, renders rows, handles 403), register-modal (validation, submit happy path, 400 surface), edit-modal (key is read-only, submit), delete (409 surfaced inline).

**Files to Modify/Create:**
- Create: `client/src/app/core/api/executor-registry.service.ts`, `executor-registry.types.ts`
- Create: `client/src/app/features/admin/executors/executors.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/executors/executor-form.modal.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/executors/contracts-form.modal.{ts,html,spec.ts}`
- Modify: `client/src/app/app.routes.ts` (replace placeholder `/admin/executors` route)
- Modify: `client/src/app/core/layouts/app-shell/sidebar.html` if the placeholder needs swap (or keep — link already exists)
- Create: `mockups/admin-executors.html`

**Technical Notes:**
`credentialsRef` is a string reference, not a secret — but treat the field with caution in screenshots and copy. The contracts editor uses a `FormArray` with the same pattern as T-028's memberships modal (guard the fieldset by `length === backing-length` to avoid the "unspecified name attribute" transient error). `allowedOutcomes` is a comma-separated input or a multi-select; v1 picks one — recommend comma-separated, validated to non-empty.

---

### T-033: Frontend — Executor bindings admin screen

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-030, T-026, T-028, T-032 (reuses `executor-registry.service.ts`)

**Description:**
Operator-only `/admin/executor-bindings` screen per `docs/ui-specification.md` §13. Lists active bindings as `(projectType → executor)` rows, supports create + delete. Soft delete is the only "edit" path: to change a binding you delete + re-create. The form's executor dropdown is fed by `GET /api/admin/executors?status=Active` (the API lists all by default; the UI filters client-side or uses a query param).

**Rationale:**
FEAT-003 AC-1 ("zero code changes to bind a second executor of a known shape") relies on this screen being the operator's entry point for the configuration-only path.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/admin-executor-bindings.html`: list with `projectType`, executor display name + key, status pill, "Created" column, row delete action; empty state; create modal.
- [ ] `/admin/executor-bindings` route guarded by `operatorGuard`.
- [ ] Create modal: `projectType` text input (lowercase + hyphen validator, mirroring `slug` rules), executor dropdown (active executors only).
- [ ] 409 (`duplicate active projectType`) renders inline.
- [ ] Delete via `ConfirmDialog` with a clear warning: "Existing projects of this type continue to read state, but you cannot create new ones until a new binding is in place."
- [ ] Specs: page (loads, renders rows), modal (validation, submit, 409 surface), delete (happy path + 409 if backend ever rejects).

**Files to Modify/Create:**
- Create: `client/src/app/features/admin/executor-bindings/executor-bindings.page.{ts,html,spec.ts}`
- Create: `client/src/app/features/admin/executor-bindings/binding-form.modal.{ts,html,spec.ts}`
- Modify: `client/src/app/app.routes.ts` (replace placeholder `/admin/executor-bindings`)
- Create: `mockups/admin-executor-bindings.html`

**Technical Notes:**
The "deleting a binding warning" copy lives in the `ConfirmDialog` message. The screen does not pre-fetch projects to count "how many projects use this binding" in v1 — FEAT-006 can add that affordance once the operator dashboard surfaces project counts per type.

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| Backend | 2 | T-029, T-030 |
| Testing | 1 | T-031 |
| Frontend | 2 | T-032, T-033 |
| **Total** | **5** | |

**Complexity:** S=1, M=2, L=1, XL=1.

**Critical path:** T-029 → T-030 → T-031. Frontend (T-032, T-033) parallelizes after T-030 and may merge in either order.

**Risk register:**
- **Cross-module DI cycle** — `ProjectService` (Workspace) now depends on `IExecutorRouter` (ExecutorRegistry), and `ExecutorRouter` depends on `ExecutorRegistryDbContext`. Make sure neither references the other's entities directly; the descriptor types in `DevHub.Contracts` are the only bridge. Watch for an accidental cycle if Workspace's authorization service ever wants to enrich a deny audit with executor metadata (it shouldn't — keep audit details lean).
- **`credentialsRef` leak risk** — the *reference* is fine to return; the *resolved value* must never appear in any response or log. Add a focused test in T-031 that grep-asserts the resolved-secret string is never in an HTTP response body across the suite.
- **Contract-replace atomicity** — `POST /executors/{id}/checkpoint-contracts` uses delete-then-insert; if a checkpoint signal for a contract being deleted arrives mid-replace, FEAT-004 might 404 the lookup. Acceptable in v1 (operator-driven, infrequent), but document it as a known v2 race for FEAT-004 reviewers.
- **Existing `feature-delivery` projects without a binding** — the seed data after T-024 created projects with `projectType="feature-delivery"` but no `ExecutorBinding`. T-031's `RegistryTestHelpers` must seed the binding in any test that creates projects; T-030's migration is *not* responsible for backfilling. Document in the migration plan.

## Post-Generation Checklist

- [x] All FEAT-003 acceptance criteria covered (AC-1↔T-030+T-032+T-033, AC-2↔T-030+T-031, AC-3↔T-030+T-031, AC-4↔T-030 + T-031 leak test, AC-5↔T-032).
- [x] Migrations precede services (T-029 → T-030).
- [x] Authorization extension lands inside T-030 before being consumed.
- [x] Each frontend task is mockup-first.
- [x] Dependency graph is acyclic.
- [x] No task violates the Stakeholder scope lock (no auto-discovery, no per-environment overrides beyond env-var resolution).
