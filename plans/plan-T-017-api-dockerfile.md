# Implementation Plan: T-017 — API Dockerfile (multi-stage) and .dockerignore

## Task Reference
- **Task ID:** T-017
- **Type:** DevOps
- **Workflow:** standard
- **Complexity:** S
- **Rationale:** Profile rule: multi-stage builds, `dotnet/aspnet` final stage, env-agnostic image, secrets never baked in.

## Overview
Two-stage Dockerfile at repo root: SDK image builds and publishes; ASP.NET runtime image runs. Image listens on `:8080`, reads all config from environment variables, runs as non-root.

## Implementation Steps

### Step 1: Dockerfile
**File:** `Dockerfile`
**Action:** Create
```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — copy only what the dotnet restore graph needs first, for layer caching
COPY DevHub.sln Directory.Build.props ./
COPY src/DevHub.Api/DevHub.Api.csproj                                src/DevHub.Api/
COPY src/DevHub.Contracts/DevHub.Contracts.csproj                    src/DevHub.Contracts/
COPY src/DevHub.Modules.Workspace/DevHub.Modules.Workspace.csproj    src/DevHub.Modules.Workspace/
COPY src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj      src/DevHub.Modules.Identity/
COPY src/DevHub.Modules.ExecutorRegistry/DevHub.Modules.ExecutorRegistry.csproj src/DevHub.Modules.ExecutorRegistry/
COPY src/DevHub.Modules.WorkItems/DevHub.Modules.WorkItems.csproj    src/DevHub.Modules.WorkItems/
COPY src/DevHub.Modules.Audit/DevHub.Modules.Audit.csproj            src/DevHub.Modules.Audit/
COPY src/DevHub.Modules.Notifications/DevHub.Modules.Notifications.csproj      src/DevHub.Modules.Notifications/
RUN dotnet restore src/DevHub.Api/DevHub.Api.csproj

# Build + publish
COPY src/ src/
RUN dotnet publish src/DevHub.Api/DevHub.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER 1000:1000
ENTRYPOINT ["dotnet", "DevHub.Api.dll"]
```

### Step 2: .dockerignore
**File:** `.dockerignore`
**Action:** Create
```
**/bin
**/obj
**/.vs
**/.idea
**/*.user
.git
.gitignore
.env
.env.*
client
tests
mockups
plans
docs
README.md
LICENSE
docker-compose*.yml
scripts
```
Important: do **not** exclude `DevHub.sln`, `Directory.Build.props`, or `src/`.

### Step 3: Local build smoke
**Action:** Verify
- `docker build -t devhub-api .` succeeds.
- `docker run --rm -p 8080:8080 -e ConnectionStrings__Postgres="..." -e Jwt__SigningKey="..." devhub-api` starts. (DB connectivity not required to start; health check will report `db: down` until reachable.)
- `curl http://localhost:8080/health` returns JSON.
- Inspect the image: `docker run --rm devhub-api ls /app` shows DLLs only, no `.cs` source files, no `appsettings.Development.json` referencing local hosts.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `Dockerfile` | Create | Multi-stage API build (sdk → aspnet) |
| `.dockerignore` | Create | Build-context exclusions |

## Edge Cases & Risks
- **`USER 1000:1000` write permissions** — the publish output is owned by root; ASP.NET Core does not write to `/app` at runtime, only reads. Logs go to stdout, not files. So 1000:1000 with read-only `/app` is fine.
- **Globalization invariants** — by default, .NET on Alpine requires `--invariant`. We use the Debian-based `aspnet:10.0` (not the Alpine variant) so globalization works out of the box. Document why.
- **`.dockerignore` excluding `client/`** — the API image must not contain the SPA; the SPA gets its own image in T-018.
- **Build-time vs run-time secrets** — no secret is baked into the image. `ConnectionStrings__Postgres`, `Jwt__SigningKey`, and `OperatorSeed__Password` must be passed at run time.

## Acceptance Verification
- [ ] `docker build -t devhub-api .` succeeds with no warnings about missing files.
- [ ] Resulting image runs and responds on `:8080`.
- [ ] Image does not contain `.cs`, `.csproj`, `.env`, or `Development` appsettings (verified with `docker run --rm devhub-api find /app -name "*.cs" -o -name "*.csproj" -o -name ".env*"` returning empty).
- [ ] Image runs as UID 1000 (`docker run --rm devhub-api id -u` → `1000`).
