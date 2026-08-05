using System.Security.Claims;

namespace Portfolio.Api.Extensions;

/// <summary>
/// Provides helpers for reading authenticated user claims.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.HasClaim(claim =>
            claim.Type == "isAdmin" &&
            string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase));
    }

    public static int? GetUserId(this ClaimsPrincipal user)
    {
        string? idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(idClaim, out int id) ? id : null;
    }

    public static string? GetUsername(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value;
    }
}