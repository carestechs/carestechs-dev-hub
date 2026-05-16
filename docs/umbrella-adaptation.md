# Umbrella Adaptation Spec

> **Status:** spec (not yet implemented) · **Target:** make `carestechs-dev-hub`
> run under the DevTools umbrella (`../devtools-umbrella.md`) alongside
> `carestechs-flow-engine`, `carestechs-agent-orchestrator`, and
> `carestechs-agent-orchestrator-ui`, sharing one Postgres and one Docker
> network. The solo-dev `docker-compose.yml` flow is preserved.

---

## Conventions the umbrella enforces

Observed from `../infra/`, `../carestechs-agent-orchestrator/docker-compose.prod.yml`,
`../carestechs-flow-engine/docker-compose.prod.yml`, and
`../carestechs-agent-orchestrator-ui/docker-compose.prod.yml`.

| Concern | Convention |
|---|---|
| Shared network | `devtools-infra` (external, `driver: bridge`); joined as `infra:` in per-project compose |
| Shared Postgres | container `postgres`, reachable on the network as `postgres:5432`, host-published `127.0.0.1:5433:5432` |
| Shared Postgres creds | `POSTGRES_USER=devtools` / `POSTGRES_PASSWORD=devtools`; one cluster, one volume |
| Per-project DB | one logical database per project, owned by `devtools`, listed in `infra/init-databases.sql` |
| Service discovery | container-name DNS on the `infra` net (e.g. `flowengine-api:8080`) — never via host loopback |
| Host port bindings | API services normally **no host port** (peers reach by container name); UIs bind `127.0.0.1:<port>:80` (loopback only, intentional) |
| Dev vs prod compose | `docker-compose.yml` = self-contained (own postgres) for solo work; `docker-compose.prod.yml` = umbrella mode (no postgres, external network) |
| Lifecycle | `./start.sh` / `./stop.sh` at the DevTools root; project listed in `PROJECTS=( ... )` |

Currently published on `127.0.0.1`: `5433` (postgres), `8000` (orchestrator-api),
`4200` (ao-ui). flow-engine API is internal-only.

---

## Port allocation after this change

| Service | Container | Host bind | Container port | Reached internally as |
|---|---|---|---|---|
| Shared Postgres | `postgres` | `127.0.0.1:5433` | 5432 | `postgres:5432` |
| flow-engine API | `flowengine-api` | — | 8080 | `flowengine-api:8080` |
| orchestrator API | `orchestrator-api` | `127.0.0.1:8000` | 8000 | `orchestrator-api:8000` |
| ao-ui | `carestechs-ao-ui` | `127.0.0.1:4200` | 80 | `carestechs-ao-ui:80` |
| **devhub API** | `devhub-api` | **`127.0.0.1:8090`** *(recommended)* | 8080 | `devhub-api:8080` |
| **devhub web** | `devhub-web` | **`127.0.0.1:4300`** | 80 | `devhub-web:80` |

Rationale: `4300` keeps the SPA on the `4xxx` band next to `ao-ui:4200`;
`8090` keeps devhub-api reachable for ops curls the same way `orchestrator-api`
is on `8000`. devhub-api host publication is optional — see §2 note.

---

## Change list

### 1. `../infra/init-databases.sql` — add `devhub` DB

One-line patch in the **infra** repo:

```sql
CREATE DATABASE devhub;
```

For hosts where the infra volume already exists (init script only runs on
first volume init), run once manually:

```bash
docker exec -i postgres psql -U devtools -d postgres -c 'CREATE DATABASE devhub;'
```

### 2. `docker-compose.prod.yml` — full rewrite

Drop the `postgres` service and its volume. Keep `api` + `web`, join the
external `devtools-infra` network, stop publishing the API (or publish on
`127.0.0.1:8090` — see note below).

```yaml
# Prod under the DevTools umbrella. The shared `postgres` lives in
# ../infra/docker-compose.yml; this file assumes the external
# `devtools-infra` network already exists. Run umbrella-wide via
# ../start.sh, or use the sibling docker-compose.yml for solo dev.
services:
  api:
    image: devhub-api:latest
    container_name: devhub-api
    build:
      context: .
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Port=5432;Database=${POSTGRES_DB:-devhub};Username=${POSTGRES_USER:-devtools};Password=${POSTGRES_PASSWORD:-devtools}"
      Jwt__Issuer:     "${Jwt__Issuer}"
      Jwt__Audience:   "${Jwt__Audience}"
      Jwt__SigningKey: "${Jwt__SigningKey}"
      Cors__SpaOrigin: "${Cors__SpaOrigin}"
      OperatorSeed__Email:       "${OperatorSeed__Email}"
      OperatorSeed__DisplayName: "${OperatorSeed__DisplayName}"
      OperatorSeed__Password:    "${OperatorSeed__Password}"
      ASPNETCORE_URLS: "http://+:8080"
      ASPNETCORE_ENVIRONMENT: "Production"
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1:8080/health > /dev/null 2>&1 || exit 1"]
      interval: 10s
      timeout: 3s
      retries: 12
    networks: [infra]
    ports:
      # Optional — peers on the infra network reach this as `devhub-api:8080`.
      # Published on loopback only, matching orchestrator-api's posture, so
      # operators can curl it directly during ops. Remove if not wanted.
      - "127.0.0.1:${API_HOST_PORT:-8090}:8080"
    restart: unless-stopped

  web:
    image: devhub-web:latest
    container_name: devhub-web
    build:
      context: ./client
    depends_on:
      api:
        condition: service_healthy
    ports:
      - "127.0.0.1:${WEB_HOST_PORT:-4300}:80"
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1/health > /dev/null 2>&1 || exit 1"]
      interval: 10s
      timeout: 3s
      retries: 6
    networks: [infra]
    restart: unless-stopped

networks:
  infra:
    external: true
    name: devtools-infra
```

Notable removals vs current file: `postgres` service, `devhub-pgdata`
volume, `depends_on: postgres` on `api`. Container names (`devhub-api`,
`devhub-web`) are already collision-free.

### 3. `client/nginx.conf` — verify the API upstream

The SPA's nginx must `proxy_pass` to the API via container DNS
(`http://devhub-api:8080`), not `localhost`. If it currently uses an alias
like `api:8080` that also resolves on the compose-created network, but
prefer the explicit container name now that the network is shared.

**Action:** read `client/nginx.conf`, confirm/adjust the upstream.

### 4. `.env.production.example` — credentials simplified

Drop the project-owned Postgres password (use shared creds). Drop the
explicit `ConnectionStrings__Postgres` (the prod compose builds it from
`POSTGRES_*`).

```dotenv
# Shared infra Postgres — must match infra/.env
POSTGRES_USER=devtools
POSTGRES_PASSWORD=devtools
POSTGRES_DB=devhub

# Project-specific
Jwt__Issuer=https://devhub.local
Jwt__Audience=devhub-spa
Jwt__SigningKey=replace-me-with-at-least-32-byte-random-string

OperatorSeed__Email=operator@devhub.local
OperatorSeed__DisplayName=Operator
OperatorSeed__Password=replace-me-and-rotate-immediately

Cors__SpaOrigin=http://127.0.0.1:4300

# Host ports (loopback)
WEB_HOST_PORT=4300
API_HOST_PORT=8090
```

The standalone `.env.example` for the dev compose keeps
`ConnectionStrings__Postgres` and the project-local postgres password —
that flow is unchanged.

### 5. `scripts/verify-docker.sh` — do NOT run against the umbrella

Current script ends with `docker compose ... down -v`, which under the
umbrella would destroy the shared `devhub-pgdata` volume — except the
volume no longer exists in this file. The bigger risk is the script
expects port `8080` and the seeded credentials, both of which change.

Two options:

- **Option A (recommended):** keep the script standalone — it always runs
  against `docker-compose.yml` (solo dev compose), never the prod one.
  Document this in a header comment.
- **Option B:** parameterize the port and accept `UMBRELLA=1` to skip
  `down -v` and use the umbrella creds. More moving parts.

Either way, when the script runs against the prod compose under the
umbrella, change `WEB_PORT` default to `4300`.

### 6. `../start.sh` — add `carestechs-dev-hub` to PROJECTS

```bash
PROJECTS=(
    carestechs-agent-orchestrator
    carestechs-flow-engine
    carestechs-agent-orchestrator-ui
    carestechs-dev-hub
)
```

`../stop.sh` will pick it up automatically (it derives from the same list).

### 7. `docker-compose.yml` (solo dev) — shift host port to avoid collision

The dev compose currently exposes Postgres on `127.0.0.1:5432`, which
collides with `infra-postgres` if the umbrella is also up. Shift it:

```yaml
ports:
  - "127.0.0.1:5434:5432"
```

…and update `.env.example` to set
`ConnectionStrings__Postgres=Host=localhost;Port=5434;…`.

Alternative: document "stop umbrella before running dev compose". Port
shift wins — zero coordination required.

### 8. `README.md` and `CLAUDE.md` — discoverability

Add one short section near the existing "Local Development" block:

> **Umbrella mode:** run alongside other DevTools projects against shared
> infra by listing this repo in `../start.sh` and using
> `docker-compose.prod.yml`. SPA is at `http://127.0.0.1:4300`. See
> `docs/umbrella-adaptation.md` and `../devtools-umbrella.md`.

---

## Acceptance criteria

A clean machine with no Docker state should pass all of the following in
order:

1. `cd ../infra && docker compose up -d` → `postgres` container healthy,
   `devhub` database exists (`docker exec postgres psql -U devtools -lqt
   | cut -d\| -f1 | grep -qw devhub`).
2. `cd ../carestechs-dev-hub && cp .env.production.example .env.production`.
3. `docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build`
   → both `devhub-api` and `devhub-web` reach `healthy`, joined to the
   `devtools-infra` network alongside `postgres`.
4. `curl -sf http://127.0.0.1:4300/ | grep -q '<title>DevHub</title>'` →
   SPA index served.
5. `POST /api/auth/login` with the seeded operator returns a JWT; subsequent
   `GET /api/auth/me` returns the same email.
6. From the orchestrator container:
   `docker exec orchestrator-api curl -sf http://devhub-api:8080/health`
   succeeds — proves cross-project DNS on the shared network.
7. `cd .. && ./start.sh` brings the full umbrella up (orchestrator,
   flow-engine, ao-ui, devhub) in one command; `./stop.sh` tears it down
   without dropping the infra volume.

---

## Open decisions captured

- **Host-publish devhub-api?** Spec recommends yes (`127.0.0.1:8090:8080`)
  to match orchestrator-api's ops ergonomic. Strip the `ports:` block on
  `api:` if you want it internal-only.
- **Solo dev compose port shift.** Spec recommends `5434` — confirm or
  pick another free port before applying.
- **verify-docker.sh policy.** Spec recommends keeping it strictly
  standalone (Option A). Switch to Option B only if you want one
  smoke-test entry point that covers both modes.
