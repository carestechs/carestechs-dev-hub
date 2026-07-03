# FEAT-013 — Project GitHub Integration

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-013 |
| **Name** | Project GitHub integration — repo link + auto-creation |
| **Target Version** | Continuous |
| **Status** | Open |
| **Priority** | High |
| **Requested By** | Carlos |
| **Date Created** | 2026-07-01 |

---

## 2. User Story

**As an** operator setting up a DevHub project, **I want** to see a direct link to the project's GitHub repository on the project page, and optionally have DevHub create the GitHub repo automatically when I create the project, **so that** I don't have to leave DevHub to find the repo and I can skip manual repo-creation steps.

---

## 3. Goals

**Milestone 1 — Repo link on project page**
When a project has a `repo` set (`owner/name`), display a clickable link to `https://github.com/{repo}` on the project home page (in `ProjectHeader`). The link opens in a new tab.

**Milestone 2 — Auto-create GitHub repo on project creation**
Add an optional "Create GitHub repository" toggle to the project creation form. When enabled, DevHub calls the GitHub REST API to create the repo under the configured organisation/user, and persists the resulting `owner/name` back on the project. Requires a GitHub PAT stored in application configuration.

---

## 4. Architectural Decisions

### 4.1 Auth model — PAT stored in app config (not per-project)

A single GitHub PAT is configured at the application level (`GitHub:Pat` in `appsettings` / env var `GitHub__Pat`). All repo-creation calls use this token. Per-project tokens are out of scope for this FEAT; the PAT approach is sufficient for a single-org setup and can be replaced by a GitHub App later.

### 4.2 Repo creation is fire-and-forget at project creation time

If the GitHub API call fails, the project is still created (without the `repo` field set) and an error is surfaced to the operator. DevHub does not retry automatically; the operator can re-run repo creation via a future endpoint or set `repo` manually via `PATCH /api/projects/{id}`.

### 4.3 Repo creation lives in the Workspace module

`GitHubService` is a new service inside `DevHub.Modules.Workspace`. It depends only on `IHttpClientFactory` and the PAT from `IConfiguration`. It is not exposed via `DevHub.Contracts` — it is an internal Workspace concern.

### 4.4 Repo name derived from project name

The GitHub repo name is derived by slugifying the project name (lowercase, spaces → hyphens, strip special chars). The operator sees the derived name before submitting and can override it in the creation form.

### 4.5 Org vs user-owned repo

A single `GitHub:Owner` config key specifies the org or user under which repos are created (e.g. `carestechs`). Combined with the repo name to produce `owner/repoName`.

---

## 5. Feature Scope

### 5.1 Included

#### Backend
- `GitHubService` in `DevHub.Modules.Workspace` — wraps GitHub REST `POST /orgs/{org}/repos` (org) or `POST /user/repos` (user). Reads `GitHub:Pat` and `GitHub:Owner` from config. Returns `owner/name` on success, throws `GitHubApiException` on failure.
- `ProjectsService.CreateAsync` extended — accept optional `createGitHubRepo: bool` + optional `repoName: string` in `CreateProjectRequest`. When `true` and PAT is configured, call `GitHubService` after the project row is saved; persist the returned `owner/name` into `project.Repo`. Failure logs a warning and returns the project without `repo` set — does not roll back project creation.
- `IConfiguration` binding: `GitHub:Pat` (required for repo creation), `GitHub:Owner` (required for repo creation). Both default to empty string; creation toggle is disabled in the UI when the backend reports GitHub integration is unconfigured.
- New endpoint `GET /api/integrations/github/status` — returns `{ "configured": bool }`. Used by the frontend to decide whether to show the repo-creation toggle.

#### Frontend
- `ProjectHeader` — when `project.repo` is set, render a GitHub link chip (octocat icon + `owner/name`) that opens `https://github.com/{repo}` in a new tab.
- `CreateProjectModal` / project creation form — add a "Create GitHub repository" toggle (shown only when `GET /api/integrations/github/status` returns `configured: true`). When toggled on, show a repo name input pre-filled with the slugified project name. On submit, include `createGitHubRepo` and `repoName` in the request.

### 5.2 Excluded

- GitHub App auth (PAT only for this FEAT)
- Per-project PAT override
- Automatic retry if GitHub API creation fails
- Repo deletion when a project is deleted
- Repo settings (private/public, topics, description) beyond what GitHub defaults — except `private: true` is always set
- Branch protection rules
- Any CI pipeline setup

---

## 6. Acceptance Criteria

- **AC-1:** When `project.repo` is set, `ProjectHeader` displays a GitHub link chip that opens `https://github.com/{repo}` in a new tab.
- **AC-2:** When `project.repo` is null, no link is shown (no empty chip).
- **AC-3:** `GET /api/integrations/github/status` returns `{ "configured": true }` when `GitHub:Pat` and `GitHub:Owner` are non-empty in config, `false` otherwise.
- **AC-4:** Project creation form shows the repo-creation toggle only when GitHub is configured.
- **AC-5:** Creating a project with `createGitHubRepo: true` results in a GitHub repo created under `GitHub:Owner`, the project's `repo` field populated with `owner/name`, and the project page showing the GitHub link immediately.
- **AC-6:** If the GitHub API call fails (e.g. repo already exists, PAT expired), the project is still created, `repo` remains null, and the API response includes a `warnings` array with a `githubRepoCreationFailed` entry.
- **AC-7:** Creating a project with `createGitHubRepo: false` (or omitting the field) behaves identically to the current flow — no GitHub API call is made.

---

## 7. Entity / API / UI Impact

### Entity changes
None — `Project.repo` already exists (FEAT-008). No migration needed.

### API changes
- `POST /api/projects` — accept optional `createGitHubRepo: bool` and `repoName: string` in request body.
- `POST /api/projects` response — add optional `warnings: string[]` field to `ProjectDto` for non-fatal creation side-effects.
- New: `GET /api/integrations/github/status` → `{ "configured": bool }`.

### UI changes
- `ProjectHeader` — add GitHub link chip when `repo` is set.
- Project creation form — add toggle + repo name input (conditional on `configured`).

---

## 8. Dependencies

| Dependency | Direction |
|------------|-----------|
| FEAT-008 — `Project.repo` + `defaultBranch` | Must be complete (already is) |
| GitHub REST API — `POST /orgs/{org}/repos` | External |
