using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GameApi.Tests.TestSupport;

/// <summary>
/// Generates JWTs shaped like AlteredAuth (Keycloak) access tokens, signed
/// with a well-known test key -- see GameApiFactory's Keycloak:TestSigningKey,
/// which makes Program.cs validate against this same key instead of fetching
/// JWKS from a live Keycloak.
/// </summary>
public static class JwtTokenHelper
{
    public const string TestSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256-1234567890";
    public const string GameHistoryScope = "bga-game-history";

    private static readonly SigningCredentials SigningCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
        SecurityAlgorithms.HmacSha256);

    /// <summary>Generates a JWT with the given space-delimited "scope" claim (null omits the claim entirely).</summary>
    public static string GenerateToken(string? scope = GameHistoryScope, DateTimeOffset? expires = null)
    {
        var claims = new List<Claim>();
        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            expires: (expires ?? DateTimeOffset.UtcNow.AddHours(1)).UtcDateTime,
            signingCredentials: SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
