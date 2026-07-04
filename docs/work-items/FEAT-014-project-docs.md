# FEAT-014 — Project Documentation (AI Framework Docs as First-Class Citizens)

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-014 |
| **Name** | Project documentation — framework docs in DevHub |
| **Target Version** | Continuous |
| **Status** | Completed |
| **Priority** | High |
| **Requested By** | Carlos |
| **Date Created** | 2026-07-03 |

---

## 2. User Story

**As an** operator starting a new project, **I want** to fill in the project's framework documents (stakeholder definition, architecture, data model, etc.) directly inside DevHub, **so that** the team has a single place to find all project context and work items cannot be started until the project is properly documented.

---

## 3. Goals

Promote the AI framework documents from "files in a repo" to first-class project data inside DevHub. Every project has a fixed set of seven documents. All seven must be filled before any work item can be created. Documents can be filled in any order and incrementally — the gate applies only at work-item creation time.

---

## 4. Architectural Decisions

### 4.1 Docs stored in DevHub, not in the repo

Documents are stored in the `project_docs` table inside the Workspace module's PostgreSQL database. The repo scaffold (`FEAT-013`) remains a convenience copy for Claude Code and git history; the authoritative version of each doc lives here.

### 4.2 Fixed doc set, not user-defined

Seven document types are hard-coded in the application. They map 1:1 to the AI framework scaffold:

| `doc_key` | Doc |
|-----------|-----|
| `stakeholder-definition` | Why? For whom? Scope? |
| `architecture` | System structure & components |
| `data-model` | Entities, fields, relationships |
| `api-spec` | Endpoint contracts & DTOs |
| `ui-specification` | Screens, components, interactions |
| `primary-user-persona` | Who is the primary user? |
| `claude-md` | Implementation conventions (CLAUDE.md) |

### 4.3 All seven docs are mandatory gate for work item creation

`WorkItemService.CreateAsync` checks that all seven `project_docs` rows exist and have non-empty `content` before forwarding to the executor. Returns `409 Conflict` with type `/probs/project-docs-incomplete` when the gate is not met.

### 4.4 Operator-only write, any member read

Creating or updating a doc requires the operator role. Any project member can read docs (same visibility rules as the project itself).

### 4.5 Plain-text storage, no parsing

Content is stored as a plain UTF-8 text blob. DevHub does not parse or validate Markdown structure. The editor is a plain textarea in v1; rich editing is a future improvement.

---

## 5. Feature Scope

### 5.1 Included

- `project_docs` table and EF Core migration
- `ProjectDocsService` with list, get, upsert
- REST endpoints: `GET /projects/{id}/docs`, `GET /projects/{id}/docs/{key}`, `PUT /projects/{id}/docs/{key}`
- "Docs" tab on the project home page listing all seven docs with filled/empty status
- Per-doc edit page (full-page, plain textarea)
- Work item creation gate (all seven docs must be filled)
- Amber banner on the project home page when docs are incomplete (operator only)
- Audit entry on every doc upsert

### 5.2 Excluded

- Markdown preview / rich text editor (future)
- Doc versioning / history (future)
- Per-doc access control beyond operator-write / member-read (future)
- Syncing doc content back to the GitHub repo (future)
- Configurable required doc sets per project type (future)

---

## 6. Acceptance Criteria

### 6.1 Doc storage
- [ ] A `project_docs` table exists: `id (uuid PK)`, `project_id (uuid FK)`, `doc_key (varchar)`, `content (text)`, `updated_at (timestamptz)`, `updated_by_id (uuid FK → members)`. Unique constraint on `(project_id, doc_key)`.
- [ ] Seven valid `doc_key` values are enforced at the API layer (400 on unknown key).

### 6.2 API
- [ ] `GET /projects/{id}/docs` returns a list of `{ key, label, filledAt, updatedByName }` for all seven keys. Keys with no row yet are returned with `filledAt: null`.
- [ ] `GET /projects/{id}/docs/{key}` returns full content + metadata.
- [ ] `PUT /projects/{id}/docs/{key}` upserts content. Requires operator role. Writes an audit entry.
- [ ] All three endpoints enforce project-level authorization (`project:read` for GET, `project:update` for PUT).

### 6.3 Work item gate
- [ ] `POST /projects/{id}/work-items` returns `409` with `type: /probs/project-docs-incomplete` when fewer than all seven docs have content.
- [ ] The 409 response includes a `missingDocs` array listing which keys are unfilled.
- [ ] An operator can see which docs are missing on the project home page before hitting the gate.

### 6.4 UI — Docs tab
- [ ] A "Docs" tab appears on the project home page.
- [ ] The tab lists all seven docs as cards with a filled (green) / empty (amber) status badge.
- [ ] Operators see an edit button per card; non-operators see a read-only view button.
- [ ] An amber banner at the top of the project home page (operator-only) lists unfilled docs when any are missing.

### 6.5 UI — Doc editor
- [ ] Clicking a doc card opens a full-page editor route (`/projects/{slug}/docs/{key}`).
- [ ] The editor shows the doc label, a description/hint of what to write, and a plain textarea.
- [ ] Save triggers `PUT /projects/{slug}/docs/{key}` and navigates back to the Docs tab on success.
- [ ] Cancel navigates back without saving.
- [ ] Non-operators see the content read-only (no textarea, no save button).

---

## 7. Entity / API / UI Impact

### Data Model
- New entity: `ProjectDoc` (`project_docs` table)
- No changes to existing entities

### API Spec
- New sub-resource: `GET/PUT /projects/{id}/docs` and `GET/PUT /projects/{id}/docs/{key}`
- Modified: `POST /projects/{id}/work-items` — new 409 response variant

### UI Specification
- Modified: `project-home.page` — new "Docs" tab, amber incomplete banner
- New: `project-docs.page` — doc list tab content
- New: `project-doc-editor.page` — full-page doc editor

---

## 8. Edge Cases & Constraints

- Empty string content (`""` or whitespace-only) is treated as unfilled — same as no row.
- A doc saved and then cleared back to empty re-opens the gate.
- Deleting a project cascades to its `project_docs` rows.
- The `claude-md` key stores the content of `CLAUDE.md`; the key name avoids the dot.
