# Implementation Plan: T-003 — Docker Compose (dev) and .env files

## Task Reference
- **Task ID:** T-003
- **Type:** DevOps
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** Profile rule "Local development first" — app must build and run before any feature code lands. Compose-for-infra-only keeps backend/frontend hot-reload on the host.

## Overview
Stand up local PostgreSQL via Docker Compose, document every env var the app expects, and update README with the three-terminal local-dev flow.

## Implementation Steps

### Step 1: docker-compose.yml
**File:** `docker-compose.yml`
**Action:** Create
```yaml
services:
  postgres:
    image: postgres:16-alpine
    container_name: devhub-postgres-dev
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-devhub}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB:-devhub}
    ports:
      - "5432:5432"
    volumes:
      - devhub-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 10
volumes:
  devhub-pgdata:
```

### Step 2: .env.example
**File:** `.env.example`
**Action:** Create
```
# PostgreSQL
POSTGRES_USER=devhub
POSTGRES_PASSWORD=change-me
POSTGRES_DB=devhub
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=devhub;Username=devhub;Password=change-me

# JWT
Jwt__Issuer=https://devhub.local
Jwt__Audience=devhub-spa
Jwt__SigningKey=replace-me-with-at-least-32-byte-random-string

# Seeded operator (created at first boot only)
OPERATOR_SEED_EMAIL=operator@devhub.local
OPERATOR_SEED_DISPLAY_NAME=Operator
OPERATOR_SEED_PASSWORD=change-me

# CORS
Cors__SpaOrigin=http://localhost:4200
```

### Step 3: .gitignore
**File:** `.gitignore`
**Action:** Create (or Modify if it already exists)
Add `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, `*.user`, `.vs/`, `.idea/`, `client/.angular/`, `client/coverage/`.

### Step 4: README local-dev section
**File:** `README.md`
**Action:** Modify
Add a "Local Development" section:
```
1. cp .env.example .env  (edit values)
2. docker compose up -d                                  # PostgreSQL
3. dotnet ef database update --project src/DevHub.Modules.Workspace  (repeat per module — see CLAUDE.md)
4. dotnet run --project src/DevHub.Api                # API on :5000
5. cd client && npm install && ng serve                  # SPA on :4200, proxies /api → :5000
6. open http://localhost:4200
```

### Step 5: Verify
**Action:** Verify
`docker compose up -d` → wait until `docker compose ps` shows `postgres` as `(healthy)`. `psql postgresql://devhub:change-me@localhost:5432/DevHub -c '\l'` succeeds.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `docker-compose.yml` | Create | Local infra (Postgres only) |
| `.env.example` | Create | Local-dev env template (no secrets) |
| `.gitignore` | Create/Modify | Exclude env files, build outputs, IDE artifacts |
| `README.md` | Modify | Document local-dev startup |

## Edge Cases & Risks
- **`.env` committed by mistake** — guarded by `.gitignore`; reinforce in PR review (pre-commit hook is a later IMP).
- **Port 5432 already in use** locally — document `POSTGRES_HOST_PORT` override later if it becomes a real problem.
- **Healthcheck variable expansion** — note the `$$` escape inside the compose file so `${POSTGRES_USER}` is evaluated at container runtime, not at compose-parse time.

## Acceptance Verification
- [ ] `docker compose up -d` brings Postgres up; `docker compose ps` shows `(healthy)` within 10s.
- [ ] `.env.example` is committed; `.env` is gitignored (`git check-ignore .env` succeeds).
- [ ] README documents the local-dev flow.
