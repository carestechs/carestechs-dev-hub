using DevHub.Contracts;
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Identity;
using DevHub.Modules.Identity.DTOs;
using DevHub.Modules.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DevHub.Modules.Identity.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthenticationService auth,
    IWebHostEnvironment env) : ControllerBase
{
    private const string RefreshCookie = "refresh";
    private const string RefreshCookiePath = "/api/auth";

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.Email, req.Password, ct);
        SetRefreshCookie(result.RefreshTokenRaw, result.RefreshExpiresAt);
        return Ok(new EnvelopeDto<LoginResponse>(
            new LoginResponse(result.AccessToken, result.AccessExpiresAt, result.Member)));
    }

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new UnauthorizedException("Missing refresh cookie.");

        var result = await auth.RefreshAsync(raw, ct);
        SetRefreshCookie(result.RefreshTokenRaw, result.RefreshExpiresAt);
        return Ok(new EnvelopeDto<RefreshResponse>(
            new RefreshResponse(result.AccessToken, result.AccessExpiresAt)));
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrWhiteSpace(raw))
            await auth.LogoutAsync(raw, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me([FromServices] ICurrentMember me, CancellationToken ct)
    {
        var response = await auth.GetCurrentMemberAsync(me.MemberId, ct);
        return Ok(new EnvelopeDto<MeResponse>(response));
    }

    private void SetRefreshCookie(string token, DateTimeOffset expiresAt)
    {
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = expiresAt,
        });
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
        });
    }
}
