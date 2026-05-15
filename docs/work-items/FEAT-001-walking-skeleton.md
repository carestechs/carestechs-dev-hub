# Feature Brief: FEAT-001 — Walking Skeleton (build, run, deploy, login)

> **Purpose:** Stand up the modular monolith + Angular SPA + Docker Compose pipeline end-to-end so every later feature has a working spine to land on. Per the architecture profile: "the application must build, run, and be locally testable before adding any feature code."

---

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-001 |
| **Name** | Walking Skeleton (build, run, deploy, login) |
| **Target Version** | Continuous (v1 foundation) |
| **Status** | Not Started |
| **Priority** | Critical |
| **Requested By** | Architecture (foundational) |
| **Date Created** | 2026-05-15 |

---

## 2. User Story

**As a** developer on this project, **I want to** check out the repo, run `docker compose up -d && dotnet run && ng serve`, sign in to the SPA, and see an empty home screen, **so that** every subsequent feature has a working build/run/deploy spine and authentication seam to extend.

---

## 3. Goal

A single-command-startable backend + frontend with health checks, JWT login, and a routable empty home screen on the modern-minimal app shell — all per the architecture and UI profiles.

---

## 4. Feature Scope

### 4.1 Included

- Solution structure: `DevHub.Api`, `DevHub.Contracts`, all six `DevHub.Modules.*` projects empty but wired.
- `Program.cs` composition root with DI, JWT bearer middleware, global RFC 7807 exception handler, and `Add<Module>Module()` registration for each module.
- PostgreSQL via Docker Compose; one `DbContext` per module with a no-op initial migration each.
- Identity module: `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me`. Seed one operator member for first-run.
- `GET /health` returning DB status.
- Angular workspace: standalone components, Tailwind 4+ configured with modern-minimal tokens, Poppins + Inter wired via Google Fonts.
- App shell + public layout components; Login screen; empty Home screen.
- HTTP interceptor: attach access token; refresh on 401; render RFC 7807 errors normalized.
- Docker multi-stage builds for backend and frontend; `docker-compose.yml` (dev infra) and `docker-compose.prod.yml`.
- `scripts/verify-docker.sh` smoke test.
- Test scaffolding: xUnit + Testcontainers project per module; one passing test per module.

### 4.2 Excluded

- Any business endpoints beyond auth + health. (Workspace CRUD, executor registry, work items: later FEATs.)
- Federated identity (open question in stakeholder def).
- Production deployment automation beyond `docker-compose.prod.yml`.

---

## 5. Acceptance Criteria

- **AC-1:** `docker compose up -d && dotnet run --project src/DevHub.Api` starts the API; `GET /health` returns 200 with `{ status: "ok", checks: { db: "up" } }`.
- **AC-2:** `cd client && ng serve` starts the SPA; navigating to `/` redirects to `/login`.
- **AC-3:** Logging in with the seeded operator credentials returns an access token, sets the refresh cookie, and lands on the empty Home screen.
- **AC-4:** `GET /api/auth/me` returns the member with an empty memberships list.
- **AC-5:** `dotnet test` runs and passes — at least one test per module project.
- **AC-6:** `scripts/verify-docker.sh` runs the full prod compose, hits `/health`, and exits 0.
- **AC-7:** All 6 module DbContexts have at least one applied migration each; per-module `dotnet ef database update` works.

---

## 6. Key Entities and Business Rules

| Entity | Role | Key Business Rules |
|--------|------|--------------------|
| Member | Seeded operator member for first login | One seed member; `system:operator` role assigned |
| Role | Seeded `operator` system role | `is_system = true` |
| IdentityCredential | Local credential for the seed member | Argon2id password hash |
| RefreshToken | Issued on login | Rotated on refresh; revocable |

**New entities required:** None beyond what `docs/data-model.md` already defines.

---

## 7. API Impact

| Endpoint | Method | Status | Notes |
|----------|--------|--------|-------|
| `/health` | GET | New | DB check |
| `/api/auth/login` | POST | New | Issues access token + refresh cookie |
| `/api/auth/refresh` | POST | New | Rotates refresh, returns access token |
| `/api/auth/logout` | POST | New | Revokes refresh, clears cookie |
| `/api/auth/me` | GET | New | Returns current member + memberships (empty in skeleton) |

**New endpoints required:** None beyond above (all already specified in `docs/api-spec.md`).

---

## 8. UI Impact

| Screen / Component | Status | Description |
|--------------------|--------|-------------|
| Login | New | Public layout, email/password form |
| Home | New (empty) | App shell, "Nothing waiting on you yet" empty state |
| App shell (header, sidebar) | New | Modern-minimal styling |
| Public layout | New | Centered card |
| `AppCard`, `AppButton`, `AppFormField`, `AppErrorBanner`, `AppSpinner`, `EmptyState` | New | Foundational components |

**New screens required:** Login and Home are defined in `docs/ui-specification.md`.

---

## 9. Edge Cases

- DB unreachable on boot — API returns 503 on `/health` and 502 on auth endpoints; SPA renders error banner.
- Refresh cookie missing or invalid — `/api/auth/refresh` returns 401; SPA redirects to `/login`.
- Seeded operator already exists — seed is idempotent (`INSERT ... ON CONFLICT DO NOTHING`).
- First-run migrations on an empty DB — every module migration runs cleanly in arbitrary order (modules do not reference each other's tables).

---

## 10. Constraints

- Must follow the `dotnet-angular-modular-monolith-docker-compose` profile exactly — no deviation from required ADRs.
- Must follow the `modern-minimal` design profile — no deviation from compiled DDRs.
- No secrets in images; everything via env vars.
- Local dev must work without Docker for backend + frontend (only PostgreSQL in compose).

---

## 11. Motivation and Priority Justification

**Motivation:** Every other feature listed below assumes the modular monolith host runs, auth works, the SPA shell renders, and Docker Compose deploys. Without the skeleton, every later task carries hidden plumbing work.

**Impact if delayed:** Every other v1 feature stalls; we cannot validate the architecture profile end-to-end.

**Dependencies on this feature:** FEAT-002, FEAT-003, FEAT-004, FEAT-005, FEAT-006 (all v1 features below).

---

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | `docs/personas/primary-user.md` (foundational — every member sees Home + Login) |
| **Stakeholder Scope Item** | "Identity and end-to-end authorization"; "Single front door, single identity" |
| **Success Metric** | "Front-door discipline" (zero end-user actions bypass DevHub in production) |
| **Related Work Items** | Blocks: FEAT-002, FEAT-003, FEAT-004, FEAT-005, FEAT-006 |
