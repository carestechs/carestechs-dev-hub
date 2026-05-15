# Implementation Plan: T-004 — Program.cs composition root

## Task Reference
- **Task ID:** T-004
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** L
- **Rationale:** Profile rule "thin API host": no controllers, services, or business logic in `DevHub.Api`. Centralized exception handler produces uniform problem-details across modules.

## Overview
Compose the API host: DI registration, JWT bearer, RFC 7807 exception handler, CORS, request logging, health-check registration, and the `Add<Module>Module()` / `Use<Module>Module()` invocations. Publish `DomainException` family from `DevHub.Contracts`.

## Implementation Steps

### Step 1: Domain exception family
**File:** `src/DevHub.Contracts/ApplicationErrors/DomainException.cs` (plus siblings)
**Action:** Create
Abstract base `DomainException(string title, string detail, int status, string typeUri)`. Concrete subclasses:
- `NotFoundException` (404, `/probs/not-found`)
- `ForbiddenException` (403, `/probs/forbidden`)
- `ValidationException` (400, `/probs/validation`) — accepts a `Dictionary<string, string[]>` for the `errors` field
- `ConflictException` (409, `/probs/conflict`)
- `ExecutorFailureException` (502, `/probs/executor-failure`) — accepts executor key and correlationId

### Step 2: Problem-details handler
**File:** `src/DevHub.Api/Middleware/ProblemDetailsHandler.cs`
**Action:** Create
Implement `IExceptionHandler`. Map any `DomainException` → its declared status and `type`. Map `Microsoft.AspNetCore.Authorization.AuthorizationFailedException` → 403/forbidden. Map everything else to 500/`/probs/internal`. Always include `correlationId` (from `Activity.Current?.Id` or a generated ULID) and `instance` (the request path).

### Step 3: appsettings
**Files:** `src/DevHub.Api/appsettings.json`, `appsettings.Development.json`
**Action:** Create
Keys: `ConnectionStrings:Postgres`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`, `Cors:SpaOrigin`, `OperatorSeed:Email`, `OperatorSeed:DisplayName`, `OperatorSeed:Password`. Values in `appsettings.json` are placeholders; env-var binding overrides them.

### Step 4: Strongly-typed options
**File:** `src/DevHub.Api/Options/JwtOptions.cs`, `CorsOptions.cs`, `OperatorSeedOptions.cs`
**Action:** Create
Each with `[Required]` attributes on critical fields. Register with `services.AddOptions<JwtOptions>().Bind(cfg.GetSection("Jwt")).ValidateDataAnnotations().ValidateOnStart();`

### Step 5: Program.cs
**File:** `src/DevHub.Api/Program.cs`
**Action:** Modify
Pipeline order:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TimestampingInterceptor>();

builder.Services
    .AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection("Jwt")).ValidateDataAnnotations().ValidateOnStart();
// (CorsOptions, OperatorSeedOptions similarly)

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
        opts.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:SpaOrigin"]!)
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsHandler>();

builder.Services.AddControllers();

// Module registration — order does not matter; each module is self-contained
builder.Services
    .AddWorkspaceModule(builder.Configuration)
    .AddIdentityModule(builder.Configuration)
    .AddExecutorRegistryModule(builder.Configuration)
    .AddWorkItemsModule(builder.Configuration)
    .AddAuditModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Health endpoint registered in T-008; per-module pipeline hooks via Use*Module() are no-ops for now.

app.Run();
```

### Step 6: Compile
**Action:** Verify
`dotnet build` succeeds.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Contracts/ApplicationErrors/*.cs` | Create | Domain exception family |
| `src/DevHub.Api/Middleware/ProblemDetailsHandler.cs` | Create | RFC 7807 translator |
| `src/DevHub.Api/appsettings.json`, `appsettings.Development.json` | Create | Config defaults |
| `src/DevHub.Api/Options/*.cs` | Create | Strongly-typed, validated options |
| `src/DevHub.Api/Program.cs` | Modify | Composition root |
| `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` | Modify | `Add<Module>Module` extensions called by Program.cs |

## Edge Cases & Risks
- **JWT signing key length** — `SymmetricSecurityKey` requires ≥256 bits (32 bytes). `.env.example` notes "at least 32-byte". Add a guard in `JwtOptions.SigningKey` (DataAnnotation `MinLength(32)`).
- **CORS + credentials** — `AllowCredentials()` requires explicit origins (no `AllowAnyOrigin`). Configured single SPA origin meets this; widening would need re-thinking.
- **Exception handler order** — `UseExceptionHandler()` must be early in the pipeline (before routing) to catch routing-phase failures cleanly.
- **Module registration order** — independent today, but if a module's `AddXModule` ever depends on services from another, the dependency must be made explicit through `DevHub.Contracts`.

## Acceptance Verification
- [ ] `Program.cs` contains only DI registration and pipeline composition; `grep "class.*Controller" src/DevHub.Api/` returns nothing.
- [ ] Throwing a `NotFoundException` from any module's service produces a 404 with `application/problem+json` and `type: .../probs/not-found`.
- [ ] Starting with `Jwt:SigningKey` shorter than 32 bytes fails fast at startup with a validation error.
- [ ] CORS preflight from `Cors:SpaOrigin` succeeds; from a different origin returns 403/CORS rejection.
