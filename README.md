# DevHub

Multi-project, multi-team workspace that sits above one or more headless lifecycle executors and serves as the single front door humans use to start, observe, approve, and complete work flowing through them.

See `docs/stakeholder-definition.md` for the full product definition and `docs/ARCHITECTURE.md` for the system design.

---

## Repo Layout

```
DevHub.slnx                          # .NET 10 solution (XML format)
src/
├── DevHub.Api/                      # Thin ASP.NET Core host (composition root only)
├── DevHub.Contracts/                # Cross-module interfaces, DTOs, persistence base
└── DevHub.Modules.{Workspace,Identity,ExecutorRegistry,WorkItems,Audit,Notifications}/
tests/
├── DevHub.TestHarness/              # Shared Postgres fixture + WebApplicationFactory
└── DevHub.Modules.<Module>.Tests/   # xUnit per-module (uses TestHarness)
client/                              # Angular 20 SPA (dev-hub workspace)
docker-compose.yml                   # Dev infra (Postgres only)
docker-compose.prod.yml              # Self-contained prod stack (postgres + api + web)
scripts/verify-docker.sh             # End-to-end prod-stack smoke test
docs/                                # Stakeholder def, architecture, data model, API spec, UI spec, work items
plans/                               # Per-task implementation plans
mockups/                             # Stakeholder-facing HTML mockups for new screens
```

Module projects reference `DevHub.Contracts` only — never each other. `DevHub.Api` is the only project that references every module. See `CLAUDE.md` for the full convention set.

---

## Local Development

Prerequisites: .NET 10 SDK, Docker (Engine + Compose v2), Node 20+, `dotnet ef` global tool.

```bash
# 1. Copy and edit your local env
cp .env.example .env  # edit at minimum POSTGRES_PASSWORD, ConnectionStrings__Postgres, Jwt__SigningKey

# 2. Bring up Postgres
docker compose up -d
docker compose ps      # wait for postgres to be (healthy)

# 3. Build the backend
dotnet build DevHub.slnx

# 4. Run migrations (added once each module ships its first entity — see plans/plan-T-005 onward)
# dotnet ef database update --project src/DevHub.Modules.Workspace --startup-project src/DevHub.Api

# 5. Start the API (hot reload via `dotnet watch`)
dotnet run --project src/DevHub.Api

# 6. Frontend (dev — proxies /api to the API on :5234)
cd client && npm install && npm start
```

### Running tests

**Backend** — xUnit + Testcontainers. Each test class spins up an isolated
Postgres database inside the shared `postgres:16-alpine` container managed
by `DevHub.TestHarness/PostgresFixture`. Docker must be available.

```bash
dotnet test DevHub.slnx
```

**Frontend** — Karma + Jasmine, headless Chrome. The dev `test` command
watches; the `test:ci` command runs once with coverage and the
`ChromeHeadlessCI` launcher (adds `--no-sandbox` etc. for unprivileged
CI containers).

```bash
cd client
npm test         # watch mode (interactive)
npm run test:ci  # single run + coverage (writes coverage/dev-hub/lcov.info)
```

### Smoke-testing the prod stack

```bash
cp .env.production.example .env.production  # edit at minimum Jwt__SigningKey and OperatorSeed__Password
./scripts/verify-umbrella.sh
```

Brings up `docker-compose.prod.yml` (api + web) against the umbrella's
shared `postgres` container, waits for healthchecks, then exercises `/`,
`/api/auth/login`, and `/api/auth/me`. **Leaves the stack running** —
the umbrella is shared infrastructure; teardown is your call.

The legacy `scripts/verify-docker.sh` assumes the pre-FEAT-007
standalone shape (with its own postgres container) and runs
`down -v` at the end; it is retained for historical regression but
should not be run against the current `docker-compose.prod.yml`.

### Umbrella mode

DevHub runs alongside the other DevTools projects (orchestrator,
flow-engine, ao-ui) under a shared `postgres` + `devtools-infra`
network — see `docs/umbrella-adaptation.md` for the deployment
contract and `../devtools-umbrella.md` for the cross-project
convention.

Bootstrap, from a clean host:

1. `cd ../infra && docker compose up -d` — shared `postgres` healthy.
2. On a host where the infra volume pre-dates DevHub being listed in
   `init-databases.sql`, run the one-shot:
   `docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'`.
3. From this repo:
   `cp .env.production.example .env.production` (edit `Jwt__SigningKey`,
   `OperatorSeed__Password`).
4. `docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build`.
5. SPA: <http://127.0.0.1:4300>. API ops curl: <http://127.0.0.1:8090/health>.

Or, from the DevTools umbrella root: `./start.sh` (requires this repo
to be listed in `PROJECTS=( ... )`).

Smoke test: `./scripts/verify-umbrella.sh`.

> **One-time hygiene** if you previously ran the pre-FEAT-007 standalone
> prod compose: `docker volume rm devhub-pgdata` removes the no-longer-
> referenced project-owned volume.

---

## Documentation

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Code conventions, anti-patterns, AI-routing table |
| `docs/stakeholder-definition.md` | Product philosophy, scope lock, success metrics |
| `docs/personas/primary-user.md` | The persona DevHub is built for |
| `docs/ARCHITECTURE.md` | Modular monolith structure, data flow, security |
| `docs/data-model.md` | Entities, relationships, conventions |
| `docs/api-spec.md` | REST endpoints, DTOs, auth model |
| `docs/ui-specification.md` | Screens, design system (modern-minimal) |
| `docs/work-items/FEAT-*.md` | Feature briefs (the v1 backlog) |
| `plans/plan-T-*.md` | Per-task implementation plans |
| `.ai-framework/` | Bundled AI framework reference (templates, prompts, guides) |
