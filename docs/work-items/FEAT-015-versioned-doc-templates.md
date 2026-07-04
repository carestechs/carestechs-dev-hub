# FEAT-015 — Versioned Doc Templates

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-015 |
| **Name** | Versioned Doc Templates — structured, evolvable framework document schemas |
| **Target Version** | Continuous |
| **Status** | In Progress |
| **Priority** | High |
| **Requested By** | Carlos |
| **Date Created** | 2026-07-03 |
| **Depends On** | FEAT-014 |

---

## 2. User Story

**As an** operator setting up or filling in a project's framework documents, **I want** each document type to have a defined set of required sections with labels and hints, **so that** the content is structured, consistent, and useful to AI agents — and so that the structure can evolve over time without breaking existing projects.

---

## 3. Goals

Replace the free-text blob per document with a versioned, section-based schema stored in the database. Each project is pinned to the template version active at creation time. New template versions can be created without invalidating existing project docs. The gate check and editor both operate against the project's pinned version.

---

## 4. Architectural Decisions

### 4.1 Template stored in the database, not hard-coded

The set of doc keys and their sections is stored in `doc_template_sections` rather than being hard-coded in application logic. This allows the structure to evolve without a code change — only a new version row and section rows are needed.

### 4.2 Project pinned to active version at creation

`projects.doc_template_version_id` is set when the project is created and is immutable thereafter. This guarantees that the gate check and the editor always operate against the schema the operator was shown when they set up the project. Version upgrades do not retroactively change existing projects.

### 4.3 `project_doc_sections` replaces `project_docs`

FEAT-014 introduced `project_docs` (one row per project per doc key). This feature drops that table and replaces it with `project_doc_sections` (one row per project per section). The `doc_key` grouping now comes from `doc_template_sections.doc_key` joined through `section_id`.

### 4.4 Version 1 seeded via data migration

The first deploy creates version 1 via an EF Core data migration (not code seeding). The 22 sections across 7 doc keys are inserted as rows. Because `project_docs` is empty in production (FEAT-014 was never deployed), no content migration is needed.

### 4.5 Admin template management is operator-only

Creating a new template version and activating it are admin operations accessed via `/api/admin/doc-templates`. No UI for editing section definitions from within DevHub is in scope — the canonical definition lives in the migration seed and admins create new versions by creating a new migration or via the admin API.

---

## 5. Data Model

### New tables

#### `doc_template_versions`

| Column | Type | Constraint |
|--------|------|-----------|
| `id` | `uuid` | PK |
| `version_number` | `int` | auto-increment, unique |
| `is_active` | `bool` | at most one row `true` |
| `notes` | `text` | nullable |
| `created_at` | `timestamptz` | not null |

#### `doc_template_sections`

| Column | Type | Constraint |
|--------|------|-----------|
| `id` | `uuid` | PK |
| `version_id` | `uuid` | FK → `doc_template_versions` |
| `doc_key` | `varchar(80)` | not null |
| `section_key` | `varchar(80)` | not null |
| `label` | `varchar(200)` | not null |
| `hint` | `text` | nullable |
| `required` | `bool` | not null |
| `display_order` | `int` | not null |

Unique constraint on `(version_id, doc_key, section_key)`.

#### `project_doc_sections`

| Column | Type | Constraint |
|--------|------|-----------|
| `id` | `uuid` | PK |
| `project_id` | `uuid` | FK → `projects`, cascade delete |
| `section_id` | `uuid` | FK → `doc_template_sections` |
| `content` | `text` | nullable |
| `updated_at` | `timestamptz` | nullable |
| `updated_by_id` | `uuid` | FK → `members`, nullable |

Unique constraint on `(project_id, section_id)`.

### Modified tables

#### `projects`

New column: `doc_template_version_id uuid NOT NULL FK → doc_template_versions`.

Set at project creation to the currently active version. Immutable thereafter.

### Dropped tables

- `project_docs` (introduced by FEAT-014, never deployed to production)

---

## 6. Version 1 Seed

The data migration inserts version 1 with `is_active = true` and the following 22 sections:

| `doc_key` | `section_key` | Label | Required |
|-----------|--------------|-------|---------|
| `stakeholder-definition` | `business-problem` | Business Problem | ✓ |
| `stakeholder-definition` | `success-criteria` | Success Criteria | ✓ |
| `stakeholder-definition` | `scope-lock` | Scope Lock | ✓ |
| `stakeholder-definition` | `guiding-principles` | Guiding Principles | ✓ |
| `architecture` | `style` | Architectural Style | ✓ |
| `architecture` | `modules` | Modules & Responsibilities | ✓ |
| `architecture` | `data-flow` | Data Flow | ✓ |
| `data-model` | `entities` | Core Entities | ✓ |
| `data-model` | `relationships` | Relationships | ✓ |
| `data-model` | `constraints` | Business Constraints | ✓ |
| `api-spec` | `endpoints` | Endpoint List | ✓ |
| `api-spec` | `dtos` | Request / Response DTOs | ✓ |
| `api-spec` | `errors` | Error Responses | ✓ |
| `ui-specification` | `screens` | Screen Inventory | ✓ |
| `ui-specification` | `components` | Key Components | ✓ |
| `ui-specification` | `interactions` | Interactions & States | ✓ |
| `primary-user-persona` | `profile` | Role & Background | ✓ |
| `primary-user-persona` | `goals` | Goals & Pain Points | ✓ |
| `primary-user-persona` | `behaviours` | Behaviours & Context | ✓ |
| `claude-md` | `stack` | Tech Stack & Commands | ✓ |
| `claude-md` | `patterns` | Patterns to Follow | ✓ |
| `claude-md` | `antipatterns` | Anti-Patterns to Avoid | ✓ |

---

## 7. Business Rules

| Rule | Detail |
|------|--------|
| Exactly one active version | At most one row may have `is_active = true`. Activating a new version deactivates the previous one atomically. |
| Projects pinned at creation | `projects.doc_template_version_id` is set at creation and is immutable. |
| New projects use the active version | If no active version exists, project creation returns 409. |
| Gate check is version-aware | `IProjectDocsQuery.CheckAllFilledAsync` queries only `required = true` sections belonging to the project's pinned version. |
| Sections are append-only within a version | A version's sections are never edited. Changes create a new version. |
| Optional sections do not block the gate | `required = false` sections are shown in the editor but excluded from the gate check. |
| A doc is filled when all required sections are filled | A section is filled when `content` is non-null and non-whitespace. |

---

## 8. API Changes

### Modified endpoints

| Endpoint | Change |
|----------|--------|
| `GET /api/projects/{id}/docs` | Returns `sections[]` per doc key. `filled` per doc key means all required sections are filled. |
| `GET /api/projects/{id}/docs/{key}` | Returns `sections[]` array (key, label, hint, required, content, filledAt) instead of a single content blob. |
| `PUT /api/projects/{id}/docs/{key}` | Body changes from `{ content: string }` to `{ sections: { [sectionKey]: string } }`. Partial saves allowed. |
| `POST /api/projects/{id}/work-items` | Gate logic updated to query `project_doc_sections` against the project's pinned version. Response shape unchanged. |

### New endpoints

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/admin/doc-templates` | GET | Operator | List all template versions with section counts |
| `/api/admin/doc-templates` | POST | Operator | Create a new template version |
| `/api/admin/doc-templates/{id}/activate` | POST | Operator | Activate a version (deactivates previous) |

---

## 9. UI Changes

| Screen | Status | Change |
|--------|--------|--------|
| Doc editor `/projects/:slug/docs/:key` | Modified | One labeled textarea per section. Save sends `{ sections: {...} }`. |
| Docs tab on project home | Modified (minor) | Fill progress shown per doc card (e.g. "3 / 4 sections"). Filled = all required sections filled. |
| Admin — Doc Templates | New | Operator-only page listing versions, active indicator, section count, and projects pinned to each version. |

---

## 10. Feature Scope

### Included

- Four new tables + drop of `project_docs`
- Version 1 seeded as a data migration (22 sections, 7 doc keys)
- Project pinned to active version at creation
- Section-based doc editor (one textarea per section)
- Partial save (only supplied sections updated)
- Gate check updated to be version- and section-aware
- Admin: list versions, create new version, activate
- Fill progress per doc card ("3/4 sections")

### Excluded

- Rich text / Markdown preview (future)
- Diff view between template versions (future)
- Per-project-type template variants (future)
- Syncing sections back to the GitHub repo (future)
- AI-assisted section content suggestions (future)
- Bulk migration of existing projects to a newer template version (future)
- UI for editing section definitions without a new migration (future)

---

## 11. Acceptance Criteria

### Data Model
- [ ] Four tables exist; `project_docs` is dropped. Version 1 is seeded with 22 sections across 7 doc keys.
- [ ] New project has `doc_template_version_id` set to the active version at creation time.
- [ ] Activating a template version atomically deactivates the previous one. Only one active version exists at any time.

### API
- [ ] `GET /projects/{id}/docs/{key}` returns `sections[]` with per-section content, not a single blob.
- [ ] `PUT /projects/{id}/docs/{key}` accepts partial section maps — only provided keys are updated. Unknown section keys return 400.
- [ ] Gate returns 409 when any required section in the project's pinned version is unfilled. `missingDocs` lists incomplete doc keys.
- [ ] Operator can create a new template version and activate it. Existing projects are unaffected.

### UI
- [ ] Doc editor shows one labeled textarea per section. Save calls the updated PUT endpoint.
- [ ] Docs tab card shows fill progress per doc key (e.g. "2 / 3 sections"). Doc shown as filled only when all required sections filled.
- [ ] Admin template management page lists versions with active indicator and section count.

### Tests
- [ ] Integration tests cover: partial save, gate pass with all required sections filled, gate block with one required section empty, version pinning (new project uses active version).
- [ ] Existing `ProjectDocsTests` and `WorkItemCreateDocsGateTests` updated to match new section-based API shape.

---

## 12. Edge Cases

| Scenario | Behaviour |
|----------|-----------|
| No active version at project creation | Project creation returns 409 with type `/probs/no-active-doc-template`. |
| Project created with version N; version N+1 activates later | Project continues to use version N. Editor and gate query version N only. |
| Partial PUT with an unknown section key | 400 response naming the unknown key(s). |
| Required section saved then cleared (empty / whitespace) | Content normalised to null; section re-enters the unfilled set. |
| Optional section left empty | Gate is unaffected. Section shown in editor with a hint, no validation error. |

---

## 13. Traceability

| Reference | Value |
|-----------|-------|
| Persona | Operator setting up a new project |
| Depends on | FEAT-014 — Project Documentation |
| Blocks | Any AI context-injection feature that reads project docs |
| Related | FEAT-013 — GitHub repo scaffold |
