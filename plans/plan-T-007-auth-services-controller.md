# Implementation Plan: T-007 — Auth services and AuthController endpoints

## Task Reference
- **Task ID:** T-007
- **Type:** Backend
- **Workflow:** standard
- **Complexity:** L
- **Rationale:** Implements every Identity endpoint in `api-spec.md`. Satisfies FEAT-001 AC-3 (login → access token + refresh cookie → empty home).

## Overview
Layer the four authentication endpoints onto the entities + hasher from T-005/T-006: access-token issuance, refresh rotation, logout (revocation), and "me" resolution. Controllers are thin; logic lives in services.

## Implementation Steps

### Step 1: JWT token issuer
**Files:** `src/Portfolio.Modules.Identity/Services/IJwtTokenIssuer.cs`, `JwtTokenIssuer.cs`
**Action:** Create
```csharp
public interface IJwtTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(Guid memberId, IEnumerable<string> roleKeys);
}
```
Implementation reads `JwtOptions`, builds a `JwtSecurityToken` with claims (`sub`=memberId, `role`=each roleKey), `iss` and `aud` from options, `exp` 15 min from now. Sign with `HmacSha256` over the configured signing key.

### Step 2: Refresh-token store
**Files:** `src/Portfolio.Modules.Identity/Services/IRefreshTokenStore.cs`, `RefreshTokenStore.cs`
**Action:** Create
```csharp
public interface IRefreshTokenStore
{
    Task<string> IssueAsync(Guid memberId, CancellationToken ct);   // returns the raw token
    Task<Guid?> ValidateAsync(string rawToken, CancellationToken ct); // returns memberId or null
    Task<string> RotateAsync(string oldRawToken, CancellationToken ct); // new raw token; old is revoked & linked
    Task RevokeChainAsync(string rawToken, CancellationToken ct);
}
```
Internals:
- Generate raw token: 32 random bytes, base64url-encoded.
- Hash with SHA-256; store hash + `IssuedAt` + `ExpiresAt = now + 14 days`.
- `RotateAsync`: validate old, set its `RevokedAt = now` and `ReplacedByTokenId = newId`. If old token's `RevokedAt` is already set → suspected replay → revoke the entire chain and return null/throw.

### Step 3: Authentication service
**Files:** `src/Portfolio.Modules.Identity/Services/IAuthenticationService.cs`, `AuthenticationService.cs`
**Action:** Create
Operations:
- `LoginAsync(email, password)` → `(accessToken, expiresAt, refreshTokenRaw, memberDto)`. Steps:
  1. `IMemberLookup.FindByEmailAsync(email)` → memberId. If null → `UnauthorizedException` (bad creds).
  2. Load `IdentityCredential` for that memberId. If null or not `Local` → bad creds.
  3. `IPasswordHasher.Verify(password, hash)`. Bad → bad creds.
  4. Check member status. `Suspended` → `ForbiddenException("Member suspended")`. `Invited` → `ForbiddenException("Member must complete onboarding")`.
  5. Look up role keys via Contracts query → `IJwtTokenIssuer.Issue(memberId, roleKeys)`.
  6. `IRefreshTokenStore.IssueAsync(memberId)`.
  7. Return result.
- `RefreshAsync(rawRefreshToken)` → `(accessToken, expiresAt, newRefreshRaw)`. `RotateAsync` + reissue access token.
- `LogoutAsync(rawRefreshToken)` → `Task`. `RevokeChainAsync`.
- `GetCurrentMemberAsync(memberId)` → `MeResponse`. Fetch member + memberships via Contracts.

### Step 4: Current-member accessor
**Files:** `src/Portfolio.Contracts/Identity/ICurrentMember.cs` (already in T-007 task list — finalize here), `src/Portfolio.Modules.Identity/Services/CurrentMemberAccessor.cs`
**Action:** Create
`ICurrentMember` exposes `Guid MemberId`, `IReadOnlyList<string> RoleKeys` (workspace-global), `bool IsAuthenticated`. Implementation reads `IHttpContextAccessor.HttpContext.User`. Register in DI as scoped.

### Step 5: DTOs
**Files:** `src/Portfolio.Modules.Identity/DTOs/*.cs`
**Action:** Create
- `LoginRequest { string Email; string Password; }` — with FluentValidation rules (`[Required]`, `[EmailAddress]`).
- `LoginResponse { string AccessToken; DateTimeOffset ExpiresAt; MemberDto Member; }`.
- `RefreshResponse { string AccessToken; DateTimeOffset ExpiresAt; }`.
- `MeResponse { MemberDto Member; List<MembershipDto> Memberships; }`.
- `MemberDto { Guid Id; string DisplayName; string Email; }`.
- `MembershipDto { Guid ProjectId; string ProjectSlug; string[] Roles; }`.
- Wrap success responses in `EnvelopeDto<T>` from `Portfolio.Contracts`.

### Step 6: AuthController
**File:** `src/Portfolio.Modules.Identity/Controllers/AuthController.cs`
**Action:** Create
Routes:
```csharp
[ApiController, Route("api/auth")]
public sealed class AuthController(IAuthenticationService auth) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.Email, req.Password, ct);
        Response.Cookies.Append("refresh", result.RefreshTokenRaw, RefreshCookieOptions(req));
        return Ok(new EnvelopeDto<LoginResponse>(new(result.AccessToken, result.ExpiresAt, result.Member)));
    }

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("refresh", out var raw))
            throw new ForbiddenException("missing-refresh", "Missing refresh cookie.");
        var result = await auth.RefreshAsync(raw, ct);
        Response.Cookies.Append("refresh", result.NewRefreshRaw, RefreshCookieOptions());
        return Ok(new EnvelopeDto<RefreshResponse>(new(result.AccessToken, result.ExpiresAt)));
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue("refresh", out var raw))
            await auth.LogoutAsync(raw, ct);
        Response.Cookies.Delete("refresh", new() { Path = "/api/auth" });
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(ICurrentMember me, CancellationToken ct)
    {
        var resp = await auth.GetCurrentMemberAsync(me.MemberId, ct);
        return Ok(new EnvelopeDto<MeResponse>(resp));
    }
}
```
`RefreshCookieOptions`: `HttpOnly = true`, `Secure = !env.IsDevelopment() || request is https`, `SameSite = SameSiteMode.Lax`, `Path = "/api/auth"`, `Expires = now + 14 days`.

### Step 7: Wire DI
**File:** `src/Portfolio.Modules.Identity/IdentityModuleExtensions.cs`
**Action:** Modify
Register `IJwtTokenIssuer`, `IRefreshTokenStore`, `IAuthenticationService`, `ICurrentMember`. Add `services.AddHttpContextAccessor()`.

### Step 8: Integration tests
**File:** `tests/Portfolio.Modules.Identity.Tests/AuthEndpointsTests.cs`
**Action:** Create
Using the Testcontainers fixture (T-020) + `WebApplicationFactory`, cover:
- Login with seeded operator → 200, body shape, refresh cookie present.
- Login with bad password → 401 problem-details.
- Login with bad email → 401 problem-details.
- Refresh with valid cookie → 200, new cookie, new access token.
- Refresh with no cookie → 401 problem-details.
- Reusing a rotated refresh → entire chain revoked, subsequent refresh fails.
- Logout → 204 and cookie cleared.
- `/me` with token → 200 with empty `memberships` list.
- `/me` without token → 401.

## Files Affected
| File | Action | Summary |
|------|--------|---------|
| `src/Portfolio.Modules.Identity/Services/IJwtTokenIssuer.cs`, `JwtTokenIssuer.cs` | Create | Access-token issuance |
| `src/Portfolio.Modules.Identity/Services/IRefreshTokenStore.cs`, `RefreshTokenStore.cs` | Create | Refresh rotation + revocation |
| `src/Portfolio.Modules.Identity/Services/IAuthenticationService.cs`, `AuthenticationService.cs` | Create | Login/Refresh/Logout/Me |
| `src/Portfolio.Modules.Identity/Services/CurrentMemberAccessor.cs` | Create | Resolves `ICurrentMember` from `HttpContext.User` |
| `src/Portfolio.Modules.Identity/DTOs/*.cs` | Create | Request/response DTOs |
| `src/Portfolio.Modules.Identity/Controllers/AuthController.cs` | Create | Endpoints |
| `src/Portfolio.Contracts/Identity/ICurrentMember.cs` | Create | Cross-module current-member |
| `src/Portfolio.Contracts/EnvelopeDto.cs` | Create | `{ data, meta }` envelope record |
| `src/Portfolio.Modules.Identity/IdentityModuleExtensions.cs` | Modify | DI |
| `tests/Portfolio.Modules.Identity.Tests/AuthEndpointsTests.cs` | Create | Integration tests |

## Edge Cases & Risks
- **Same-IP brute force** — no rate limiting in this task; tracked as a v1 IMP. Document the gap.
- **Cookie behind a non-TLS dev proxy** — `Secure = !env.IsDevelopment()` keeps the cookie usable in HTTP-localhost dev; production-mode running on plain HTTP would silently drop the cookie and surface as 401-on-refresh. Document.
- **Refresh-token replay** — implemented above via chain revocation. Add a focused test for this scenario.
- **Clock skew** — already addressed by `ClockSkew = 30s` in `JwtBearerOptions` (T-004).
- **CORS + credentials** — `SameSite=Lax` is the right default for the SPA's same-site `/api/` proxy; cross-site usage would require `SameSite=None` and `Secure=true`. Out of scope.

## Acceptance Verification
- [ ] Login with valid seed credentials returns 200 with the documented body shape and sets the refresh cookie.
- [ ] Login with bad credentials returns 401 `application/problem+json`.
- [ ] Login when member status = `Suspended` returns 403.
- [ ] Refresh rotates the token (new value in cookie, old hash marked `revoked_at`).
- [ ] Logout returns 204 and revokes the chain.
- [ ] `/api/auth/me` returns the seed operator with empty `memberships`.
- [ ] Reusing a rotated refresh revokes the entire chain (replay defense).
