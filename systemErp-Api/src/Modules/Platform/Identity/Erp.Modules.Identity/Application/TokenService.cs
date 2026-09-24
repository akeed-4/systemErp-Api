using System.Security.Cryptography;
using System.Text;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Modules.Identity.Application;

internal sealed record IssuedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues the JWT (claims: sub, tenant_id, tenant_code, role, name, email) and a rotating refresh token.
/// Refresh token format "{tenantId:N}.{random}": the prefix routes the anonymous refresh call to the right tenant
/// database; only a SHA-256 hash is stored.
/// </summary>
internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock, IdentityDbContext db)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public IssuedTokens Issue(User user, Guid tenantId, string tenantCode)
    {
        var jwt = options.Value;
        var key = Encoding.UTF8.GetBytes(jwt.SigningKey);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        }

        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [ErpClaims.UserId] = user.Id.ToString(),
            [ErpClaims.TenantId] = tenantId.ToString(),
            [ErpClaims.TenantCode] = tenantCode,
            [ErpClaims.Role] = user.RoleCode,
            [ErpClaims.Name] = user.Name,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
        if (user.Email is not null)
        {
            claims[ErpClaims.Email] = user.Email;
        }

        var accessToken = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        });

        var refreshToken = $"{tenantId:N}.{Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32))}";
        db.RefreshTokens.Add(new RefreshToken(user.Id, Hash(refreshToken), now.AddDays(jwt.RefreshTokenDays)));
        return new IssuedTokens(accessToken, refreshToken, expires);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static Guid? ReadTenantId(string? refreshToken)
    {
        var prefix = refreshToken?.Split('.', 2)[0];
        return Guid.TryParseExact(prefix, "N", out var tenantId) ? tenantId : null;
    }
}
