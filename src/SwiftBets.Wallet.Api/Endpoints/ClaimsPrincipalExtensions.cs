using System.Security.Claims;

namespace SwiftBets.Wallet.Api.Endpoints;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in customer; null for a service token, which has no subject.</summary>
    public static Guid? CustomerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
