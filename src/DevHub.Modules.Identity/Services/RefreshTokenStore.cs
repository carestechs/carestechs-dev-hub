using System.Security.Cryptography;
using DevHub.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Identity.Services;

internal sealed class RefreshTokenStore(IdentityDbContext db) : IRefreshTokenStore
{
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(14);

    public async Task<RefreshIssueResult> IssueAsync(Guid memberId, CancellationToken ct)
    {
        var raw = GenerateRawToken();
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(RefreshLifetime);
        db.RefreshTokens.Add(new RefreshToken
        {
            MemberId = memberId,
            TokenHash = HashRaw(raw),
            IssuedAt = now,
            ExpiresAt = expires,
        });
        await db.SaveChangesAsync(ct);
        return new RefreshIssueResult(raw, expires);
    }

    public async Task<RefreshRotateResult?> RotateAsync(string rawToken, CancellationToken ct)
    {
        var hash = HashRaw(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return null;

        // Replay defense: presenting an already-revoked token revokes the rest of the chain.
        if (token.RevokedAt is not null)
        {
            await RevokeChainStartingFromAsync(token, ct);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (token.ExpiresAt <= DateTimeOffset.UtcNow) return null;

        var newRaw = GenerateRawToken();
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(RefreshLifetime);
        var replacement = new RefreshToken
        {
            MemberId = token.MemberId,
            TokenHash = HashRaw(newRaw),
            IssuedAt = now,
            ExpiresAt = expires,
        };
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct); // assign Id

        token.RevokedAt = now;
        token.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(ct);

        return new RefreshRotateResult(token.MemberId, newRaw, expires);
    }

    public async Task RevokeChainAsync(string rawToken, CancellationToken ct)
    {
        var hash = HashRaw(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        await RevokeChainStartingFromAsync(token, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task RevokeChainStartingFromAsync(RefreshToken token, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Revoke forward chain.
        var current = token;
        while (current is not null)
        {
            current.RevokedAt ??= now;
            if (current.ReplacedByTokenId is null) break;
            current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == current.ReplacedByTokenId, ct);
        }
        // Revoke any tokens that point at any node we just touched (defensive).
        await db.RefreshTokens
            .Where(t => t.MemberId == token.MemberId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashRaw(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
