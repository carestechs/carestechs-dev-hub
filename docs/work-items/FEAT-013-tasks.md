# FEAT-013 Tasks — Project GitHub Integration

---

## Group 1: Foundation

### T-001: GitHub config binding + status endpoint

**Type:** Backend
**Workflow:** standard
**Complexity:** S
**Dependencies:** None

**Description:**
Add `GitHub:Pat` and `GitHub:Owner` to `appsettings.json` (empty defaults) and bind them via `IOptions<GitHubOptions>`. Add a new controller `IntegrationsController` in `DevHub.Modules.Workspace` with `GET /api/integrations/github/status` returning `{ "configured": bool }` — true when both values are non-empty.

**Rationale:**
AC-3 and AC-4 require the frontend to know whether GitHub integration is available before rendering the repo-creation toggle.

**Acceptance Criteria:**
- [ ] `GET /api/integrations/github/status` returns `{ "configured": false }` when env vars are absent
- [ ] Returns `{ "configured": true }` when `GitHub__Pat` and `GitHub__Owner` are set in the environment
- [ ] Endpoint requires no authentication (public read — the flag is not sensitive)
- [ ] `GitHubOptions` record lives in `DevHub.Modules.Workspace`

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Options/GitHubOptions.cs` — new record `{ string Pat; string Owner; }`
- `src/DevHub.Api/appsettings.json` — add `"GitHub": { "Pat": "", "Owner": "" }`
- `src/DevHub.Modules.Workspace/Controllers/IntegrationsController.cs` — new controller
- `src/DevHub.Modules.Workspace/WorkspaceModule.cs` (or equivalent DI registration) — register `IOptions<GitHubOptions>`

**Technical Notes:**
Bind with `services.Configure<GitHubOptions>(configuration.GetSection("GitHub"))`. The endpoint is intentionally unauthenticated — `configured: true/false` reveals nothing sensitive.

---

### T-002: GitHubService — repo creation

**Type:** Backend
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-001

**Description:**
Add `GitHubService` inside `DevHub.Modules.Workspace` that wraps the GitHub REST API `POST /orgs/{org}/repos` (or `POST /user/repos` when the owner is a user, not an org). Uses `IHttpClientFactory` with a named client `"github"`. Returns the created repo's `full_name` (`owner/name`) on success; throws `GitHubApiException` (new domain exception) on non-2xx responses.

**Rationale:**
AC-5 and AC-6 require a clean service boundary around the GitHub API call so `ProjectsService` can call it and handle failure without leaking HTTP concerns.

**Acceptance Criteria:**
- [ ] `CreateRepoAsync(string repoName, CancellationToken ct)` calls GitHub API with `Authorization: Bearer {pat}`, `private: true`, repo name from arg
- [ ] On 201 returns `owner/repoName` string
- [ ] On non-201 throws `GitHubApiException` with the GitHub error message
- [ ] Named HttpClient `"github"` has `BaseAddress = https://api.github.com`, `User-Agent = DevHub`, `Accept = application/vnd.github+json`
- [ ] Owner is read from `GitHubOptions.Owner`; determines org vs user endpoint by checking if owner matches `GitHub:OrgMode` config (default: `true` = use org endpoint)

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/Services/GitHubService.cs` — new service
- `src/DevHub.Modules.Workspace/Exceptions/GitHubApiException.cs` — new exception
- `src/DevHub.Modules.Workspace/WorkspaceModule.cs` — register named HttpClient + `IGitHubService`

**Technical Notes:**
Use `JsonPropertyName` attributes on the request/response DTOs rather than `System.Text.Json` global options. Keep the DTO types private (internal to `GitHubService`). GitHub requires `User-Agent` header — omitting it returns 403.

---

### T-003: Extend CreateProjectRequest + ProjectsService for repo creation

**Type:** Backend
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-002

**Description:**
Extend `CreateProjectRequest` DTO with optional `CreateGitHubRepo: bool` and `RepoName: string?`. In `ProjectsService.CreateAsync`, after the project row is saved and committed, call `GitHubService.CreateRepoAsync` when the flag is true. On success set `project.Repo = returnedFullName`. On `GitHubApiException` log a warning, leave `project.Repo` null, and append `"githubRepoCreationFailed"` to a `Warnings` list on the returned DTO.

**Rationale:**
AC-5, AC-6, AC-7 — the project must be created regardless of GitHub API outcome; failure is surfaced as a non-fatal warning.

**Acceptance Criteria:**
- [ ] `POST /api/projects` with `createGitHubRepo: true` and `repoName: "my-repo"` creates the project AND the GitHub repo, returning `project.repo = "owner/my-repo"`
- [ ] GitHub API failure leaves `project.repo` null and response includes `warnings: ["githubRepoCreationFailed"]`
- [ ] `createGitHubRepo: false` (or absent) makes no GitHub API call — existing behaviour unchanged
- [ ] `RepoName` is validated: lowercase, alphanumeric + hyphens, max 100 chars, matches `^[a-z0-9][a-z0-9-]*$`
- [ ] Audit entry includes `repoCreated: true/false` in details

**Files to Modify/Create:**
- `src/DevHub.Modules.Workspace/DTOs/ProjectDtos.cs` — add `CreateGitHubRepo`, `RepoName`, `Warnings` fields
- `src/DevHub.Modules.Workspace/Services/ProjectsService.cs` — call GitHubService after commit
- `src/DevHub.Modules.Workspace/Controllers/ProjectsController.cs` — pass new fields through

**Technical Notes:**
Call GitHub *after* `SaveChangesAsync` so the project row exists even if GitHub fails. Do not wrap the GitHub call in the DB transaction. `RepoName` defaults to the slugified project name when omitted — add a `SlugifyRepoName(string projectName)` static helper.

---

## Group 2: Frontend

### T-004: GitHub status API service method

**Type:** Frontend
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-001

**Description:**
Add `getGitHubStatus(): Observable<{ configured: boolean }>` to the projects API service (or a new `integrations.service.ts`). Add `warnings?: string[]` to the `ProjectDto` TypeScript interface.

**Rationale:**
The creation form (T-005) and the project header (T-006) need typed access to the new endpoints.

**Acceptance Criteria:**
- [ ] `GET /api/integrations/github/status` is called and typed correctly
- [ ] `ProjectDto` interface includes `warnings?: string[]`
- [ ] `ProjectSummaryDto` interface includes `repo?: string | null`  (already present from FEAT-008 — verify)

**Files to Modify/Create:**
- `client/src/app/core/api/integrations.service.ts` — new service (or extend `projects.service.ts`)
- `client/src/app/core/api/projects.types.ts` — add `warnings` to `ProjectDto`

---

### T-005: Project creation form — repo creation toggle

**Type:** Frontend
**Workflow:** mockup-first
**Complexity:** M
**Dependencies:** T-003, T-004

**Description:**
Extend the project creation modal/form with a "Create GitHub repository" toggle that appears only when `configured: true`. When toggled on, show a repo name input pre-filled with the slugified project name (reactive: updates as the user types the project name). On submit include `createGitHubRepo` and `repoName` in the request body. After successful creation, if `warnings` includes `"githubRepoCreationFailed"`, show a toast warning "Project created but GitHub repo creation failed."

**Rationale:**
AC-4, AC-5, AC-6 — operator must be able to trigger repo creation from the UI and get feedback when it fails.

**Acceptance Criteria:**
- [ ] Toggle not shown when `configured: false`
- [ ] Toggle shown and enabled when `configured: true`
- [ ] Repo name input pre-fills from project name (slugified: lowercase, spaces → hyphens)
- [ ] Repo name input is editable and validated (pattern `^[a-z0-9][a-z0-9-]*$`, max 100 chars)
- [ ] Submitting with toggle on sends `createGitHubRepo: true` and `repoName` in request
- [ ] Warning toast shown when response includes `warnings: ["githubRepoCreationFailed"]`
- [ ] No change to form when toggle is off

**Files to Modify/Create:**
- `client/src/app/features/projects/components/create-project-modal.ts` — add toggle + repo name signal, load github status
- `client/src/app/features/projects/components/create-project-modal.html` — add UI elements

**Technical Notes:**
Load GitHub status once on modal open (not on every keystroke). Slugify helper: `name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')`. Use `signal()` for `githubConfigured`, `createRepo`, `repoName`.

---

### T-006: ProjectHeader — GitHub repo link chip

**Type:** Frontend
**Workflow:** standard
**Complexity:** S
**Dependencies:** T-004

**Description:**
In `ProjectHeader`, when `project.repo` is non-null, render a small chip with a GitHub icon (SVG inline) and the `owner/name` text that links to `https://github.com/{repo}` opening in a new tab. Place it in the metadata strip alongside the project type badge.

**Rationale:**
AC-1 and AC-2 — the reviewer / developer needs a one-click path to the repo from the project page.

**Acceptance Criteria:**
- [ ] Chip visible when `project.repo` is set
- [ ] Chip absent when `project.repo` is null/undefined
- [ ] Link opens `https://github.com/{repo}` in a new tab with `rel="noopener"`
- [ ] Chip uses the project design language (same height as project-type badge, sky-600 colour)

**Files to Modify/Create:**
- `client/src/app/features/projects/components/project-header.ts` — add `repo` computed from project input
- `client/src/app/features/projects/components/project-header.html` — add chip

---

## Group 3: Testing

### T-007: Unit tests — GitHubService + ProjectsService extension

**Type:** Testing
**Workflow:** standard
**Complexity:** M
**Dependencies:** T-003

**Description:**
Add unit tests for `GitHubService` (mock `IHttpClientFactory`) and integration tests for `ProjectsService.CreateAsync` with the GitHub path — success case (mock GitHubService returns `owner/repo`), failure case (mock throws `GitHubApiException`).

**Rationale:**
CLAUDE.md testing convention: every new service method needs unit tests; authorization deny paths are mandatory.

**Acceptance Criteria:**
- [ ] `GitHubService.CreateRepoAsync` success path returns correct `owner/name`
- [ ] `GitHubService.CreateRepoAsync` non-201 response throws `GitHubApiException`
- [ ] `ProjectsService.CreateAsync` with `createGitHubRepo: true`: repo created, `project.Repo` set
- [ ] `ProjectsService.CreateAsync` with GitHub failure: project saved, `Warnings` contains `"githubRepoCreationFailed"`
- [ ] `ProjectsService.CreateAsync` with `createGitHubRepo: false`: GitHub service never called

**Files to Modify/Create:**
- `tests/DevHub.Modules.Workspace.Tests/GitHubServiceTests.cs` — new
- `tests/DevHub.Modules.Workspace.Tests/ProjectsServiceGitHubTests.cs` — new

---

## Summary

| Type | Count |
|------|-------|
| Backend | 3 (T-001, T-002, T-003) |
| Frontend | 3 (T-004, T-005, T-006) |
| Testing | 1 (T-007) |
| **Total** | **7** |

**Complexity distribution:** 3× S, 3× M, 1× M

**Critical path:** T-001 → T-002 → T-003 → T-005 (mockup-first, needs approval before implementation)

**Risks / open questions:**
- `GitHub:Owner` — is it an org or a user account? Affects which GitHub endpoint is called. Default assumption: org. Worth confirming before T-002.
- PAT scope — needs `repo` scope (or `public_repo` for public repos). Must be documented in the env setup guide.
- Repo visibility — defaulting to `private: true`. Confirm this is correct.
