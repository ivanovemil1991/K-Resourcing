using System.Security.Claims;

namespace Claims.Api;

public static class AuthorizationPolicies
{
    public const string Read = "ClaimsRead";
    public const string Write = "ClaimsWrite";

    public static bool HasPermission(ClaimsPrincipal user, bool write)
    {
        var roles = user.FindAll("roles").Select(claim => claim.Value);
        var scopes = user.FindAll("scp").SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return roles.Contains("Claims.Write") || scopes.Contains("claims.write") ||
               (!write && (roles.Contains("Claims.Read") || scopes.Contains("claims.read")));
    }
}
