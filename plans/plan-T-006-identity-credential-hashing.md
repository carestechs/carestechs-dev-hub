# Implementation Plan: T-006 — Identity module: IdentityCredential + RefreshToken + Argon2id hashing

## Task Reference
- **Task ID:** T-006
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** M
- **Rationale:** Stores authentication material. Argon2id matches modern best practice and avoids shipping ASP.NET Core Identity (over-scoped for this seam).

## Overview
Add Identity's two entities under the `identity` schema, ship Argon2id hashing as `IPasswordHasher`, and extend seeding so the operator member has a working local credential at first boot.

## Implementation Steps

### Step 1: Add Argon2 package
**File:** `src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj`
**Action:** Modify
Add `<PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.*" />`.

### Step 2: Entities
**Files:** `src/DevHub.Modules.Identity/Entities/IdentityCredential.cs`, `RefreshToken.cs`, `Enums/CredentialProvider.cs`
**Action:** Create
- `IdentityCredential` — `BaseEntity`. Fields: `MemberId (Guid)` (cross-module ref to Workspace.Member), `Provider (CredentialProvider)`, `PasswordHash (string?, 255)`, `FederatedSubject (string?, 255)`. No soft delete; deletion is hard.
- `RefreshToken` — `BaseEntity` (we reuse `Id` here as the row id). Fields: `MemberId (Guid)`, `TokenHash (string, 255)` (SHA-256 of the literal token), `IssuedAt`, `ExpiresAt`, `RevokedAt (DateTimeOffset?)`, `ReplacedByTokenId (Guid?)`.
- `CredentialProvider` — `Local`, `Federated`.

### Step 3: DbContext mappings
**File:** `src/DevHub.Modules.Identity/IdentityDbContext.cs`
**Action:** Modify
- `DbSet<IdentityCredential> Credentials`, `DbSet<RefreshToken> RefreshTokens`.
- `OnModelCreating`:
  - `HasDefaultSchema("identity")` (set in T-002).
  - `Entity<IdentityCredential>().HasIndex(c => c.MemberId).IsUnique();`
  - `Entity<IdentityCredential>().Property(c => c.Provider).HasConversion<string>();`
  - `Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();`
  - `Entity<RefreshToken>().HasOne<RefreshToken>().WithMany().HasForeignKey(t => t.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);`

### Step 4: Migration
**Action:** Generate
`dotnet ef migrations add Initial --project src/DevHub.Modules.Identity --startup-project src/DevHub.Api`. Verify schema = `identity`, columns snake_case, no cross-module FK constraints (Workspace.Member.Id is referenced by ID only — no DB-level FK).

### Step 5: Password hasher
**Files:** `src/DevHub.Modules.Identity/Services/IPasswordHasher.cs`, `Argon2PasswordHasher.cs`
**Action:** Create
```csharp
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encoded);
}
```
`Argon2PasswordHasher` uses `Konscious.Security.Cryptography.Argon2id`:
- Salt: 16 random bytes from `RandomNumberGenerator`.
- Params: `MemorySize = 65536` (64 MB), `Iterations = 4`, `DegreeOfParallelism = 2`, `HashLengthInBytes = 32`.
- Output encoding: `argon2id$v=19$m=65536,t=4,p=2$<base64 salt>$<base64 hash>`.
- `Verify` parses the encoded form, re-hashes with the embedded params, compares with `CryptographicOperations.FixedTimeEquals`.

### Step 6: Identity seeder
**File:** `src/DevHub.Modules.Identity/Seeding/IdentitySeeder.cs`
**Action:** Create
`IHostedService`. On `StartAsync` (must run **after** WorkspaceSeeder):
1. `await db.Database.MigrateAsync()`.
2. Resolve seed operator member id via `IProjectMembershipQuery` or a temporary `IMemberLookup` helper published from `DevHub.Contracts`. (Add a simple `IMemberLookup.FindByEmailAsync(email)` to Contracts; Workspace implements it.)
3. If no `IdentityCredential` exists for that member id, insert one with provider=`Local`, password hash from `OPERATOR_SEED_PASSWORD`.
4. Idempotent.

### Step 7: Wire DI and ordering
**File:** `src/DevHub.Modules.Identity/IdentityModuleExtensions.cs`
**Action:** Modify
Register `IPasswordHasher`, `IdentitySeeder` as a hosted service. Ordering: hosted services run in registration order, so `Program.cs` must call `AddWorkspaceModule` before `AddIdentityModule` (already the case in T-004 plan).

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/DevHub.Modules.Identity/DevHub.Modules.Identity.csproj` | Modify | Add Argon2 package |
| `src/DevHub.Modules.Identity/Entities/IdentityCredential.cs` | Create | Credential entity |
| `src/DevHub.Modules.Identity/Entities/RefreshToken.cs` | Create | Refresh-token entity |
| `src/DevHub.Modules.Identity/Entities/Enums/CredentialProvider.cs` | Create | Enum |
| `src/DevHub.Modules.Identity/IdentityDbContext.cs` | Modify | DbSets + mappings |
| `src/DevHub.Modules.Identity/Migrations/*` | Create | Initial migration |
| `src/DevHub.Modules.Identity/Services/IPasswordHasher.cs` | Create | Interface |
| `src/DevHub.Modules.Identity/Services/Argon2PasswordHasher.cs` | Create | Argon2id implementation |
| `src/DevHub.Modules.Identity/Seeding/IdentitySeeder.cs` | Create | Seeds operator credential |
| `src/DevHub.Contracts/Identity/IMemberLookup.cs` | Create | Cross-module ID resolution |
| `src/DevHub.Modules.Workspace/Services/MemberLookup.cs` | Create | Workspace's implementation |
| `src/DevHub.Modules.Identity/IdentityModuleExtensions.cs` | Modify | Register hasher + seeder |

## Edge Cases & Risks
- **Argon2 params on weak hardware** — 64 MB × 2 lanes can be slow on a small dev laptop. Benchmark in `Argon2PasswordHasherTests` (T-020) and document the chosen params; revisit before v1 cut.
- **Cross-module member lookup** — Identity must not load Workspace's `Member` entity directly. Use the `IMemberLookup` interface published in `DevHub.Contracts`; Workspace implements it; this preserves the "ID-only cross-module reference" rule.
- **Refresh token replay** — `TokenHash` uniqueness alone is not enough; the rotation chain in T-007 sets `RevokedAt` and `ReplacedByTokenId` so a reused old token can be detected.

## Acceptance Verification
- [ ] `dotnet ef database update --project src/DevHub.Modules.Identity` applies cleanly.
- [ ] `IPasswordHasher.Hash("pwd")` then `Verify("pwd", hash)` returns true; `Verify("other", hash)` returns false. (Unit test in T-020.)
- [ ] After two consecutive boots of the API, exactly one `identity.identity_credentials` row exists for the seeded operator.
- [ ] No `identity.identity_credentials.password_hash` is `null` for a `Local` provider row.
