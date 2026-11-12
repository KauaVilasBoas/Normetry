using System.Security.Claims;
using Normetry.Application.Abstractions;

namespace Normetry.Api.Auth;

/// <summary>
/// The caller, read only from the validated token's claims. The body, query string and headers
/// never feed it, so a client cannot choose its tenant.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser, ITenantContext
{
    private ClaimsPrincipal User =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("The request has no authenticated user.");

    public string UserId =>
        User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("The token has no sub claim.");

    public Guid TenantId =>
        TryGetTenantId(User, out var tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                $"The request has no valid tenant: the endpoint must require the {Policies.Tenant} policy."
            );

    public IReadOnlyList<string> Roles => NormetryRoles.All.Where(User.IsInRole).ToArray();

    /// <summary>Exactly one <c>tenant_id</c> claim holding a non-empty UUID in 8-4-4-4-12 form.</summary>
    internal static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return user.FindAll("tenant_id").ToArray() is [{ Value: { Length: 36 } value }]
            && Guid.TryParseExact(value, "D", out tenantId)
            && tenantId != Guid.Empty;
    }
}
