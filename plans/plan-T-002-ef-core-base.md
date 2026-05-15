# Implementation Plan: T-002 — EF Core base (naming convention, timestamptz, UUID PKs, BaseEntity)

## Task Reference
- **Task ID:** T-002
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** Profile mandates snake_case, `timestamptz`, UUID PKs end-to-end. Centralizing the base avoids drift across modules.

## Overview
Install EF Core + Npgsql + naming-convention package in every module project, publish `BaseEntity`/`ISoftDeletable`/`TimestampingInterceptor` from `DevHub.Contracts`, and scaffold an empty `<Module>DbContext` per module that applies the snake_case convention and the timestamping interceptor.

## Implementation Steps

### Step 1: Add EF + Npgsql packages to every module
**File:** `src/DevHub.Modules.<Module>/DevHub.Modules.<Module>.csproj`
**Action:** Modify
Add `<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.*" />`, `<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.*" />` (PrivateAssets="all"), `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.*" />`, `<PackageReference Include="EFCore.NamingConventions" Version="10.0.*" />`. (Pin the highest stable matching EF 10 at implementation time.)

### Step 2: Define `BaseEntity`
**File:** `src/DevHub.Contracts/Persistence/BaseEntity.cs`
**Action:** Create
```csharp
public abstract class BaseEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Step 3: Define `ISoftDeletable`
**File:** `src/DevHub.Contracts/Persistence/ISoftDeletable.cs`
**Action:** Create
```csharp
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
```

### Step 4: Define `TimestampingInterceptor`
**File:** `src/DevHub.Contracts/Persistence/TimestampingInterceptor.cs`
**Action:** Create
Implement `SaveChangesInterceptor`. On `SavingChangesAsync`, walk `ChangeTracker.Entries<BaseEntity>()`. For `Added`: set `CreatedAt = UpdatedAt = DateTimeOffset.UtcNow`. For `Modified`: set `UpdatedAt = DateTimeOffset.UtcNow`. Never touch `CreatedAt` on modify.

### Step 5: Create per-module DbContext
**File:** `src/DevHub.Modules.<Module>/<Module>DbContext.cs` (×6)
**Action:** Create
```csharp
public sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("workspace"); // per-module schema; see plan-T-009 for the others
        base.OnModelCreating(modelBuilder);
    }
}
```
Identity uses `"identity"`, ExecutorRegistry `"executor_registry"`, WorkItems `"work_items"`, Audit `"audit"`, Notifications `"notifications"`.

### Step 6: Wire the DbContext via the module extension (placeholder until T-004)
**File:** `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×6)
**Action:** Create
```csharp
public static IServiceCollection AddWorkspaceModule(this IServiceCollection services, IConfiguration cfg)
{
    services.AddDbContext<WorkspaceDbContext>((sp, opts) =>
        opts.UseNpgsql(cfg.GetConnectionString("Postgres"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<TimestampingInterceptor>()));
    return services;
}
public static IApplicationBuilder UseWorkspaceModule(this IApplicationBuilder app) => app;
```
Register `TimestampingInterceptor` as a singleton in `DevHub.Api/Program.cs` (T-004).

### Step 7: Smoke build
**Action:** Verify
Run `dotnet build`; expect clean.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.<Module>/*.csproj` (×6) | Modify | Add EF Core + Npgsql + EFCore.NamingConventions |
| `src/DevHub.Contracts/Persistence/BaseEntity.cs` | Create | Shared base entity |
| `src/DevHub.Contracts/Persistence/ISoftDeletable.cs` | Create | Soft-delete marker interface |
| `src/DevHub.Contracts/Persistence/TimestampingInterceptor.cs` | Create | Auto-populates `CreatedAt`/`UpdatedAt` |
| `src/DevHub.Modules.<Module>/<Module>DbContext.cs` (×6) | Create | Empty DbContext with schema default |
| `src/DevHub.Modules.<Module>/<Module>ModuleExtensions.cs` (×6) | Create | `Add<Module>Module` extension wiring the DbContext |

## Edge Cases & Risks
- **Clock skew** — `DateTimeOffset.UtcNow` not `DateTime.UtcNow`; the latter loses offset information.
- **Migration files written before naming convention is applied** — would produce PascalCase columns. Confirm `UseSnakeCaseNamingConvention()` is in effect *before* generating any migration in T-005/T-006/T-009.
- **EF Core 10 + Npgsql 10 compatibility** — if Npgsql 10 is not yet stable at implementation time, fall back to the latest 9.x line and document the version pin in `Directory.Build.props`.

## Acceptance Verification
- [ ] A trivial test migration produces `snake_case` table/column names (verified in T-005).
- [ ] `Id` columns are `uuid` in PostgreSQL and `Guid` in C#.
- [ ] `BaseEntity` and `ISoftDeletable` exist in `DevHub.Contracts/Persistence/`.
- [ ] Inserting an entity through any DbContext populates `CreatedAt` and `UpdatedAt`; updating populates only `UpdatedAt` (verified in T-020 module-level test).
