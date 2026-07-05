# FEAT-016 — Doc Repo Sync

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-016 |
| **Name** | Doc Repo Sync — repo write on initial lock, work-item final-state updates |
| **Target Version** | Continuous |
| **Status** | Completed |
| **Priority** | High |
| **Requested By** | Carlos |
| **Date Created** | 2026-07-04 |
| **Depends On** | FEAT-015, FEAT-013 |

---

## 2. User Story

**As an** operator who just finished the initial doc fill for a project, **I want** the locked doc content to be automatically pushed to the project's GitHub repo, **so that** the repo reflects what was captured in DevHub from day one — without a manual copy-paste step.

**As a** team member whose work item has finished, **I want** any doc section updates produced by that work item to be reflected in both DevHub and the repo automatically, **so that** the project's framework documents stay in sync with what the work item delivered.

---

## 3. Goals

Two complementary sync paths:

1. **Lock → repo write.** The moment a project's docs transition to locked (all required sections filled), DevHub pushes each doc's assembled content to the project's GitHub repo as a Markdown file. This creates the canonical initial version in the repo.

2. **Work-item Completed → pull from repo.** When a work item reaches the `Completed` terminal state, DevHub reads each project doc file directly from the GitHub repo and updates `project_doc_sections` with whatever content is now in the repo. This means an AI executor (or any contributor) can commit updated Markdown directly to the repo during the work item; DevHub will pick up those changes on completion. DevHub's DB stays authoritative after the pull; the repo commit is the update mechanism.

---

## 4. Architectural Decisions

### 4.1 Repo write is triggered at the service layer, not the controller

`ProjectDocsService.UpsertDocSectionsAsync` already detects the lock transition (the moment `filledCount >= allVersionRequired.Count` becomes true). The repo push fires from that same method, immediately after `SaveChangesAsync`, when the transition is detected for the first time. Idempotent: if content is already identical in the repo, the GitHub API returns 200 with no new commit.

### 4.2 Doc files are assembled from sections

Each doc key maps to one Markdown file in the repo (e.g. `docs/stakeholder-definition.md`). The file content is assembled by iterating sections in `display_order`, rendering each as a `## Section Label\n\ncontent` block. The assembler lives in a static helper on `ProjectDocsService` so both the lock-transition write and the work-item write use the same output.

### 4.3 Pull-from-repo on work-item completion (design pivot)

> **Note:** The original design called for the executor to push `docSections` in the final-state payload. This was replaced during implementation: executors commit changes directly to the project's GitHub repo, and DevHub pulls those changes back into the DB on work-item `Completed`.

When `CheckpointSignalsService.SignalAsync` receives a response with `CurrentStatus == "Completed"`, it fires a background task (`TryPullDocsFromRepoAsync`) that:

1. Creates a fresh DI scope (the request scope is disposed by the time the background task runs).
2. Resolves `IProjectDocSyncService` from the new scope.
3. Calls `PullDocsFromRepoAsync(projectId, CancellationToken.None)` — `CancellationToken.None` because the HTTP request's token is cancelled the moment the 200 response is delivered to the client.

`PullDocsFromRepoAsync` reads each of the 7 doc files from the repo via `IGitHubService.GetFileContentAsync`, parses them with `ParseDocMarkdown` (which maps `## Label` headers back to section IDs using the template's Label field), and updates `project_doc_sections` in place.

### 4.4 Signal handler drives the doc pull

`CheckpointSignalsService` (in `DevHub.Modules.WorkItems`) is the entry point. It detects the `Completed` terminal status after the executor responds and fires the background pull. Cross-module call goes through `IProjectDocSyncService` in `DevHub.Contracts`.

### 4.5 GitHub write reuses the existing GitHub client from FEAT-013

FEAT-013 introduced a `GitHubService` (or equivalent) for repo creation. The same client is used here for file upserts (`PUT /repos/{owner}/{repo}/contents/{path}`). If the project has no repo (`projects.repo` is null), the sync step is silently skipped and a debug log line is emitted — no error, no retry.

### 4.6 Repo write is fire-and-forget within the transaction boundary

The `SaveChangesAsync` call completes first (doc sections persisted). The GitHub push happens after, outside the DB transaction. If the push fails, the section write is not rolled back — DevHub is authoritative; the repo is a derived artifact. The failure is logged as a warning and an audit entry is written with outcome `Failed`.

### 4.7 No unlock — the lock state is permanent

The work-item doc write does not change the locked status. `locked` remains true because all required sections are still filled (just with updated content). There is no "unlock" operation in scope.

---

## 5. Data Model Changes

No new tables. No changes to the executor final-state schema.

### New: `IProjectDocSyncService` interface in `DevHub.Contracts`

```csharp
public interface IProjectDocSyncService
{
    Task<bool?> PullDocsFromRepoAsync(Guid projectId, CancellationToken ct);
}
```

Implemented by `ProjectDocsService` in `DevHub.Modules.Workspace`. Registered and resolved cross-module via DI.

### Modified: `IGitHubService` — new `GetFileContentAsync` method

```csharp
Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct);
```

Returns `null` on 404 (file not found), throws `GitHubApiException` on other non-2xx responses.

---

## 6. API Changes

No new public endpoints.

### Modified behaviour: `PUT /api/projects/{id}/docs/{key}`

When this call causes the lock transition (first time all required sections become filled), the response body gains a top-level `repoSynced: bool` field indicating whether the GitHub push succeeded. This is informational — callers should not branch on it.

### Work-item completion triggers a pull (no executor contract change)

Executors do not need to emit any special field. They commit changes to the project repo directly; DevHub reads those changes back on `Completed`. No executor contract coordination required.

---

## 7. Business Rules

| Rule | Detail |
|------|--------|
| Repo write only when repo and defaultBranch are set | If either is null, skip silently. No error. |
| Lock write is one-time | The repo push only fires at the lock transition (first time all required sections filled). Subsequent `PUT` calls return 409. |
| Pull updates all sections found in the repo | On `Completed`, DevHub reads all 7 doc files. Each file's parsed sections overwrite the matching `project_doc_sections` rows. Sections not mentioned in the repo file are left unchanged. |
| Repo files are missing → skip silently | 404 from `GetFileContentAsync` is logged at Debug level and the pull continues for the remaining docs. |
| Pull failure is non-fatal | If the pull fails (GitHub error, parse error), the signal response is unaffected. Failure is logged as a warning. |
| Repo is the update mechanism after lock | Executors commit changes to the repo; DevHub picks them up on `Completed`. The `PUT` lock gate stays in place for operators. |
| Repo files assembled in section display order | Sections are rendered as `## Label\n\ncontent`. Doc key maps to filename via the `RepoFilePaths` dict in `ProjectDocsService`. |

---

## 8. File Path Conventions

| `doc_key` | Repo file path |
|-----------|----------------|
| `stakeholder-definition` | `docs/stakeholder-definition.md` |
| `architecture` | `docs/ARCHITECTURE.md` |
| `data-model` | `docs/data-model.md` |
| `api-spec` | `docs/api-spec.md` |
| `ui-specification` | `docs/ui-specification.md` |
| `primary-user-persona` | `docs/personas/primary-user.md` |
| `claude-md` | `CLAUDE.md` |

The mapping is a static dictionary in the sync helper. `CLAUDE.md` is written to the repo root; all others go under `docs/`.

---

## 9. Feature Scope

### Included

- Repo write on lock transition (all required sections first filled)
- Section assembly into Markdown files using `display_order` and section labels
- `IGitHubService.GetFileContentAsync` for reading repo files
- `IProjectDocSyncService.PullDocsFromRepoAsync` contract interface in `DevHub.Contracts`
- `ParseDocMarkdown` static helper in `ProjectDocsService` (maps `## Label` headers back to section IDs)
- Fire-and-forget pull in `CheckpointSignalsService` on `Completed` status (fresh DI scope, `CancellationToken.None`)
- `repoSynced` field on the PUT response at the lock transition
- Audit entries for repo push success and failure
- Silent skip when `projects.repo` or `projects.defaultBranch` is null

### Excluded

- Manual re-trigger of repo sync from the UI (future)
- Diff view / PR-based repo update instead of direct commit (future)
- Conflict resolution if repo file was edited outside DevHub (future)
- Per-branch targeting (writes and reads always use the project's `defaultBranch`)

---

## 10. Acceptance Criteria

### Lock → repo write
- [x] When the last required section is filled via `PUT /api/projects/{id}/docs/{key}`, each of the 7 doc files is created or updated in the project's GitHub repo.
- [x] Repo files are assembled in section `display_order`; each section is rendered as `## Label\n\ncontent`.
- [x] `CLAUDE.md` is written to repo root; all other docs written under `docs/`.
- [x] If `projects.repo` is null, the call succeeds with `repoSynced: false` and no error.
- [x] If the GitHub push fails (rate limit, auth failure), the PUT still returns 200; failure is logged and written to the audit log.

### Work-item Completed → pull from repo
- [x] When `CheckpointSignalsService.SignalAsync` receives `CurrentStatus == "Completed"`, `PullDocsFromRepoAsync` is fired in a background scope using `CancellationToken.None`.
- [x] `PullDocsFromRepoAsync` reads each doc file via `GetFileContentAsync`, parses `## Label` headers, and updates `project_doc_sections` in the DB.
- [x] Files not found in the repo (404) are silently skipped; the pull continues for the remaining docs.
- [x] If the repo pull throws (GitHub down, credentials invalid), the signal response is unaffected — failure is logged as a warning.
- [x] Non-`Completed` terminal states (e.g. `Failed`) do not trigger a pull.
- [x] The `PUT /api/projects/{id}/docs/{key}` endpoint still returns 409 for locked projects; the repo-commit path is the only way to update content after lock.

### Tests
- [x] Integration test: fill all required sections → assert GitHub client received 7 file upsert calls.
- [x] Integration test: work item signals Completed → stub content updated externally → `project_doc_sections` updated from stub.
- [x] Unit test: `AssembleDocMarkdown` helper renders sections in order with correct headings.
- [x] Integration test: GitHub push failure → PUT still returns 200, audit entry has outcome `Failed`.
- [x] Integration test: `projects.repo` is null → lock write completes, `repoSynced: false`, no GitHub call.
- [x] Real GitHub E2E: initial push creates files; external commit updates `ARCHITECTURE.md`; work item Completed → DB updated from GitHub.

---

## 11. Edge Cases

| Scenario | Behaviour |
|----------|-----------|
| GitHub push times out | DB write committed; timeout logged as warning; `repoSynced: false` in response. |
| Section content is whitespace-only | Normalised to null before write; not rendered in the assembled file. |
| Repo file not found (404) on pull | Skipped silently; other doc files are still pulled. |
| Two work items complete concurrently | Last write wins per section (standard EF upsert). No lock-level coordination needed. |
| Project's `defaultBranch` is not set | Skip repo push and pull; `repoSynced: false` on initial push. |
| Repo file already exists with identical content | GitHub API returns 200 with no new commit; counted as success on push. |
| Request cancellation token fires before pull completes | Pull uses `CancellationToken.None`; it runs to completion regardless of HTTP connection state. |

---

## 12. Coordination Notes

- **Orchestrator team**: No executor contract change required. Executors that want to update project docs during a work item should commit changes directly to the project's GitHub repo. DevHub will pull those changes back on `Completed`. No new fields, no schema coordination.
- **FEAT-013 GitHub integration**: `GitHubService` now exposes both `UpsertFileAsync` (write) and `GetFileContentAsync` (read), covering the full sync cycle.

---

## 13. Traceability

| Reference | Value |
|-----------|-------|
| Persona | Operator setting up a project; AI executor completing a work item |
| Depends on | FEAT-015 — Versioned Doc Templates (section model + lock) |
| Depends on | FEAT-013 — GitHub Repo Auto-Creation (GitHub client + repo field) |
| Blocks | Any feature that reads project docs from the repo as AI context |
| Related | Future: manual sync re-trigger, PR-based doc updates |
