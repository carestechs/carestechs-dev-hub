# FEAT-001 Task Breakdown — Walking Skeleton

> Generated from `docs/work-items/FEAT-001-walking-skeleton.md` using `.ai-framework/prompts/feature-tasks.md`. Implementation order follows the dependency graph at the bottom of this file.

---

## Foundation

### T-001: Create .NET solution and project structure

**Type:** DevOps · **Workflow:** standard · **Complexity:** M · **Dependencies:** None

**Description:**
Create `DevHub.sln`, the thin host `DevHub.Api`, the shared `DevHub.Contracts`, and the six feature module projects (`DevHub.Modules.Workspace`, `.Identity`, `.ExecutorRegistry`, `.WorkItems`, `.Audit`, `.Notifications`), each with the per-module directory layout from the architecture profile. Wire project references and create the per-module test projects under `tests/`.

**Rationale:**
Foundational — every later backend task lands in one of these projects. The modular monolith structure is required by the architecture profile.

**Acceptance Criteria:**
- [ ] `DevHub.sln` exists and `dotnet build` succeeds with zero errors and zero warnings.
- [ ] `DevHub.Api` references all six module projects and `DevHub.Contracts`.
- [ ] Every module project references `DevHub.Contracts` only (never another module directly).
- [ ] `tests/DevHub.Modules.<Module>.Tests/` exists for each module; all test projects build.

**Files to Modify/Create:**
- Create: `DevHub.sln`
- Create: `src/DevHub.Api/DevHub.Api.csproj`
- Create: `src/DevHub.Contracts/DevHub.Contracts.csproj`
- Create: `src/DevHub.Modules.Workspace/DevHub.Modules.Workspace.csproj`
- Create: `src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj`
- Create: `src/DevHub.Modules.ExecutorRegistry/DevHub.Modules.ExecutorRegistry.csproj`
- Create: `src/DevHub.Modules.WorkItems/DevHub.Modules.WorkItems.csproj`
- Create: `src/DevHub.Modules.Audit/DevHub.Modules.Audit.csproj`
- Create: `src/DevHub.Modules.Notifications/DevHub.Modules.Notifications.csproj`
- Create: `tests/DevHub.Modules.<Module>.Tests/*.csproj` (×6)
- Create: `.editorconfig`, `Directory.Build.props` (nullable enable, ImplicitUsings, TreatWarningsAsErrors)

**Technical Notes:**
TargetFramework `net10.0`. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. No module .csproj has a `ProjectReference` to another module — only to `DevHub.Contracts`. Per the profile, `DevHub.Api` is the *only* project that references every module.

---

### T-002: EF Core base — naming convention, timestamptz, UUID PKs, BaseEntity

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-001

**Description:**
Add `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `EFCore.NamingConventions` to every module. Create an internal-shared `ModuleDbContext` base class (in each module) and a `BaseEntity` abstract class with `Id (Guid)`, `CreatedAt (DateTimeOffset)`, `UpdatedAt (DateTimeOffset)`, and (where applicable) `DeletedAt (DateTimeOffset?)`. Configure the snake_case naming convention and `timestamptz` conversions on every DbContext.

**Rationale:**
Profile mandates snake_case, `timestamptz`, UUID PKs end-to-end. Centralizing the base avoids drift across modules.

**Acceptance Criteria:**
- [ ] A trivial migration on any module produces `snake_case` table/column names and `timestamptz` columns.
- [ ] `Id` columns are `uuid` in PostgreSQL and `Guid` in C#.
- [ ] `BaseEntity` defines `Id`, `CreatedAt`, `UpdatedAt`; a sibling `ISoftDeletable` adds `DeletedAt`.
- [ ] `SaveChangesAsync` automatically populates `CreatedAt`/`UpdatedAt` (override on each DbContext or via a single `SaveChangesInterceptor` published from `DevHub.Contracts`).

**Files to Modify/Create:**
- Modify: each module `.csproj` to add EF Core + Npgsql + EFCore.NamingConventions
- Create: `src/DevHub.Contracts/Persistence/BaseEntity.cs`
- Create: `src/DevHub.Contracts/Persistence/ISoftDeletable.cs`
- Create: `src/DevHub.Contracts/Persistence/TimestampingInterceptor.cs`
- Create: `src/DevHub.Modules.<Module>/<Module>DbContext.cs` (one per module — empty `DbSet`s, but `OnConfiguring`/`OnModelCreating` apply the naming convention)

**Technical Notes:**
Use `optionsBuilder.UseSnakeCaseNamingConvention()` in each DbContext. Inject `TimestampingInterceptor` via `DbContextOptionsBuilder.AddInterceptors(...)` in each `Add<Module>Module()` extension (T-004). PKs default to client-generated `Guid.NewGuid()` — never `Identity` columns.

---

### T-003: Docker Compose (dev) + .env files

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-001

**Description:**
Author `docker-compose.yml` to run PostgreSQL only (port 5432, named volume, healthcheck via `pg_isready`). Create `.env.example` with all env vars needed for local dev (`POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `JWT_AUDIENCE`, `JWT_ISSUER`, `OPERATOR_SEED_EMAIL`, `OPERATOR_SEED_PASSWORD`). Document local-dev startup in `README.md`.

**Rationale:**
Profile rule "Local development first": the app must build and run before any feature code lands. Compose-for-infra-only keeps backend/frontend hot-reload on the host.

**Acceptance Criteria:**
- [ ] `docker compose up -d` brings PostgreSQL up and `pg_isready` returns 0 within 10s.
- [ ] `.env.example` is present and committed; `.env` is gitignored.
- [ ] README.md documents the three-terminal local-dev flow (compose up, dotnet run, ng serve).

**Files to Modify/Create:**
- Create: `docker-compose.yml`
- Create: `.env.example`
- Create: `.gitignore` (add `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`)
- Modify: `README.md` (Local development section)

**Technical Notes:**
Use `postgres:16-alpine`. Name the volume `devhub-pgdata`. Healthcheck: `pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB`. Default `POSTGRES_USER=devhub`, `POSTGRES_DB=devhub`.

---

### T-004: Program.cs composition root (DI, JWT, RFC 7807, AddModule extensions)

**Type:** Backend · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-002, T-003

**Description:**
Wire `DevHub.Api/Program.cs` as the composition root only: configuration binding, JWT bearer authentication, global RFC 7807 exception handling (`UseExceptionHandler` with a problem-details writer), CORS for the SPA origin, request logging, health-check endpoint registration, and the per-module `Add<Module>Module()` / `Use<Module>Module()` extensions. Each module gets a public `<Module>ModuleExtensions` class with both extensions.

**Rationale:**
Profile rule "thin API host": no controllers, services, or business logic in `DevHub.Api`. Centralized exception handler produces uniform problem-details across modules.

**Acceptance Criteria:**
- [ ] `DevHub.Api/Program.cs` contains only DI registration and pipeline composition; no controllers, no services.
- [ ] An unhandled `DomainException`-typed exception thrown from any module is translated to RFC 7807 (`application/problem+json`) with the right status code.
- [ ] `JwtBearerOptions` validates issuer, audience, signing key, and lifetime from configuration.
- [ ] CORS allows the SPA origin (configurable via env var) only.

**Files to Modify/Create:**
- Modify: `src/DevHub.Api/Program.cs`
- Create: `src/DevHub.Api/appsettings.json`, `appsettings.Development.json`
- Create: `src/DevHub.Contracts/ApplicationErrors/DomainException.cs`, `NotFoundException.cs`, `ForbiddenException.cs`, `ValidationException.cs`, `ConflictException.cs`, `ExecutorFailureException.cs`
- Create: `src/DevHub.Api/Middleware/ProblemDetailsHandler.cs`
- Create: `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×6) — initial empty `AddXModule(this IServiceCollection, IConfiguration)` and `UseXModule(this IApplicationBuilder)` extensions

**Technical Notes:**
Use the built-in `Microsoft.AspNetCore.Diagnostics.ProblemDetailsService` if it suffices, otherwise wire a custom `IExceptionHandler`. JWT options bound from `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`. Pipeline order: routing → CORS → authentication → authorization → exception handler → endpoints. Strongly-typed options validated on startup via `Services.AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()`.

---

## Backend — Identity & seeded operator

### T-005: Workspace module — Member + Role entities + initial migration + seed

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-004

**Description:**
Create the `Member` and `Role` entities + EF mappings inside `DevHub.Modules.Workspace`. Add an initial migration that creates `workspace.members`, `workspace.roles`, and minimal placeholder tables for `teams`, `projects`, `project_memberships`, `role_assignments` (full surface lands in FEAT-002 — for now just the columns required to compile the DbContext). Add a startup data seeder that idempotently inserts the system `operator` role and the seed operator member.

**Rationale:**
The seed operator is required for AC-3 (first login). `Member` is referenced by Identity's `IdentityCredential.member_id` in T-006.

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.Workspace` applies cleanly on an empty database.
- [ ] After startup, `workspace.roles` contains a row with `key = 'operator'`, `is_system = true`.
- [ ] `workspace.members` contains a row matching `OPERATOR_SEED_EMAIL`.
- [ ] Re-running the seeder is idempotent (no duplicate rows, no exception).

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Workspace/Entities/Member.cs`, `Role.cs`, `Team.cs`, `Project.cs`, `ProjectMembership.cs`, `RoleAssignment.cs` (latter four as minimal scaffolds)
- Create: `src/DevHub.Modules.Workspace/Entities/Enums/MemberStatus.cs`
- Modify: `src/DevHub.Modules.Workspace/WorkspaceDbContext.cs`
- Create: `src/DevHub.Modules.Workspace/Migrations/*` (initial)
- Create: `src/DevHub.Modules.Workspace/Seeding/WorkspaceSeeder.cs` and register it inside `WorkspaceModuleExtensions.AddWorkspaceModule()`
- Modify: `src/DevHub.Modules.Workspace/WorkspaceModuleExtensions.cs`

**Technical Notes:**
Tables live under the `workspace` schema (`modelBuilder.HasDefaultSchema("workspace")`). Apply soft-delete on Project/Team/Member/Membership via global query filter (`HasQueryFilter(e => e.DeletedAt == null)`). The seeder runs on `IHostedService.StartAsync` so it executes after migrations.

---

### T-006: Identity module — IdentityCredential + RefreshToken entities + Argon2id hashing

**Type:** Backend · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-005

**Description:**
Create the `IdentityCredential` and `RefreshToken` entities + EF mappings under the `identity` schema. Add the initial migration. Implement `IPasswordHasher` with Argon2id (using `Konscious.Security.Cryptography.Argon2`) and wire it into DI. Extend the seeder (or add an Identity seeder) to ensure the operator member has a `Local` credential with the password from `OPERATOR_SEED_PASSWORD`.

**Rationale:**
Stores authentication material. Argon2id matches modern best practice and avoids shipping ASP.NET Core Identity (over-scoped for this seam).

**Acceptance Criteria:**
- [ ] `dotnet ef database update --project src/DevHub.Modules.Identity` applies cleanly.
- [ ] `IPasswordHasher.Hash("password")` produces an Argon2id-encoded string; `Verify` round-trips.
- [ ] The seeded operator member has exactly one `Local` credential at startup; re-seeding is idempotent.
- [ ] `RefreshToken` is unique by `token_hash`; the hash column stores SHA-256 of the token, never the literal.

**Files to Modify/Create:**
- Modify: `src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj` (add `Konscious.Security.Cryptography.Argon2`)
- Create: `src/DevHub.Modules.Identity/Entities/IdentityCredential.cs`, `RefreshToken.cs`, `Enums/CredentialProvider.cs`
- Modify: `src/DevHub.Modules.Identity/IdentityDbContext.cs`
- Create: `src/DevHub.Modules.Identity/Migrations/*` (initial)
- Create: `src/DevHub.Modules.Identity/Services/IPasswordHasher.cs`, `Argon2PasswordHasher.cs`
- Create: `src/DevHub.Modules.Identity/Seeding/IdentitySeeder.cs`
- Modify: `src/DevHub.Modules.Identity/IdentityModuleExtensions.cs`

**Technical Notes:**
Argon2id parameters: 64 MB memory, 4 iterations, 2 lanes (tune by benchmarking on target hardware later). The seeder must run *after* the Workspace seeder so the member exists.

---

### T-007: Identity module — Auth services and AuthController endpoints

**Type:** Backend · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-006

**Description:**
Implement `IAuthenticationService` with four operations: `LoginAsync`, `RefreshAsync`, `LogoutAsync`, `GetCurrentMemberAsync`. Implement `IJwtTokenIssuer` (signs access tokens with `Jwt:SigningKey`, sets `iss`, `aud`, `sub` = member id, `exp` = 15 min) and `IRefreshTokenStore` (rotates refresh tokens, stores SHA-256, supports revocation). Add `AuthController` exposing the four endpoints from `api-spec.md` Identity. Refresh tokens are returned as `HttpOnly; Secure; SameSite=Lax` cookies scoped to `/api/auth`.

**Rationale:**
Implements every Identity endpoint in `api-spec.md`. Satisfies AC-3 (login → access token + refresh cookie → empty home).

**Acceptance Criteria:**
- [ ] `POST /api/auth/login` with valid credentials returns 200 with `accessToken`, `expiresAt`, `member`, and sets the refresh cookie.
- [ ] `POST /api/auth/login` with bad credentials returns 401 problem-details; with a suspended member, 403.
- [ ] `POST /api/auth/refresh` consumes the cookie, rotates, and returns a new access token; missing/invalid cookie → 401.
- [ ] `POST /api/auth/logout` (authenticated) revokes the current refresh chain and clears the cookie; returns 204.
- [ ] `GET /api/auth/me` (authenticated) returns `{ member, memberships: [] }` for the seed operator (no project memberships until FEAT-002).
- [ ] Every endpoint runs against an integration test (Testcontainers Postgres).

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.Identity/Services/IAuthenticationService.cs`, `AuthenticationService.cs`
- Create: `src/DevHub.Modules.Identity/Services/IJwtTokenIssuer.cs`, `JwtTokenIssuer.cs`
- Create: `src/DevHub.Modules.Identity/Services/IRefreshTokenStore.cs`, `RefreshTokenStore.cs`
- Create: `src/DevHub.Modules.Identity/Services/ICurrentMemberAccessor.cs`, `CurrentMemberAccessor.cs` (resolves from `HttpContext.User` claims; published via `DevHub.Contracts`)
- Create: `src/DevHub.Modules.Identity/Controllers/AuthController.cs`
- Create: `src/DevHub.Modules.Identity/DTOs/LoginRequest.cs`, `LoginResponse.cs`, `RefreshResponse.cs`, `MeResponse.cs`, `MemberDto.cs`, `MembershipDto.cs`
- Modify: `src/DevHub.Modules.Identity/IdentityModuleExtensions.cs` (register services)
- Modify: `src/DevHub.Contracts/Identity/ICurrentMember.cs` (interface)

**Technical Notes:**
Controller is thin: `await _auth.LoginAsync(req); return Ok(new EnvelopeDto<LoginResponse>(...))`. `IJwtTokenIssuer` uses `System.IdentityModel.Tokens.Jwt`. Refresh cookie attributes: `HttpOnly; Secure; SameSite=Lax; Path=/api/auth`. The "memberships" list on `/me` reads from `IProjectMembershipQuery` (published in `DevHub.Contracts`); for now, returns empty (Workspace stub provides an empty implementation until FEAT-002).

---

### T-008: Health controller with DB check

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-004

**Description:**
Add `GET /health` returning `200` with `{ status: "ok", checks: { db: "up" } }` when the database is reachable, or `503` with `{ status: "degraded", checks: { db: "down" } }` otherwise. Implemented via ASP.NET Core's `HealthCheckService` with a Postgres check (any module's DbContext works — use `WorkspaceDbContext` since it is guaranteed present from T-005).

**Rationale:**
AC-1 of FEAT-001. Also serves as Docker Compose's `depends_on: service_healthy` target.

**Acceptance Criteria:**
- [ ] `GET /health` returns 200 with the documented JSON when Postgres is reachable.
- [ ] Returns 503 with the documented JSON when Postgres is down (verified by stopping the container).
- [ ] Response writer produces the exact shape `{ status, checks: { db } }`, not the default ASP.NET Core health-check format.

**Files to Modify/Create:**
- Modify: `src/DevHub.Api/Program.cs` (register `AddHealthChecks().AddDbContextCheck<WorkspaceDbContext>("db")`)
- Create: `src/DevHub.Api/HealthCheckResponseWriter.cs`

**Technical Notes:**
Use `MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.WriteAsync })`. Health endpoint must be `[AllowAnonymous]`.

---

### T-009: Stub initial migrations for the remaining four modules

**Type:** Backend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-002, T-004

**Description:**
For `ExecutorRegistry`, `WorkItems`, `Audit`, and `Notifications`: create a no-op initial migration (empty Up/Down) so that `dotnet ef database update` works on each module on first boot. This proves the per-module migration pipeline; real entities land in FEAT-003/004/005/006.

**Rationale:**
AC-7 of FEAT-001 ("per-module `dotnet ef database update` works"). Catches DI/wiring drift before real entities are added.

**Acceptance Criteria:**
- [ ] `dotnet ef migrations list --project src/DevHub.Modules.<Name>` lists exactly one migration per module.
- [ ] `dotnet ef database update --project src/DevHub.Modules.<Name>` applies cleanly against an empty database.
- [ ] Each module has its own schema (`executor_registry`, `work_items`, `audit`, `notifications`) created at migration time.

**Files to Modify/Create:**
- Create: `src/DevHub.Modules.ExecutorRegistry/Migrations/*`
- Create: `src/DevHub.Modules.WorkItems/Migrations/*`
- Create: `src/DevHub.Modules.Audit/Migrations/*`
- Create: `src/DevHub.Modules.Notifications/Migrations/*`
- Modify: each module's `DbContext.OnModelCreating` to call `modelBuilder.HasDefaultSchema("...")`

**Technical Notes:**
Generate empty migrations by setting `HasDefaultSchema` and running `dotnet ef migrations add Initial`. EF will produce a migration that creates the schema only.

---

## Frontend

### T-010: Angular workspace with Tailwind 4 and modern-minimal tokens

**Type:** Frontend · **Workflow:** standard · **Complexity:** M · **Dependencies:** None

**Description:**
Create the Angular workspace under `client/` using `ng new DevHub --standalone --routing --style=css --skip-tests=false` (Angular 20+). Add Tailwind CSS 4+, configure `tailwind.config.js` with the modern-minimal token palette (sky-500 primary, slate neutrals, semantic colors) and font extensions (Poppins headings, Inter body). Link Google Fonts in `index.html`. Add a `proxy.conf.json` proxying `/api` → `http://localhost:5000` for dev.

**Rationale:**
Foundational frontend; every later UI task lands here. Locks in the design system before any screen work begins.

**Acceptance Criteria:**
- [ ] `cd client && npm install && ng serve` starts on `http://localhost:4200` with no template errors.
- [ ] `ng build` produces a production bundle.
- [ ] Tailwind utility classes (`bg-sky-500`, `font-heading`, `font-body`) render correctly in a smoke component.
- [ ] `proxy.conf.json` forwards `/api/*` to the API.

**Files to Modify/Create:**
- Create: `client/` (entire Angular workspace)
- Create: `client/tailwind.config.js`, `client/postcss.config.js`
- Modify: `client/src/styles.css` (Tailwind `@import "tailwindcss";` and any `@theme` overrides)
- Modify: `client/src/index.html` (Google Fonts `<link>` for Poppins + Inter)
- Create: `client/proxy.conf.json`

**Technical Notes:**
Tailwind v4 uses CSS-first config via `@theme`; mirror the token table from `docs/ui-specification.md` § Design System. Add `font-heading` (Poppins) and `font-body` (Inter) families via `theme.extend.fontFamily`. Wire `angular.json` `serve` configuration to use `proxy.conf.json`.

---

### T-011: Foundational standalone components (AppCard, AppButton, AppFormField, AppErrorBanner, AppSpinner, EmptyState)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** L · **Dependencies:** T-010

**Description:**
Build the shared building blocks listed in `docs/ui-specification.md` § Shared Components. Each is a standalone component with an `.html` template, no component CSS files (Tailwind utilities only), public input/output API per the spec, and Jasmine specs that cover the variants and the disabled/loading states.

**Rationale:**
Every screen reuses these. Building them up-front avoids inconsistent re-implementations in later screen tasks.

**Acceptance Criteria:**
- [ ] All six components exist as standalone with separate `.html` templates and no `.scss/.css` files.
- [ ] `AppButton` renders all four variants (`primary`, `secondary`, `ghost`, `danger`) and the `loading` state with an inline spinner.
- [ ] `AppCard` clickable variant has `hover:shadow-md hover:-translate-y-0.5` and a visible focus ring.
- [ ] `AppFormField` exposes `label`, `helperText`, `error`, `required`, sets `aria-invalid` on error.
- [ ] `AppErrorBanner` accepts a normalized `AppError` and renders `title`, `detail`, and `correlationId`.
- [ ] Each component has at least one spec covering its key states.

**Files to Modify/Create:**
- Create: `client/src/app/shared/components/app-card/app-card.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/components/app-button/app-button.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/components/app-form-field/app-form-field.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/components/app-error-banner/app-error-banner.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/components/app-spinner/app-spinner.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/components/empty-state/empty-state.component.{ts,html,spec.ts}`
- Create: `client/src/app/shared/index.ts` (barrel export)

**Technical Notes:**
This task is `mockup-first` — see `.ai-framework/prompts/mockup-generation.md` and produce `mockups/foundational-components.html` covering all six components in their key states before implementation. All components use Angular Signals for state; `AppButton` accepts `loading` as input signal.

---

### T-012: Layouts — PublicLayoutComponent and AppShellComponent

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-011

**Description:**
Build the two layouts from `docs/ui-specification.md` § Shared Layouts. `PublicLayoutComponent` is a centered card on `bg-slate-50`. `AppShellComponent` is the persistent left sidebar (collapsible below `md:`) + top header with logo, search slot, pending-action badge, and member menu. Both expose a `<router-outlet>`.

**Rationale:**
Every screen renders inside one of these. Locking the chrome before screen work avoids rework.

**Acceptance Criteria:**
- [ ] `PublicLayoutComponent` renders a centered `max-w-md` card on `bg-slate-50` with logo above the outlet.
- [ ] `AppShellComponent` renders the header (`h-14`, white, bottom border) and sidebar (`w-64`, white, right border) from `md:` and up; below `md:`, sidebar collapses behind a hamburger.
- [ ] Sidebar exposes nav items: Home, Projects, Pending on you (live group, placeholder for FEAT-005), Operator, Admin.
- [ ] Header pending-action badge accepts a count input (placeholder for FEAT-005).
- [ ] Mockups produced and approved before implementation.

**Files to Modify/Create:**
- Create: `client/src/app/core/layouts/public-layout/public-layout.component.{ts,html,spec.ts}`
- Create: `client/src/app/core/layouts/app-shell/app-shell.component.{ts,html,spec.ts}`
- Create: `client/src/app/core/layouts/app-shell/sidebar.component.{ts,html}` and `header.component.{ts,html}`
- Create: `mockups/public-layout.html`, `mockups/app-shell.html`

**Technical Notes:**
Mobile-first: base styles assume mobile; `md:` opens the sidebar. Pending-action badge is `bg-sky-500 text-white rounded-full text-xs h-5 min-w-5 px-1` when count > 0; hidden otherwise.

---

### T-013: Auth core service — token storage, HTTP interceptor, problem-details normalizer

**Type:** Frontend · **Workflow:** standard · **Complexity:** L · **Dependencies:** T-010

**Description:**
Implement `AuthService` (Angular signal-based; holds the in-memory access token and the resolved `Member`), `authInterceptor` (attaches `Authorization: Bearer <token>` to `/api/*` requests; on 401 attempts one silent refresh via `POST /api/auth/refresh` and replays the original request; on second 401, clears state and redirects to `/login`), and `problemDetailsInterceptor` (normalizes `application/problem+json` responses into a typed `AppError` and rethrows). Wire both interceptors in `app.config.ts`.

**Rationale:**
AC-3 requires login → token attach → empty home. Centralized interceptors avoid per-component error handling.

**Acceptance Criteria:**
- [ ] After successful `login()`, every subsequent `/api/*` request carries `Authorization: Bearer ...`.
- [ ] A 401 response on a non-auth endpoint triggers a single refresh attempt and one replay; a second 401 clears state and routes to `/login`.
- [ ] `problemDetailsInterceptor` parses `type`, `title`, `status`, `detail`, `correlationId`, `errors` and surfaces them as `AppError`; non-problem-detail errors fall back to `{ title: "Network error", detail: error.message }`.
- [ ] Auth state survives a full-page reload via a `POST /api/auth/refresh` on app bootstrap.
- [ ] Specs cover login, refresh-on-401, and second-401 logout flow.

**Files to Modify/Create:**
- Create: `client/src/app/core/auth/auth.service.{ts,spec.ts}`
- Create: `client/src/app/core/auth/auth.interceptor.{ts,spec.ts}`
- Create: `client/src/app/core/errors/problem-details.interceptor.{ts,spec.ts}`
- Create: `client/src/app/core/errors/app-error.ts`
- Create: `client/src/app/core/auth/auth.types.ts`
- Modify: `client/src/app/app.config.ts` (register `provideHttpClient(withInterceptors([...]))`)
- Create: `client/src/app/core/auth/app-bootstrap.ts` (runs refresh-on-load before routing)

**Technical Notes:**
Use `inject(HttpClient)`. Access token lives in a signal inside `AuthService` and is never persisted to `localStorage` (per Stakeholder rule — single front door, no per-executor credentials, and to limit XSS blast radius). Refresh cookie survives reload — that's how state restores.

---

### T-014: Route guards and route configuration

**Type:** Frontend · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-013

**Description:**
Implement `authGuard` (redirects to `/login` if `AuthService.isAuthenticated()` is false) and `anonGuard` (redirects to `/` if the user is already authenticated). Configure `app.routes.ts`: `/login` (Public layout, `anonGuard`), `/` (App shell, `authGuard`, redirects to home), and a placeholder `/me`.

**Rationale:**
Defense in depth in the UI; the server is still the authoritative gate. Required for AC-2/AC-3 navigation behavior.

**Acceptance Criteria:**
- [ ] Unauthenticated navigation to `/` redirects to `/login`.
- [ ] Authenticated navigation to `/login` redirects to `/`.
- [ ] Routes lazy-load home and login components.
- [ ] Specs cover both guards.

**Files to Modify/Create:**
- Create: `client/src/app/core/auth/auth.guard.ts`
- Modify: `client/src/app/app.routes.ts`

**Technical Notes:**
Use functional guards (`CanActivateFn`). Lazy-load feature components via `loadComponent: () => import('...')`.

---

### T-015: Login screen

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** M · **Dependencies:** T-011, T-013, T-014

**Description:**
Build `LoginPageComponent` per `docs/ui-specification.md` § Login: email + password fields, Sign-in button, error banner on auth failure, loading state with inline spinner. Submits to `AuthService.login()`. On success, navigates to `/`.

**Rationale:**
The single entry point for end users. Direct mapping to AC-3.

**Acceptance Criteria:**
- [ ] Submitting valid credentials lands on `/` with the seed operator visible from `GET /api/auth/me`.
- [ ] Invalid credentials produce an inline error banner with the RFC 7807 `title` + `detail`; the button re-enables.
- [ ] Loading state disables both fields and the button; button shows inline spinner.
- [ ] Pressing Enter inside either field submits the form.
- [ ] Spec covers happy path, validation error, server error (401), and loading.

**Files to Modify/Create:**
- Create: `client/src/app/features/login/login.page.{ts,html,spec.ts}`
- Create: `mockups/login.html`
- Modify: `client/src/app/app.routes.ts` (already added in T-014; this task supplies the actual component)

**Technical Notes:**
Form via `ReactiveFormsModule` standalone import. Validation: email required + format, password required min length 1 (server validates everything else). Mockup-first per CLAUDE.md routing — produce `mockups/login.html` first.

---

### T-016: Home screen (placeholder with empty state)

**Type:** Frontend · **Workflow:** mockup-first · **Complexity:** S · **Dependencies:** T-011, T-012, T-014

**Description:**
Build `HomePageComponent` per `docs/ui-specification.md` § Home: a welcome heading using the resolved member's `displayName`, a "Pending on you" section that renders the `EmptyState` placeholder ("You're all caught up." — the real list lands in FEAT-005), and a "Your projects" section that renders an `EmptyState` ("An operator hasn't added you to any project yet." — the real grid lands in FEAT-002).

**Rationale:**
AC-3 lands here. Demonstrates the App shell + foundational components end-to-end.

**Acceptance Criteria:**
- [ ] After login as the seed operator, navigating to `/` renders the welcome heading with the operator's display name.
- [ ] Both sections show the empty state from T-011's `EmptyState` component.
- [ ] No real API calls beyond `/api/auth/me` (which already happened during bootstrap).
- [ ] Mockup-first: `mockups/home-empty.html` produced before implementation.

**Files to Modify/Create:**
- Create: `client/src/app/features/home/home.page.{ts,html,spec.ts}`
- Create: `mockups/home-empty.html`
- Modify: `client/src/app/app.routes.ts`

**Technical Notes:**
Reads `displayName` from `AuthService.currentMember()` signal. No HTTP calls.

---

## DevOps

### T-017: API Dockerfile (multi-stage SDK → ASP.NET) and .dockerignore

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-004

**Description:**
Author a root-level `Dockerfile` for the API with two stages: `mcr.microsoft.com/dotnet/sdk:10.0` for build/publish, and `mcr.microsoft.com/dotnet/aspnet:10.0` for the final image. Copy only what's needed (`.csproj` first for layer caching, then sources). Final image runs `dotnet DevHub.Api.dll` on `:8080` and reads all config from env vars.

**Rationale:**
Profile rule: multi-stage builds, `dotnet/aspnet` final stage, env-agnostic image, secrets never baked in.

**Acceptance Criteria:**
- [ ] `docker build -t devhub-api .` succeeds.
- [ ] Resulting image listens on `:8080` and serves `/health`.
- [ ] Image has no `.env` files, no source code, no SDK — only published binaries.
- [ ] `.dockerignore` excludes `bin/`, `obj/`, `client/`, `tests/`, `.git/`, `*.env*`.

**Files to Modify/Create:**
- Create: `Dockerfile`
- Create: `.dockerignore`

**Technical Notes:**
Restore using `dotnet restore DevHub.sln`; publish only `DevHub.Api` (`dotnet publish src/DevHub.Api -c Release -o /app/publish`). Set `ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_ENVIRONMENT=Production`. Run as non-root user (`USER 1000:1000`).

---

### T-018: Client Dockerfile (node build → nginx) and nginx.conf

**Type:** DevOps · **Workflow:** standard · **Complexity:** S · **Dependencies:** T-010

**Description:**
Author `client/Dockerfile` with a Node build stage (`node:20-alpine` runs `npm ci && ng build`) and an nginx final stage (`nginx:1.27-alpine`) that copies the build output to `/usr/share/nginx/html`. Author `client/nginx.conf` to serve the SPA, fall back to `index.html` via `try_files` for client-side routing, and reverse-proxy `/api/` to `http://api:8080`.

**Rationale:**
Profile rule: nginx-spa-proxy + container-per-process. DevHub is the single origin in production.

**Acceptance Criteria:**
- [ ] `docker build -t devhub-web client/` succeeds.
- [ ] Resulting image serves `/` with the SPA index and a 200 status.
- [ ] Deep-link `/projects/foo` returns the SPA (not 404), via `try_files`.
- [ ] `/api/health` is reverse-proxied to the API container.

**Files to Modify/Create:**
- Create: `client/Dockerfile`
- Create: `client/nginx.conf`

**Technical Notes:**
`location /api/ { proxy_pass http://api:8080/api/; proxy_http_version 1.1; proxy_buffering off; proxy_request_buffering off; proxy_set_header Connection ""; ... }` — `proxy_buffering off` is required for SSE pass-through later.

---

### T-019: docker-compose.prod.yml and verify-docker.sh

**Type:** DevOps · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-017, T-018, T-008

**Description:**
Author `docker-compose.prod.yml` describing the API and frontend services on a shared `infra` external network, with healthchecks and `depends_on: condition: service_healthy` on the API. Author `scripts/verify-docker.sh` that brings up the stack, polls `http://localhost:<port>/health` until it returns 200, hits the SPA root, and tears down on exit.

**Rationale:**
AC-6 of FEAT-001. Profile rule: prod compose on a shared infra network with healthchecks gating startup.

**Acceptance Criteria:**
- [ ] `docker compose -f docker-compose.prod.yml up -d` brings up API + frontend successfully.
- [ ] `scripts/verify-docker.sh` returns 0 on a clean machine and tears the stack down even on failure.
- [ ] No image contains `.env` or source code.

**Files to Modify/Create:**
- Create: `docker-compose.prod.yml`
- Create: `.env.production.example`
- Create: `scripts/verify-docker.sh` (executable, `set -euo pipefail`, `trap "docker compose -f docker-compose.prod.yml down" EXIT`)

**Technical Notes:**
The `infra` external network is documented in README.md (`docker network create infra` is a prerequisite). Postgres lives on the `infra` network and is referenced by URL `postgresql://devhub:...@infra-postgres:5432/DevHub` — the application is environment-agnostic.

---

## Testing

### T-020: xUnit + Testcontainers fixture and one passing test per module

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-005, T-006, T-009

**Description:**
Add xUnit, FluentAssertions, and Testcontainers (Postgres) to each `tests/DevHub.Modules.<Module>.Tests/` project. Create a shared `PostgresFixture` (collection fixture) that spins up a Postgres container once per test run and applies the relevant module's migrations to a fresh database. Write one passing test per module (e.g. "DbContext can connect and apply migrations").

**Rationale:**
AC-5 of FEAT-001 ("at least one test per module project"). Establishes the integration-test pattern (real Postgres via Testcontainers) for every subsequent backend feature.

**Acceptance Criteria:**
- [ ] `dotnet test` runs and all module test projects pass.
- [ ] The `PostgresFixture` is shared across tests (single container per run) and creates an isolated database per test class.
- [ ] No test depends on a host-installed Postgres.

**Files to Modify/Create:**
- Modify: each `tests/DevHub.Modules.<Module>.Tests/*.csproj` (add `Testcontainers.PostgreSql`, `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`)
- Create: `tests/DevHub.TestHarness/PostgresFixture.cs`, `PostgresCollection.cs`, `DevHub.TestHarness.csproj` (referenced by all test projects)
- Create: one `*.Tests.cs` per module test project

**Technical Notes:**
`PostgresFixture` exposes `string ConnectionString` and a helper `Task<TContext> CreateAsync<TContext>()`. Use `[Collection("postgres")]` to share the container. For the auth integration tests in T-007's AC, reuse this fixture via the WebApplicationFactory pattern.

---

### T-021: Angular spec scaffolding + AuthService and shared-component specs

**Type:** Testing · **Workflow:** standard · **Complexity:** M · **Dependencies:** T-011, T-013

**Description:**
Ensure `ng test` runs in headless Chrome with Karma + Jasmine. Add specs for `AuthService` (login, refresh-on-401, double-401 logout) using `HttpTestingController`, and confirm specs already authored in T-011/T-012/T-015/T-016 pass.

**Rationale:**
AC-5 of FEAT-001 for the frontend side. Establishes the test pattern for later screens.

**Acceptance Criteria:**
- [ ] `cd client && ng test --watch=false --browsers=ChromeHeadless` exits 0.
- [ ] `AuthService` specs cover login, refresh-on-401, and double-401-logout flows.
- [ ] Shared-component specs from T-011 and layout specs from T-012 are part of the suite.

**Files to Modify/Create:**
- Modify: `client/karma.conf.js` (ChromeHeadless launcher)
- Modify: `client/angular.json` (test runner config)
- Create: additional spec files only if missing from T-011/T-013

**Technical Notes:**
Use Angular's `provideHttpClientTesting` and `HttpTestingController`. CI command becomes `ng test --watch=false --code-coverage --browsers=ChromeHeadless`.

---

## Summary

| Group | Count |
|-------|-------|
| Foundation | 4 (T-001 to T-004) |
| Backend — Identity & seed | 5 (T-005 to T-009) |
| Frontend | 7 (T-010 to T-016) |
| DevOps | 3 (T-017 to T-019) |
| Testing | 2 (T-020 to T-021) |
| **Total** | **21 tasks** |

**Complexity distribution:** S=6 · M=10 · L=5 · XL=0.

**Critical path:** T-001 → T-002 → T-004 → T-005 → T-006 → T-007 → T-016 (end-to-end login). This is the longest dependency chain at 7 tasks. Frontend foundation (T-010 → T-011 → T-013 → T-015) can proceed in parallel.

**Parallelism plan:**
- After T-004, T-009 can proceed in parallel with the Identity backend chain (T-005→T-007).
- T-010 has no backend dependency and can start day 1.
- T-017 / T-018 / T-019 unblock once their respective build artifacts compile.

**Risks / open questions:**
- **Argon2id parameters** in T-006 are placeholder values; benchmark on target hardware before v1 cut.
- **JWT signing-key rotation** strategy is out of scope here but should be tracked as an IMP for v1.1 (cur. relies on env-var, no rotation).
- **Identity-provider posture** (Stakeholder open question) remains deferred — `IdentityCredential.provider` is `Local` for v1 but the schema is ready for `Federated`.
- **EF Core 10 + Npgsql** version compatibility — confirm versions on T-002 before proceeding (use the latest stable Npgsql that targets EF Core 10).
- **Tailwind 4 + Angular 20** integration — Tailwind v4 uses CSS-first config; confirm the Angular CLI dev server hot-reloads `@theme` changes correctly during T-010 (fallback: pin to Tailwind 3.4 if hot reload breaks).

## Post-Generation Checklist

- [x] All FEAT-001 acceptance criteria have corresponding tasks (AC-1↔T-008, AC-2↔T-014/T-015, AC-3↔T-015/T-016, AC-4↔T-007, AC-5↔T-020/T-021, AC-6↔T-019, AC-7↔T-005..T-009).
- [x] Database/model changes (T-002, T-005, T-006, T-009) precede code that uses them (T-007).
- [x] API endpoints (T-007, T-008) defined before frontend integration (T-013, T-015, T-016).
- [x] Error handling addressed in T-004 (RFC 7807 handler) and T-013 (problem-details interceptor).
- [x] Testing tasks (T-020, T-021) cover happy-path and edge cases (refresh-on-401, double-401 logout, db-down health check).
- [x] No task violates the Stakeholder scope lock (no executor work, no cross-project state, no end-user-facing direct executor access).
- [x] Dependency graph is acyclic (verified above).
- [x] Critical path identified and sensible.
