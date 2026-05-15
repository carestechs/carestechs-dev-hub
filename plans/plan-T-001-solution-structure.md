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
**File:** `DevHub.sln`
**Action:** Create
Run `dotnet new sln -n DevHub` at the repo root.

### Step 2: Add solution-wide build conventions
**File:** `Directory.Build.props`
**Action:** Create
Set `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>latest</LangVersion>`. Goes in repo root so every `.csproj` inherits.

### Step 3: Add `.editorconfig`
**File:** `.editorconfig`
**Action:** Create
C# conventions: 4-space indent, file-scoped namespaces (`csharp_style_namespace_declarations = file_scoped:warning`), `dotnet_diagnostic.CS1591.severity = none` to keep XML-doc warnings off for now.

### Step 4: Create `DevHub.Api` (thin host)
**File:** `src/DevHub.Api/DevHub.Api.csproj`
**Action:** Create
`dotnet new web -n DevHub.Api -o src/DevHub.Api`. Add to solution: `dotnet sln add src/DevHub.Api`.

### Step 5: Create `DevHub.Contracts`
**File:** `src/DevHub.Contracts/DevHub.Contracts.csproj`
**Action:** Create
`dotnet new classlib -n DevHub.Contracts -o src/DevHub.Contracts`. Add to solution. **Has no project references** (it is the contract).

### Step 6: Create the six module projects
**File:** `src/DevHub.Modules.<Module>/DevHub.Modules.<Module>.csproj` (×6)
**Action:** Create
For each of `Workspace`, `Identity`, `ExecutorRegistry`, `WorkItems`, `Audit`, `Notifications`:
- `dotnet new classlib -n DevHub.Modules.<Module> -o src/DevHub.Modules.<Module>`
- `dotnet add src/DevHub.Modules.<Module> reference src/DevHub.Contracts`
- `dotnet sln add src/DevHub.Modules.<Module>`
Folder layout per module: `Controllers/`, `Services/`, `Entities/`, `DTOs/`, `Migrations/`. Leave folders empty for now (placeholder `.gitkeep` files are fine).

### Step 7: Wire `DevHub.Api` references
**File:** `src/DevHub.Api/DevHub.Api.csproj`
**Action:** Modify
Add a `ProjectReference` to `DevHub.Contracts` and all six module projects. This is the **only** project that references every module.

### Step 8: Create the test projects
**File:** `tests/DevHub.Modules.<Module>.Tests/DevHub.Modules.<Module>.Tests.csproj` (×6)
**Action:** Create
`dotnet new xunit -n DevHub.Modules.<Module>.Tests -o tests/DevHub.Modules.<Module>.Tests` and add `ProjectReference` to the corresponding module + to `DevHub.Contracts`. Add to solution.

### Step 9: Build
**Action:** Verify
Run `dotnet build`; expect zero errors and zero warnings.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `DevHub.sln` | Create | Solution root |
| `Directory.Build.props` | Create | Solution-wide build options (`net10.0`, nullable, warnings-as-errors) |
| `.editorconfig` | Create | C# style conventions |
| `src/DevHub.Api/DevHub.Api.csproj` | Create | Thin host project |
| `src/DevHub.Contracts/DevHub.Contracts.csproj` | Create | Shared interfaces/DTOs project |
| `src/DevHub.Modules.<Module>/*.csproj` | Create (×6) | Feature modules |
| `tests/DevHub.Modules.<Module>.Tests/*.csproj` | Create (×6) | Per-module xUnit test projects |

## Edge Cases & Risks
- **Cross-module project references would break the modular monolith rule.** Enforce by review (or, optionally, a `Directory.Build.targets` analyzer in a later IMP).
- **`DevHub.Api` becoming a dumping ground for logic.** Mitigate by keeping `Program.cs` short (T-004) and routing everything through module extension methods.

## Acceptance Verification
- [ ] `dotnet build` succeeds with zero errors and zero warnings.
- [ ] `dotnet sln list` shows all 14 projects (1 host + 1 contracts + 6 modules + 6 test projects).
- [ ] `grep -r "ProjectReference Include=\"..\\DevHub.Modules" src/DevHub.Modules.*/*.csproj` returns empty (no module references another module).
- [ ] `tests/DevHub.Modules.<Module>.Tests/` builds for each module.
