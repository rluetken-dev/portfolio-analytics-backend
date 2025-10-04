using System.Security.Claims;

namespace Portfolio.Api.Extensions;

/// <summary>
/// Helper extensions for working with authenticated users.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns true if the current user has an admin claim.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.HasClaim(c => c.Type == "isAdmin" && c.Value == "true");
    }

    /// <summary>
    /// Returns the current user's ID as an int, or null if not authenticated.
    /// </summary>
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    /// <summary>
    /// Returns the current user's username, or null if not authenticated.
    /// </summary>
    public static string? GetUsername(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value;
    }
}
