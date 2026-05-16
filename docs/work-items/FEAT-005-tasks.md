# FEAT-005 Task Breakdown — Pending-Action Notifications

> Generated from `docs/work-items/FEAT-005-notifications.md` using `.ai-framework/prompts/feature-tasks.md`. 5 tasks across Backend, Testing, and Frontend.

## Scope choices locked in before generation

- **Trigger model: synchronous in-process hook, not domain events.** When WorkItems writes the `Granted` audit row after a start / signal / cancel forward, it calls `IPendingActionReconciler.RecomputeForWorkItemAsync(workItemId, projectId)` *inside the same transaction*. Sync keeps AC-1 (≤2s) trivially satisfiable on the loopback and avoids a queue/eventbus abstraction we don't need yet. The reconciler is published from `DevHub.Contracts` so WorkItems doesn't take a Notifications reference.
- **Reconciler semantics:** for a given work item, the reconciler:
  1. Reads the current `(currentStatus, currentCheckpointKey)` cache and resolves the contract via `IExecutorRouter.GetCheckpointContractAsync` (404 → no-op).
  2. If status is `WaitingOnCheckpoint`, computes the set of members who hold `contract.requiredRoleKey` on the project (via `IProjectMembershipQuery` + workspace-role assignments for operators).
  3. Upserts non-dismissed `PendingActionSignal` rows for that set; dismisses any pre-existing rows for members no longer in the set (lost role, no longer needed).
  4. If status is terminal or shifted to a different checkpoint, dismisses any non-dismissed rows for the previous checkpoint key.
- **SSE channel:** v1 uses an in-process `Channel<PendingActionEvent>`-per-member registry. The reconciler writes to every interested member's channel; the SSE controller reads from the caller's channel and serializes to `text/event-stream`. No fan-out across processes — v1 is single-host. Multi-host is a v2 problem (Redis pub/sub, NATS, etc.).
- **No replay state.** On reconnect, the client refetches `/api/notifications/pending` for the source-of-truth list (AC-3 + AC-5). The SSE stream is purely "deltas from now."
- **Channel-agnostic schema.** `PendingActionSignal` has no `channel` column in v1 — every row is implicitly the in-app channel. Adding email/webhook later is column add + worker, not a schema rewrite.
- **Backfill on membership add.** When a `ProjectMembership` is created or a role is assigned to an existing membership, Workspace calls `IPendingActionReconciler.RecomputeForMemberInProjectAsync(memberId, projectId)` so a fresh member sees existing pending-on-their-role work without waiting for the next transition.

---

## Backend

### T-042: Notifications foundation — entities + DbContext + real migration

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-009 (stub migration), T-034 (WorkItems foundation)

**Description:**
Replace T-009's empty Notifications migration with the real one. Land `PendingActionSignal` per data-model §322–338. Wire `NotificationsDbContext` with the partial-unique constraint and the per-member lookup index.

**Rationale:**
Every later FEAT-005 task reads or writes this table.

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.Notifications` creates `notifications.pending_action_signals` with all columns from data-model.md.
- [ ] UNIQUE `(member_id, work_item_id, checkpoint_key)` WHERE `dismissed_at IS NULL`.
- [ ] Index on `(member_id, project_id)` WHERE `dismissed_at IS NULL`.
- [ ] No nav properties to entities in other modules — FK columns only.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Notifications/Entities/PendingActionSignal.cs`
- Modify: `src/DevHub.Modules.Notifications/NotificationsDbContext.cs` (DbSet + fluent config + indexes)
- Replace: `src/DevHub.Modules.Notifications/Migrations/*` (real `Initial`)

**Technical Notes:**
`PendingActionSignal : BaseEntity` (`CreatedAt` covers "raised at"). `DismissedAt` is nullable timestamptz, not the soft-delete shape — these rows are kept briefly for UI fade then can be hard-purged by a FEAT-006 archive task. No nav to other modules; cross-module references are by id only.

---

### T-043: IPendingActionReconciler + WorkItems hook + Workspace backfill hook

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-042, T-036 (WorkItems services), T-024 (Workspace memberships)

**Description:**
Publish `IPendingActionReconciler` from `DevHub.Contracts`. Implementation in Notifications — `RecomputeForWorkItemAsync(workItemId, projectId)` does the upsert/dismiss work described in the scope choices above. WorkItems' `WorkItemsService` and `CheckpointSignalsService` call it inside their existing transactions, after the Granted audit row but before commit. Workspace's `MembershipService` calls `RecomputeForMemberInProjectAsync(memberId, projectId)` after add-membership / role-assignment changes so a new member backfills correctly.

**Rationale:**
AC-1 (≤2s) and AC-2 (dismiss on resolve) both fall out of "compute synchronously on transition." The Workspace backfill covers the AC-1 edge case "member added to a project that already has pending checkpoints for their role."

**Acceptance Criteria:**
- [ ] `IPendingActionReconciler.RecomputeForWorkItemAsync` upserts non-dismissed rows for members in the required-role set; dismisses rows for members not in the set; idempotent (calling twice with no state change is a no-op writing zero rows).
- [ ] Terminal status (`Completed`/`Failed`/`Cancelled`) dismisses all non-dismissed rows for that work item.
- [ ] `RecomputeForMemberInProjectAsync` raises rows for any work item in the project that is `WaitingOnCheckpoint` where the contract's `requiredRoleKey` matches the member's roles.
- [ ] Operators get a row for every pending checkpoint regardless of project membership (workspace-wide grant).
- [ ] All writes happen on the Notifications DbContext (no cross-module shared transactions — the reconciler runs in its own scope; the WorkItems caller awaits it inside its own outer tx).
- [ ] Reconciler is unit-testable in isolation (no HTTP, no SSE).

**Files to Modify/Create:**
- Create: `src/DevHub.Contracts/Notifications/IPendingActionReconciler.cs`, `PendingActionEvent.cs` (the event shape consumed by SSE)
- Create: `src/DevHub.Modules.Notifications/Services/PendingActionReconciler.cs`
- Create: `src/DevHub.Modules.Notifications/Services/PendingActionStreamRegistry.cs` (in-process channel-per-member registry — used by T-044, but registered here so the reconciler can publish)
- Modify: `src/DevHub.Modules.Notifications/NotificationsModuleExtensions.cs` (register `IPendingActionReconciler` + the registry singleton)
- Modify: `src/DevHub.Modules.WorkItems/Services/WorkItemsService.cs` — call reconciler in `StartAsync` + `CancelAsync` after Granted audit
- Modify: `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` — call reconciler in `SignalAsync` after Granted audit
- Modify: `src/DevHub.Modules.Workspace/Services/MembershipService.cs` — call `RecomputeForMemberInProjectAsync` on add / role change

**Technical Notes:**
The reconciler needs the membership-with-role query — extend `IProjectMembershipQuery` if it doesn't already expose "members of project P with role key K." That query lives in Workspace; the reconciler consumes it via Contracts. Operator-set: extend `IProjectMembershipQuery` to `GetWorkspaceOperatorsAsync()` returning all operator member ids.

Sync-call risk: the reconciler is in the WorkItems request path. If it gets slow under load, we move it behind an in-process channel later. v1 is fine because typical "members with role X on project P" sets are tiny.

---

### T-044: GET /api/notifications/pending + SSE /api/notifications/stream

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-043

**Description:**
Two endpoints on the Notifications module:
- `GET /api/notifications/pending` — authenticated, scoped to the caller. Returns all non-dismissed rows joined to project + work item for the title/slug/checkpoint display name.
- `GET /api/notifications/stream` — SSE, authenticated, scoped to the caller. Subscribes to the caller's per-member channel in `PendingActionStreamRegistry`; serializes each `PendingActionEvent` as one SSE chunk; reuses the T-037 `?access_token=` JWT shim path.

The reconciler in T-043 writes to the registry for every affected member at the end of `RecomputeForWorkItemAsync`. The stream controller reads the caller's channel and writes bytes through; no buffering, no batching beyond the natural event boundary.

**Rationale:**
AC-1 (≤2s) needs the push path; AC-3 (badge sync after tab reopen) needs the JSON list as source of truth on reconnect.

**Acceptance Criteria:**
- [ ] `GET /pending` returns `{ data: [{ projectId, projectSlug, workItemId, workItemTitle, checkpointKey, checkpointDisplayName, raisedAt }] }` per api-spec.md §Notifications.
- [ ] `GET /stream` returns `Content-Type: text/event-stream`, sets `Cache-Control: no-store` + `X-Accel-Buffering: no`, calls `Response.StartAsync` before any write.
- [ ] On client disconnect, the per-member channel reader is released; no zombie subscribers.
- [ ] Multiple concurrent stream connections per member are allowed (mobile + desktop in the same session); each gets its own reader.
- [ ] Token via `Authorization: Bearer` OR `?access_token=` (the T-037 shim already covers `/stream` paths).

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Notifications/DTOs/PendingActionDto.cs`
- Create: `src/DevHub.Modules.Notifications/Services/INotificationsQueryService.cs` + `NotificationsQueryService.cs` (joins to Workspace + WorkItems via cross-module lookups)
- Create: `src/DevHub.Modules.Notifications/Controllers/NotificationsController.cs` (pending + stream)
- Modify: `src/DevHub.Modules.Notifications/NotificationsModuleExtensions.cs` (register services + add `FrameworkReference` to AspNetCore.App for the controller — Notifications module doesn't have it yet)
- Verify: `src/DevHub.Api/Program.cs` `AddApplicationPart(typeof(NotificationsDbContext).Assembly)` already wires the new controller.

**Technical Notes:**
The `NotificationsQueryService` needs cross-module reads:
- `(workItemId → title)` — add a tiny `IWorkItemLookup` in Contracts (or extend an existing lookup).
- `(projectId → slug)` — `IProjectLookup` already exposes slug via `FindByIdAsync`.
- `(checkpointKey → displayName)` — `IExecutorRouter.GetCheckpointContractAsync` already returns this.

Stream controller pattern mirrors T-037: thin action, `Response.StartAsync`, write each event with `WriteAsync` + `FlushAsync`, dispose the reader on cancellation. `PendingActionStreamRegistry.Subscribe(memberId)` returns an `IAsyncEnumerable<PendingActionEvent>` plus an `IAsyncDisposable` cleanup that removes the reader from the per-member set.

---

### T-045: Integration tests — reconciliation correctness + AC-1..AC-5 verification

**Type:** Testing · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-044

**Description:**
Cover the reconciler's correctness in unit tests + the endpoints in integration tests + the cross-cutting ACs in a dedicated suite.

**Rationale:**
The reconciler's logic (who-gets-a-row, dismiss-on-resolve, backfill-on-add) is the most failure-prone surface in FEAT-005. AC-1 / AC-2 / AC-5 are external promises that need explicit assertions.

**Acceptance Criteria:**
- [ ] `PendingActionReconcilerTests` (unit-ish, real Postgres via Testcontainers, no HTTP):
  - WaitingOnCheckpoint upserts rows for members with the required role.
  - Operator members get rows even without a project membership.
  - Terminal status dismisses all rows for the work item.
  - Status change to a different checkpoint dismisses old rows and raises new ones.
  - Member loses required role → row is dismissed on next reconcile.
  - Idempotent: second call with no state change writes zero rows.
- [ ] `NotificationsEndpointsTests`:
  - `GET /pending` as a fresh non-operator returns rows scoped to the caller only.
  - `GET /pending` as a different caller does not leak rows.
  - `GET /pending` after a signal that resolves the checkpoint returns the dismissed row no longer.
- [ ] `NotificationStreamTests`:
  - **AC-1**: open stream, then signal a checkpoint that puts a work item into WaitingOnCheckpoint, then observe the corresponding event on the stream within 2s. Use the fake executor's scripted Signal → WaitingOnCheckpoint state.
  - **AC-2**: open stream, resolve a checkpoint, observe a dismiss event.
  - **AC-5**: open stream A, disconnect, open stream B, perform a transition — assert exactly one event arrives on B (no duplicate from A's pre-disconnect queue, no replay).
- [ ] `MembershipBackfillTests` (in Workspace tests):
  - Add a member with the reviewer role to a project that has a pending reviewer checkpoint — `GET /pending` for that member now lists the entry.

**Files to Modify/Create:**
- Create: `tests/DevHub.Modules.Notifications.Tests/PendingActionReconcilerTests.cs`
- Create: `tests/DevHub.Modules.Notifications.Tests/NotificationsEndpointsTests.cs`
- Create: `tests/DevHub.Modules.Notifications.Tests/NotificationStreamTests.cs`
- Create: `tests/DevHub.Modules.Notifications.Tests/Helpers/NotificationsTestHelpers.cs` (login + start + signal + fetch-pending + subscribe-and-await-first-event)
- Modify: `tests/DevHub.Modules.Notifications.Tests/DevHub.Modules.Notifications.Tests.csproj` (refs to Audit/Identity/Workspace/WorkItems/ExecutorRegistry)
- Create: `tests/DevHub.Modules.Workspace.Tests/MembershipBackfillTests.cs`

**Technical Notes:**
The stream-event-await helper reads from the response stream with `StreamReader.ReadLineAsync` and matches against `data: ...` lines. Wrap each await in a `CancellationTokenSource(TimeSpan.FromSeconds(3))` so a missed event fails fast.

AC-5 is the subtle one: opening + disconnecting stream A and then opening stream B is sequential in test; the reconciler's per-member writes go to whichever readers are subscribed *at the time of the write*. The disconnect must remove A's reader BEFORE B subscribes; the registry's `IAsyncDisposable` cleanup is the load-bearing piece.

---

## Frontend

### T-046: PendingActionList + sidebar badge + live SSE

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-044

**Description:**
Three deliverables on the SPA:
- `NotificationsService` (sibling to `WorkItemsService`/`WorkspaceService`) wrapping the two endpoints.
- A `PendingActionList` component on Home — replaces the FEAT-001 placeholder card.
- A sidebar group + header count badge that stay in sync app-wide, driven by a session-long SSE connection opened from the app shell.

**Rationale:**
UI Spec §2 (Home — Pending on you) + §App Shell (sidebar live group with count badge in the header). Without this UI, the backend stream isn't reachable.

**Acceptance Criteria:**
- [ ] Mockup at `mockups/home-pending-actions.html` covering: empty state, list with 3+ entries (project + work item + checkpoint chip), disconnected state (gray indicator + Reconnect), header badge variations (0 / 3 / 99+).
- [ ] `NotificationsService` ships typed `listPending()` + `streamUrl(accessToken)` helper.
- [ ] On app shell mount (post-login), opens an `EventSource` against `/api/notifications/stream` and updates a singleton signal `pendingActions = signal<PendingActionDto[]>([])`. On `error` the indicator goes red; on user click of Reconnect, reopens.
- [ ] On connect AND on every reconnect, the shell calls `listPending()` to resync (AC-3 + AC-5 — no replay; refetch is source of truth).
- [ ] Sidebar group renders the first 5 entries grouped by project; "See all" links to Home. Header badge shows total count with "99+" overflow.
- [ ] Clicking an entry navigates to the corresponding work-item review page.
- [ ] Specs:
  - service contract (list returns parsed DTOs)
  - shell wiring: connect on mount, refetch on reconnect, badge stays in sync
  - PendingActionList renders empty state and list state
  - Sidebar group renders top-5 grouped by project

**Files to Modify/Create:**
- Create: `client/src/app/core/api/notifications.{service,types}.ts`
- Modify: `client/src/app/core/layouts/app-shell/app-shell.{ts,html,spec.ts}` (mount EventSource, refetch on reconnect, expose pendingActions signal)
- Create: `client/src/app/features/home/pending-action-list.{ts,html,spec.ts}`
- Modify: `client/src/app/features/home/home.page.{html,ts}` (replace placeholder with `<pending-action-list>`)
- Modify: `client/src/app/core/layouts/app-shell/sidebar.{html,ts}` (live group + badge)
- Create: `mockups/home-pending-actions.html`

**Technical Notes:**
Sidebar badge "99+" overflow: if `count >= 100` render `99+`. Group-by-project in the sidebar: take the first 5 entries (already raisedAt-desc from the server), `groupBy` projectSlug client-side.

Reuse `StreamFeed`'s EventSource reconnect-on-error pattern; abstract just enough into a tiny `useEventSource(url)` helper in `core/auth/` if duplication starts to bite. v1 inlines.

---

## Summary

| Group | Count | Tasks |
|-------|-------|-------|
| Backend | 3 | T-042, T-043, T-044 |
| Testing | 1 | T-045 |
| Frontend | 1 | T-046 |
| **Total** | **5** | |

**Complexity:** S=1, M=3, L=1.

**Critical path:** T-042 → T-043 → T-044 → T-045 → T-046. All sequential; the frontend needs both endpoints from T-044 and the AC verification in T-045 makes the backend shippable in isolation.

**Risk register:**
- **Sync reconciler in WorkItems hot path.** The reconciler runs *inside* the WorkItems service transaction. For typical role-set sizes (≤10 members) this is fine; we ship a metrics counter so FEAT-006's operator dashboard can surface reconciler timing if it ever becomes a problem.
- **Single-host SSE.** The in-process channel registry is a single-host abstraction. Multi-host means scale-out work in v2 (Redis pub/sub or NATS); v1 explicitly assumes single-host. Document at the top of `PendingActionStreamRegistry.cs`.
- **Stream connection survives JWT expiry?** No — when the access token expires the SSE connection 401s. The shell catches `onerror`, refetches the access token via the refresh-cookie flow (existing AuthService.restore), then reopens. Document explicitly; flag for FEAT-006 if friction shows up.
- **`raisedAt` clock skew.** The reconciler uses `DateTimeOffset.UtcNow` on the server. Client sorts by `raisedAt` — same clock. No skew worry.
- **Membership reconciliation race.** If a `ProjectMembership.Add` and a `WorkItem.Signal` happen concurrently, the AfterSave hooks run in their own scopes; the partial-unique index protects against double-insert. Document that "the same member may briefly see a pending row dismiss + raise within the same second" — a known harmless artefact.
- **Operator inbox volume.** An operator sees every pending checkpoint across the workspace. For v1 small workspaces that's fine; for production workspaces with 1000+ work items it's a UX problem. Add a small "show only my project memberships" toggle on the PendingActionList — defer to FEAT-006 polish.

## Post-Generation Checklist

- [x] All FEAT-005 ACs map to specific tasks (AC-1↔T-043+T-045, AC-2↔T-043+T-045, AC-3↔T-044+T-046, AC-4 is a measurement promise, not a tasked behavior — covered by the surface itself, AC-5↔T-045).
- [x] Migrations precede services (T-042 → T-043).
- [x] Cross-module contracts published before consumption (T-043's `IPendingActionReconciler` before WorkItems hooks).
- [x] SSE endpoint reuses the T-037 JWT shim path.
- [x] Frontend task is mockup-first.
- [x] Dependency graph is acyclic.
- [x] No task violates the Stakeholder scope lock (no email/webhook channels, no snoozing, no "pending on my team").
