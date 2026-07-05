# Tasks — FEAT-016: Doc Repo Sync

Generated: 2026-07-04  
Feature Brief: `docs/work-items/FEAT-016-doc-repo-sync.md`  
Branch: `feat/doc-repo-sync`

---

## Group 1 — Foundation

---

### T-001: Extend `IGitHubService` with `UpsertFileAsync`

**Type:** Backend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** None

**Description:**  
Add a `UpsertFileAsync` method to `IGitHubService` and implement it in `GitHubService`. The method writes a file to a GitHub repo via the Contents API (`PUT /repos/{owner}/{repo}/contents/{path}`). It must handle both create (no existing SHA) and update (existing SHA required) cases by first attempting a HEAD/GET to retrieve the current file SHA before writing.

**Rationale:**  
The lock-transition push and the work-item doc update both need to write Markdown files to the project repo. `GitHubService` currently only supports `CreateRepoAsync`; this extends it to cover file upserts.

**Acceptance Criteria:**
- [ ] `IGitHubService` has `Task UpsertFileAsync(string repo, string path, string content, string branch, string commitMessage, CancellationToken ct)`.
- [ ] When the file does not exist in the repo, the implementation creates it (no SHA in request body).
- [ ] When the file already exists, the implementation fetches its SHA via `GET /repos/{owner}/{repo}/contents/{path}` and includes it in the PUT body.
- [ ] On non-2xx response from the GitHub API, `GitHubApiException` is thrown with the status code and response body.
- [ ] `repo` is expected as `owner/name` (e.g., `carestechs/my-project`); the owner prefix is split from the options org only when `repo` is a bare name.

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/GitHubService.cs` — add interface method + implementation
- `src/DevHub.Modules.Workspace/Services/GitHubService.cs` — add private `GetFileShaAsync` helper

**Technical Notes:**  
GitHub Contents API: `GET /repos/{owner}/{repo}/contents/{path}` returns `{ "sha": "..." }` if the file exists and 404 if it does not. The PUT body requires `{ "message": "...", "content": "<base64>", "branch": "...", "sha": "<existing sha if update>" }`. Content must be base64-encoded (`Convert.ToBase64String(Encoding.UTF8.GetBytes(content))`). The `repo` param may be `owner/name` — split on `/` to get owner and name separately; if only a name (no `/`) is supplied, use the configured org.

---

### T-002: Add `IProjectDocSyncService` interface to `DevHub.Contracts`

**Type:** Backend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** None

**Description:**  
Create a new interface `IProjectDocSyncService` in `DevHub.Contracts/Workspace/`. This is the cross-module seam that lets `DevHub.Modules.WorkItems` call doc sync operations on `DevHub.Modules.Workspace` without a direct reference, following the existing pattern of `IProjectDocsQuery`, `IProjectAuthorizationService`, etc.

**Rationale:**  
`CheckpointSignalsService` (in `WorkItems`) must trigger a doc write (in `Workspace`) when a work item completes. The contract interface enforces the module-boundary rule: cross-module calls go through `DevHub.Contracts` interfaces only.

**Acceptance Criteria:**
- [ ] Interface exists at `DevHub.Contracts/Workspace/IProjectDocSyncService.cs`.
- [ ] Interface declares `Task ApplyWorkItemDocUpdateAsync(Guid projectId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> docSections, Guid actingMemberId, CancellationToken ct)`.
- [ ] Interface declares `Task PushAllDocsToRepoAsync(Guid projectId, CancellationToken ct)` (used internally at lock transition — exposed for testability).
- [ ] No implementation logic in this file.

**Files to Modify/Create:**
- `src/DevHub.Contracts/Workspace/IProjectDocSyncService.cs` — new file

**Technical Notes:**  
Use `IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>` rather than `Dictionary<...>` for the interface boundary — callers pass what they have; the implementation can copy to mutable dicts internally.

---

## Group 2 — Backend

---

### T-003: Implement `AssembleDocMarkdown` static helper

**Type:** Backend  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** None

**Description:**  
Add a `static string AssembleDocMarkdown(string docLabel, IEnumerable<(string Label, string? Content)> sections)` helper to `ProjectDocsService` (or a companion `DocMarkdownAssembler` static class). It renders a full doc as Markdown: a `# Doc Label` heading followed by each section as `## Section Label\n\ncontent`, separated by blank lines. Sections with null/whitespace content render the heading only (no content block).

**Rationale:**  
Both the lock-transition push (7 files) and the work-item doc update (partial files) need to produce Markdown from section data. Centralizing the assembly logic means both paths produce identical output and the format is testable in isolation.

**Acceptance Criteria:**
- [ ] `AssembleDocMarkdown` returns a string starting with `# {docLabel}`.
- [ ] Each section is rendered as `## {Label}` followed by the content (or nothing if content is null/whitespace).
- [ ] Sections are rendered in the order supplied by the caller (display order).
- [ ] A blank line separates consecutive sections.
- [ ] Method is `internal static` and has a dedicated unit test class.

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — add `internal static string AssembleDocMarkdown(...)` at the bottom of the file

**Technical Notes:**  
Use a `StringBuilder` for concatenation. Include a trailing newline so the file ends with `\n` (Git convention). The file-path mapping (`doc_key` → repo path) lives separately in the caller, not in this helper.

---

### T-004: Implement `IProjectDocSyncService` on `ProjectDocsService`

**Type:** Backend  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-001, T-002, T-003

**Description:**  
Implement `IProjectDocSyncService` on `ProjectDocsService`. Add two methods:

1. **`PushAllDocsToRepoAsync`** — loads all sections for a project (joined with content rows), assembles Markdown for each of the 7 doc keys using `AssembleDocMarkdown`, and calls `IGitHubService.UpsertFileAsync` for each file. Silently skips if `projects.repo` is null or `projects.defaultBranch` is null. On GitHub failure, logs a warning and writes an audit entry with `outcome: Failed`; does not throw.

2. **`ApplyWorkItemDocUpdateAsync`** — for each doc key in `docSections`: looks up template sections for the project's pinned version, upserts content rows in `project_doc_sections` (same upsert logic as `UpsertDocSectionsAsync` but without the locked guard), then calls `PushAllDocsToRepoAsync` to re-push the affected file. Unknown doc keys or section keys are skipped with a warning audit entry; the method completes normally regardless.

**Rationale:**  
These are the two sync paths described in the feature brief (§3). Implementing them on `ProjectDocsService` avoids a new service class and reuses the existing DB context and `IGitHubService` dependency.

**Acceptance Criteria:**
- [ ] `PushAllDocsToRepoAsync` pushes up to 7 files using the path map from §8 of the brief (`CLAUDE.md` at root, others under `docs/`).
- [ ] `PushAllDocsToRepoAsync` is a no-op (no error, no GitHub call) when `projects.repo` is null.
- [ ] `PushAllDocsToRepoAsync` is a no-op when `projects.defaultBranch` is null.
- [ ] `ApplyWorkItemDocUpdateAsync` writes only supplied sections; absent sections are untouched.
- [ ] Unknown doc keys in `docSections` produce a warning-level log line + audit entry with `outcome: Failed`; method continues for remaining keys.
- [ ] Unknown section keys within a known doc key produce the same warning behavior.
- [ ] `ProjectDocsService` declares `IProjectDocSyncService` in its implements list.

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — implement interface, add two methods, inject `IGitHubService`
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — add `private static readonly IReadOnlyDictionary<string, string> RepoFilePaths` mapping doc keys to repo paths

**Technical Notes:**  
File path map:
```csharp
private static readonly IReadOnlyDictionary<string, string> RepoFilePaths =
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["stakeholder-definition"] = "docs/stakeholder-definition.md",
        ["architecture"]           = "docs/ARCHITECTURE.md",
        ["data-model"]             = "docs/data-model.md",
        ["api-spec"]               = "docs/api-spec.md",
        ["ui-specification"]       = "docs/ui-specification.md",
        ["primary-user-persona"]   = "docs/personas/primary-user.md",
        ["claude-md"]              = "CLAUDE.md",
    };
```
`IGitHubService` is already registered and injected into `ProjectService`; add it as a constructor param to `ProjectDocsService` too. `PushAllDocsToRepoAsync` does **not** run inside a DB transaction — the DB write (if any) was committed before this is called.

---

### T-005: Wire lock-transition repo push into `UpsertDocSectionsAsync`

**Type:** Backend  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-004

**Description:**  
Modify `ProjectDocsService.UpsertDocSectionsAsync` to detect the lock transition — the moment a `PUT` call causes all required sections to become filled for the first time — and fire `PushAllDocsToRepoAsync` immediately after `SaveChangesAsync`. The transition is detected by comparing the pre-save and post-save locked state: if docs were not locked before the call and are locked after, the push fires. Add `RepoSynced` to `ProjectDocDto` so the caller knows whether the push succeeded.

**Rationale:**  
The feature brief specifies that the initial repo write fires at the lock transition (§3, §4.1). `UpsertDocSectionsAsync` is the only public write path on docs, so this is the correct injection point.

**Acceptance Criteria:**
- [ ] `RepoSynced: bool?` is added to `ProjectDocDto` (null when no push attempt was made; true/false when a push was attempted).
- [ ] When the call does not cause a lock transition, `RepoSynced` is null and no GitHub call is made.
- [ ] When the call causes a lock transition and `projects.repo` is set, `PushAllDocsToRepoAsync` is called and `RepoSynced` reflects its success.
- [ ] A GitHub push failure does not cause `UpsertDocSectionsAsync` to throw or roll back the DB write.
- [ ] An audit entry with `action: "project:docs-repo-synced"` and `outcome: Granted` or `Failed` is written post-commit when a push is attempted.

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/ProjectDocsService.cs` — lock-transition detection + push trigger
- `src/DevHub.Modules.Workspace/DTOs/ProjectDocDtos.cs` — add `bool? RepoSynced` to `ProjectDocDto`
- `client/src/app/core/api/workspace.types.ts` — add `repoSynced?: boolean` to `ProjectDocDto`

**Technical Notes:**  
Pre-save locked state: check if all required sections are filled *before* applying the new content. Post-save: re-query (or infer from the new content map). Simplest approach: compute `wasLocked` before the upsert loop, then compute `isNowLocked` after `SaveChangesAsync` using the same logic already in `ListAsync`. The push fires only when `!wasLocked && isNowLocked`.

---

### T-006: Register `IProjectDocSyncService` in the workspace DI registration

**Type:** Backend  
**Workflow:** standard  
**Complexity:** XS  
**Dependencies:** T-004

**Description:**  
Register `ProjectDocsService` as `IProjectDocSyncService` in `WorkspaceModuleExtensions.cs` so it can be resolved by `CheckpointSignalsService` in `DevHub.Modules.WorkItems`.

**Rationale:**  
The cross-module DI pattern requires the implementation to be registered against the contract interface at composition-root time. Without this, the `WorkItems` module cannot resolve `IProjectDocSyncService`.

**Acceptance Criteria:**
- [ ] `WorkspaceModuleExtensions.AddWorkspaceModule` registers `ProjectDocsService` as `IProjectDocSyncService` (scoped, same lifetime as other services in the module).
- [ ] The registration is alongside the existing `IProjectDocsService` and `IProjectDocsQuery` registrations for the same concrete type.
- [ ] `DevHub.Api` builds without error.

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs` — add `services.AddScoped<IProjectDocSyncService, ProjectDocsService>()`

**Technical Notes:**  
`ProjectDocsService` is already registered as `IProjectDocsService` and `IProjectDocsQuery`. Adding a third `AddScoped` for `IProjectDocSyncService` pointing to the same concrete type is correct — ASP.NET Core DI resolves each interface to its own scope-tracked instance, which is fine here since all three share the same `WorkspaceDbContext` scope.

---

### T-007: Detect terminal status and apply doc sync in `CheckpointSignalsService`

**Type:** Backend  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-002, T-006

**Description:**  
After the DB transaction commits in `CheckpointSignalsService.SignalAsync`, check whether `signalResp.CurrentStatus` is `"Completed"`. If so, attempt to extract a `docSections` property from `signalResp.ExecutorState` (a `JsonElement`) and, if found, deserialize it as `Dictionary<string, Dictionary<string, string>>` and call `IProjectDocSyncService.ApplyWorkItemDocUpdateAsync`. Wrap the entire post-commit doc sync in a try/catch — a failure must not affect the signal response returned to the caller.

**Rationale:**  
The feature brief specifies that the work-item final state drives the doc update (§3, §4.4). `CheckpointSignalsService.SignalAsync` is the point where DevHub receives the terminal status from the executor. Only `"Completed"` triggers the sync — `"Failed"` and `"Cancelled"` do not, as a failed work item should not update docs.

**Acceptance Criteria:**
- [ ] `IProjectDocSyncService` is injected into `CheckpointSignalsService`.
- [ ] When `signalResp.CurrentStatus == "Completed"` and `ExecutorState` has a `docSections` property, `ApplyWorkItemDocUpdateAsync` is called.
- [ ] When `signalResp.CurrentStatus` is `"Failed"` or `"Cancelled"`, no doc sync is attempted.
- [ ] When `docSections` is absent or null in `ExecutorState`, no doc sync is attempted.
- [ ] A malformed `docSections` value (not a valid object structure) is caught, logged as a warning, and does not throw.
- [ ] The `SignalAsync` return value and response status are unaffected by doc-sync failures.

**Files to Modify/Create:**
- `src/DevHub.Modules.WorkItems/Services/CheckpointSignalsService.cs` — inject `IProjectDocSyncService`, add post-commit doc sync block
- `src/DevHub.Modules.WorkItems/WorkItemsModuleExtensions.cs` — confirm `IProjectDocSyncService` is resolvable (no change needed if already registered in Workspace module, but verify)

**Technical Notes:**  
Extract `docSections` from `ExecutorState`:
```csharp
if (signalResp.ExecutorState.ValueKind == JsonValueKind.Object
    && signalResp.ExecutorState.TryGetProperty("docSections", out var docSectionsEl)
    && docSectionsEl.ValueKind == JsonValueKind.Object)
{
    var docSections = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(docSectionsEl.GetRawText());
    if (docSections is { Count: > 0 })
        await _docSync.ApplyWorkItemDocUpdateAsync(projectId, docSections, actingMemberId, ct);
}
```
Keep this in a separate `try/catch` block so a deserialization failure or sync failure does not surface to the caller. The `reconciler.RecomputeForWorkItemAsync` call that follows the existing commit should remain after this block.

---

## Group 3 — Testing

---

### T-008: Unit tests for `AssembleDocMarkdown`

**Type:** Testing  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-003

**Description:**  
Add a focused unit-test class `DocMarkdownAssemblerTests` in `DevHub.Modules.Workspace.Tests`. Test the `AssembleDocMarkdown` helper in isolation — no DB, no HTTP.

**Rationale:**  
The Markdown assembler is pure logic with no dependencies, making it the easiest thing to pin with unit tests before the integration tests run against real DB + mock GitHub.

**Acceptance Criteria:**
- [ ] Test: single section with content → correct `# Doc\n\n## Section\n\ncontent\n` output.
- [ ] Test: section with null/whitespace content → heading rendered, no content block.
- [ ] Test: multiple sections → blank line between sections, order matches input.
- [ ] Test: trailing newline always present.

**Files to Modify/Create:**
- `tests/DevHub.Modules.Workspace.Tests/DocMarkdownAssemblerTests.cs` — new file, no `[Collection]` (no Postgres needed)

---

### T-009: Integration tests — lock transition fires repo push

**Type:** Testing  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-005

**Description:**  
Add integration tests to `ProjectDocsTests.cs` (or a new `ProjectDocRepoSyncTests.cs`) that verify the repo push fires correctly at the lock transition. Use a fake/mock `IGitHubService` registered in the test `DevHubApiFactory` to capture calls without hitting the real GitHub API.

**Rationale:**  
The lock-transition push is the primary sync path. Integration tests confirm the full stack — HTTP → service → DB → GitHub client — works end-to-end, including the silent-skip behavior when repo is unset.

**Acceptance Criteria:**
- [ ] Test: fill all required sections on a project with `repo` set → fake `IGitHubService.UpsertFileAsync` called 7 times, one per doc key, with correct paths.
- [ ] Test: `repoSynced: true` in the PUT response at the lock transition.
- [ ] Test: project with no `repo` set → no `UpsertFileAsync` calls, `repoSynced: false`, PUT returns 200.
- [ ] Test: `UpsertFileAsync` throws `GitHubApiException` → PUT still returns 200, `repoSynced: false`, audit entry with `outcome: Failed` written.
- [ ] Test: subsequent PUT after lock (409) → no `UpsertFileAsync` calls.

**Files to Modify/Create:**
- `tests/DevHub.Modules.Workspace.Tests/ProjectDocRepoSyncTests.cs` — new test class
- `tests/DevHub.TestHarness/DevHubApiFactory.cs` — add `FakeGitHubService` substitution option (or a `SpyGitHubService` that records calls)

**Technical Notes:**  
Register a `SpyGitHubService : IGitHubService` in the test factory that records all `UpsertFileAsync` calls and optionally throws. The factory option `UseFakeGitHub = true` registers the spy; the test accesses the spy via DI or a static call counter. Do not hit `api.github.com` in any test.

---

### T-010: Integration tests — work item terminal state triggers doc update

**Type:** Testing  
**Workflow:** standard  
**Complexity:** M  
**Dependencies:** T-007, T-009

**Description:**  
Add integration tests that verify a checkpoint signal leading to `"Completed"` status with a `docSections` payload in `ExecutorState` causes `ApplyWorkItemDocUpdateAsync` to run and the DB + repo to be updated.

**Rationale:**  
The work-item final-state path is the ongoing update channel. These tests verify the entire chain: signal → terminal status detected → `docSections` extracted → DB updated → repo pushed.

**Acceptance Criteria:**
- [ ] Test: signal returns `CurrentStatus: "Completed"` with `ExecutorState: { docSections: { "architecture": { "system-overview": "updated" } } }` → `project_doc_sections` row updated, fake `UpsertFileAsync` called for `docs/ARCHITECTURE.md`.
- [ ] Test: signal returns `CurrentStatus: "Failed"` with `docSections` in state → no DB write, no GitHub call.
- [ ] Test: signal returns `CurrentStatus: "Completed"` with no `docSections` in state → no DB write, no GitHub call.
- [ ] Test: `docSections` contains an unknown doc key → signal returns 200, unknown key skipped, known keys applied.
- [ ] Test: `UpsertFileAsync` throws during doc sync → signal endpoint still returns 200 with the updated work-item state.

**Files to Modify/Create:**
- `tests/DevHub.Modules.WorkItems.Tests/WorkItemDocSyncTests.cs` — new test class

**Technical Notes:**  
The `FakeExecutor` in the test harness controls what `ExecutorSignalResponse` is returned. Extend it to accept a `JsonElement` for `ExecutorState` so tests can inject `docSections`. The existing `FakeExecutor` or `UseFakeExecutor = true` factory path is the right extension point.

---

### T-011: Integration test — `projects.defaultBranch` null → silent skip

**Type:** Testing  
**Workflow:** standard  
**Complexity:** S  
**Dependencies:** T-009

**Description:**  
Add one integration test that verifies: when a project has a repo set but `defaultBranch` is null, the lock-transition push is silently skipped (no GitHub call, `repoSynced: false`, no error).

**Rationale:**  
The feature brief lists "project's `defaultBranch` is not set → skip repo write" as an explicit edge case (§11). It is cheap to test and guards against a NullReferenceException regression.

**Acceptance Criteria:**
- [ ] Test: project with `repo = "org/name"` and `defaultBranch = null`, fill all required sections → `repoSynced: false`, no `UpsertFileAsync` call, PUT returns 200.

**Files to Modify/Create:**
- `tests/DevHub.Modules.Workspace.Tests/ProjectDocRepoSyncTests.cs` — add test to existing class from T-009

---

## Summary

| # | Task | Type | Complexity | Dependencies |
|---|------|------|-----------|-------------|
| T-001 | Extend `IGitHubService` with `UpsertFileAsync` | Backend | S | — |
| T-002 | Add `IProjectDocSyncService` contract interface | Backend | S | — |
| T-003 | Implement `AssembleDocMarkdown` static helper | Backend | S | — |
| T-004 | Implement `IProjectDocSyncService` on `ProjectDocsService` | Backend | M | T-001, T-002, T-003 |
| T-005 | Wire lock-transition push into `UpsertDocSectionsAsync` | Backend | M | T-004 |
| T-006 | Register `IProjectDocSyncService` in workspace DI | Backend | XS | T-004 |
| T-007 | Detect terminal status and apply doc sync in `CheckpointSignalsService` | Backend | M | T-002, T-006 |
| T-008 | Unit tests — `AssembleDocMarkdown` | Testing | S | T-003 |
| T-009 | Integration tests — lock transition fires repo push | Testing | M | T-005 |
| T-010 | Integration tests — work item terminal state triggers doc update | Testing | M | T-007, T-009 |
| T-011 | Integration test — `defaultBranch` null → silent skip | Testing | S | T-009 |

**Total: 11 tasks**  
Backend: 7 · Testing: 4  
XS: 1 · S: 5 · M: 4

**Critical path:**  
T-001 → T-004 → T-005 → T-009 → T-010  
(GitHub client → sync service → lock-trigger → repo push tests → doc update tests)

**Parallel work:**  
T-002 and T-003 have no dependencies and can start immediately alongside T-001.  
T-008 can start as soon as T-003 is done, in parallel with T-004.  
T-006 and T-007 can proceed once T-004 is merged.

**Risks / Open Questions:**
1. **`FakeExecutor` extension** — T-010 requires the test fake executor to return a configurable `ExecutorState` JSON. If the current `FakeExecutor` hardcodes the state response, it needs to be made configurable. Assess during T-010; may add a small setup cost.
2. **`IGitHubService` injection into `ProjectDocsService`** — `ProjectDocsService` currently does not receive `IGitHubService`. Adding it is straightforward, but verify the constructor injection doesn't break the existing test factory setup (which may register a null/fake GitHub service).
3. **Orchestrator team coordination** — The `docSections` field in `ExecutorState` is an orchestrator-side concern. T-007 can be implemented and deployed independently (the extraction is no-op when the field is absent), but the end-to-end path only works once the orchestrator starts emitting it. Flag this in the PR description.
