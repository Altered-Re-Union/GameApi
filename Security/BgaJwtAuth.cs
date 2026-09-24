using System.Security.Claims;

namespace GameApi.Security;

/// <summary>
/// Scope checks against an already-authenticated ClaimsPrincipal (see
/// Program.cs's JwtBearer wiring -- token validation itself is the standard
/// ASP.NET Core middleware's job, this is only the authorization half).
/// </summary>
public static class BgaJwtAuth
{
    /// <summary>
    /// Checks whether the authenticated principal holds the required OAuth
    /// scope. Standard OAuth2 scope claim names are checked: "scope"
    /// (space-delimited) and "scp" (array or space-delimited) -- AlteredAuth
    /// (Keycloak) uses "scope".
    /// </summary>
    public static bool HasScope(ClaimsPrincipal principal, string requiredScope)
    {
        if (string.IsNullOrEmpty(requiredScope))
        {
            return false;
        }

        // Standard "scope" claim (space-delimited string).
        var scopeClaim = principal.FindFirst("scope")?.Value;
        if (scopeClaim is not null && scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // "scp" claim -- may be an array or space-delimited string.
        var scpClaims = principal.FindAll("scp");
        foreach (var claim in scpClaims)
        {
            if (claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
