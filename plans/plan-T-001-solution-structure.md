# Implementation Plan: T-001 — Create .NET solution and project structure

## Task Reference
- **Task ID:** T-001
- **Type:** DevOps
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** Foundational — every later backend task lands in one of these projects. The modular monolith structure is required by the architecture profile.

## Overview
Lay down the .NET solution, the thin API host, the shared contracts project, the six feature modules, and their test projects — with the per-module folder layout from the architecture profile. After this step, `dotnet build` succeeds; no code lives anywhere yet beyond empty classes.

## Implementation Steps

### Step 1: Create the solution file
**File:** `Portfolio.sln`
**Action:** Create
Run `dotnet new sln -n Portfolio` at the repo root.

### Step 2: Add solution-wide build conventions
**File:** `Directory.Build.props`
**Action:** Create
Set `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>latest</LangVersion>`. Goes in repo root so every `.csproj` inherits.

### Step 3: Add `.editorconfig`
**File:** `.editorconfig`
**Action:** Create
C# conventions: 4-space indent, file-scoped namespaces (`csharp_style_namespace_declarations = file_scoped:warning`), `dotnet_diagnostic.CS1591.severity = none` to keep XML-doc warnings off for now.

### Step 4: Create `Portfolio.Api` (thin host)
**File:** `src/Portfolio.Api/Portfolio.Api.csproj`
**Action:** Create
`dotnet new web -n Portfolio.Api -o src/Portfolio.Api`. Add to solution: `dotnet sln add src/Portfolio.Api`.

### Step 5: Create `Portfolio.Contracts`
**File:** `src/Portfolio.Contracts/Portfolio.Contracts.csproj`
**Action:** Create
`dotnet new classlib -n Portfolio.Contracts -o src/Portfolio.Contracts`. Add to solution. **Has no project references** (it is the contract).

### Step 6: Create the six module projects
**File:** `src/Portfolio.Modules.<Module>/Portfolio.Modules.<Module>.csproj` (×6)
**Action:** Create
For each of `Workspace`, `Identity`, `ExecutorRegistry`, `WorkItems`, `Audit`, `Notifications`:
- `dotnet new classlib -n Portfolio.Modules.<Module> -o src/Portfolio.Modules.<Module>`
- `dotnet add src/Portfolio.Modules.<Module> reference src/Portfolio.Contracts`
- `dotnet sln add src/Portfolio.Modules.<Module>`
Folder layout per module: `Controllers/`, `Services/`, `Entities/`, `DTOs/`, `Migrations/`. Leave folders empty for now (placeholder `.gitkeep` files are fine).

### Step 7: Wire `Portfolio.Api` references
**File:** `src/Portfolio.Api/Portfolio.Api.csproj`
**Action:** Modify
Add a `ProjectReference` to `Portfolio.Contracts` and all six module projects. This is the **only** project that references every module.

### Step 8: Create the test projects
**File:** `tests/Portfolio.Modules.<Module>.Tests/Portfolio.Modules.<Module>.Tests.csproj` (×6)
**Action:** Create
`dotnet new xunit -n Portfolio.Modules.<Module>.Tests -o tests/Portfolio.Modules.<Module>.Tests` and add `ProjectReference` to the corresponding module + to `Portfolio.Contracts`. Add to solution.

### Step 9: Build
**Action:** Verify
Run `dotnet build`; expect zero errors and zero warnings.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `Portfolio.sln` | Create | Solution root |
| `Directory.Build.props` | Create | Solution-wide build options (`net10.0`, nullable, warnings-as-errors) |
| `.editorconfig` | Create | C# style conventions |
| `src/Portfolio.Api/Portfolio.Api.csproj` | Create | Thin host project |
| `src/Portfolio.Contracts/Portfolio.Contracts.csproj` | Create | Shared interfaces/DTOs project |
| `src/Portfolio.Modules.<Module>/*.csproj` | Create (×6) | Feature modules |
| `tests/Portfolio.Modules.<Module>.Tests/*.csproj` | Create (×6) | Per-module xUnit test projects |

## Edge Cases & Risks
- **Cross-module project references would break the modular monolith rule.** Enforce by review (or, optionally, a `Directory.Build.targets` analyzer in a later IMP).
- **`Portfolio.Api` becoming a dumping ground for logic.** Mitigate by keeping `Program.cs` short (T-004) and routing everything through module extension methods.

## Acceptance Verification
- [ ] `dotnet build` succeeds with zero errors and zero warnings.
- [ ] `dotnet sln list` shows all 14 projects (1 host + 1 contracts + 6 modules + 6 test projects).
- [ ] `grep -r "ProjectReference Include=\"..\\Portfolio.Modules" src/Portfolio.Modules.*/*.csproj` returns empty (no module references another module).
- [ ] `tests/Portfolio.Modules.<Module>.Tests/` builds for each module.
