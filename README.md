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
cp .env.production.example .env.production  # edit at minimum POSTGRES_PASSWORD and Jwt__SigningKey
WEB_HOST_PORT=8081 ./scripts/verify-docker.sh
```

Builds the API + web images, brings up `docker-compose.prod.yml`
(postgres + api + web), waits for healthchecks, then exercises `/`,
`/api/auth/login`, and `/api/auth/me`. Tears the stack down on exit.

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
