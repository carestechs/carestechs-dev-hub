# Implementation Plan: T-039 — Project home work-items table + StartWorkModal

## Task Reference
- **Task ID:** T-039 · **Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M
- **Rationale:** First place a project member sees DevHub working. Replaces the FEAT-002 "Work items land in FEAT-004" placeholder.

## Overview
Three deliverables:
1. `WorkItemsService` (sibling to `WorkspaceService`/`ExecutorRegistryService`).
2. `WorkItemTable` mounted on `ProjectHomePage`, with status filter + "Waiting on me" toggle.
3. `StartWorkModal` — opaque JSON input, posts to `POST /api/projects/{id}/work-items`.

## Implementation Steps

### Step 0: Mockup
**File:** `mockups/project-home-with-workitems.html` · Create

Extend the existing `project-home.html`. Add:
- "Start work →" primary button in the header.
- Filter row: status pill bar (`All` / `Running` / `WaitingOnCheckpoint` / `Completed` / `Failed` / `Cancelled`) + "Waiting on me" toggle.
- `AppTable` with columns Title (link), Status pill, Waiting on (role chip), Updated.
- Empty state: "No work items yet. Start your first one."
- StartWorkModal: title input + JSON textarea + Cancel/Start buttons.

### Step 1: Types + service
**Files (Create):**
- `client/src/app/core/api/work-items.types.ts`

```ts
export type WorkItemStatus = string; // executor-defined; treat as opaque string

export interface ExecutorRef { id: string; key: string; displayName: string; }
export interface MemberRef { id: string; displayName: string; }

export interface WorkItemSummaryDto {
  id: string; projectId: string; title: string;
  currentStatus: WorkItemStatus; currentCheckpointKey?: string;
  executor: ExecutorRef; executorCorrelationMarker: string;
  createdAt: string; createdBy: MemberRef;
}
export interface WorkItemDto extends WorkItemSummaryDto {
  executorState: unknown;
  signals: CheckpointSignalDto[];
}
export interface CheckpointSignalDto {
  id: string; checkpointKey: string; outcome: string;
  signaledBy: MemberRef; signaledAt: string;
  executorResponseStatus?: number; payload?: unknown;
}

export interface StartWorkItemRequest { title: string; input: unknown; }
export interface SignalRequest { outcome: string; payload?: unknown; }
```

- `client/src/app/core/api/work-items.service.ts`

```ts
@Injectable({ providedIn: 'root' })
export class WorkItemsService {
  private readonly http = inject(HttpClient);
  list(projectId: string, req: PageRequest & { status?: string; waitingOnMe?: boolean } = {}): Promise<PagedEnvelope<WorkItemSummaryDto>> { /* GET */ }
  get(projectId: string, id: string): Promise<WorkItemDto> { /* GET, unwrap */ }
  start(projectId: string, body: StartWorkItemRequest): Promise<WorkItemDto> { /* POST */ }
  signal(projectId: string, workItemId: string, key: string, body: SignalRequest, idempotencyKey: string): Promise<WorkItemDto> {
    return firstValueFrom(this.http.post<Envelope<WorkItemDto>>(
      `/api/projects/${projectId}/work-items/${workItemId}/checkpoints/${key}/signal`,
      body,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )).then(env => env.data);
  }
  listSignals(projectId: string, workItemId: string, req: PageRequest = {}): Promise<PagedEnvelope<CheckpointSignalDto>> { /* GET */ }
  cancel(projectId: string, workItemId: string): Promise<void> { /* POST .../cancel */ }
  streamUrl(projectId: string, workItemId: string, accessToken: string): string {
    return `/api/projects/${projectId}/work-items/${workItemId}/stream?access_token=${encodeURIComponent(accessToken)}`;
  }
}
```

### Step 2: WorkItemTable component
**Files (Create):**
- `client/src/app/features/projects/work-items/work-item-table.{ts,html,spec.ts}`

Inputs: `rows: WorkItemSummaryDto[]`, `loading`, `error`, `meta`. Outputs: `pageChanged`, `rowClicked`. Renders `AppTable` with the four columns. Status pill maps known values to color (`Running` → sky, `WaitingOnCheckpoint` → amber, `Completed` → emerald, `Failed` → red, `Cancelled` → slate); unknown values render as a neutral slate pill.

### Step 3: StartWorkModal
**Files (Create):**
- `client/src/app/features/projects/work-items/start-work.modal.{ts,html,spec.ts}`

Form: `title` (required, ≤255), `input` (textarea, JSON, validated by `JSON.parse` on submit). On submit emits `{ title, input }`. Renders 409 + 502 inline via `AppErrorBanner`.

### Step 4: Wire into ProjectHomePage
**File:** `client/src/app/features/projects/project-home.page.ts` (and `.html`) · Modify

- Remove the placeholder.
- Add signals: `workItems`, `workItemsLoading`, `workItemsError`, `workItemsPage`, `statusFilter`, `waitingOnMe`.
- Load via `WorkItemsService.list(...)` after the project loads. Refilter on `statusFilter` / `waitingOnMe` changes.
- `+ Start work` button opens `StartWorkModal`. On success, refresh the list + navigate to the new work item's detail page (T-040 lands the route).
- Row click navigates to `/projects/:slug/work-items/:id` (placeholder route until T-040, but the link is real).

### Step 5: Specs
**File:** `client/src/app/features/projects/project-home.page.spec.ts` · Modify
Add two cases to the existing spec: "loads work items after project load" and "Start button opens the modal, submits, refreshes." Microtask flushing follows the FEAT-002 pattern.

**File:** `start-work.modal.spec.ts` · Create
JSON-parse error surfaced, submit emits payload, 409 surfaced inline.

**File:** `work-item-table.spec.ts` · Create
Renders rows, pagination event emits, row click emits.

## Files Affected
| File | Action |
|------|--------|
| `core/api/work-items.{types,service}.ts` | Create |
| `features/projects/work-items/work-item-table.{ts,html,spec.ts}` | Create |
| `features/projects/work-items/start-work.modal.{ts,html,spec.ts}` | Create |
| `features/projects/project-home.page.{ts,html,spec.ts}` | Modify |
| `mockups/project-home-with-workitems.html` | Create |

## Edge Cases & Risks
- **Start-role pre-check** — v1 ships the affordance gap: button is always enabled for members; 403 surfaces in the modal. Document; T-041 can refine.
- **JSON input UX** — a plain `<textarea>` is the v1 shape. If executors evolve to ship JSON Schemas, FEAT-006 can render a schema-driven form. The textarea ships with a "Validate" affordance that runs `JSON.parse` on blur.
- **Status filter** — values are executor-defined strings. The pill bar hardcodes the conventional five; if an executor reports something exotic the row renders the raw string in a neutral pill and the filter doesn't include it. Acceptable v1; FEAT-006 can derive the pill set from observed statuses.

## Acceptance Verification
- [ ] Mockup approved.
- [ ] `ng build` clean.
- [ ] `ng test` is green; new spec count ≥ 5.
- [ ] Manual smoke: log in as operator, open a project, click Start, paste `{}` as input, submit, see the new row appear.
