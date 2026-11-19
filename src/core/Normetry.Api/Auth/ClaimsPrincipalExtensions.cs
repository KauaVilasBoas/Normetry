using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Normetry.Api.Auth;

/// <summary>
/// The caller checks every part of the API shares: the policies (<c>AuthSetup</c>), the permission
/// handler, the gateway, the rate limiter and <see cref="HttpCurrentUser"/>. They read only the
/// validated principal, never the body, query string or headers.
/// </summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// A non-empty <c>sub</c> claim. <c>RequireClaim</c> would accept an empty value, and an empty
    /// one identifies nobody.
    /// </summary>
    public static bool TryGetSubject(
        this ClaimsPrincipal user,
        [NotNullWhen(true)] out string? subject
    )
    {
        subject = user.FindFirstValue("sub") is { Length: > 0 } value ? value : null;
        return subject is not null;
    }

    /// <summary>Exactly one <c>tenant_id</c> claim holding a non-empty UUID in 8-4-4-4-12 form.</summary>
    public static bool TryGetTenantId(this ClaimsPrincipal user, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return user.FindAll("tenant_id").ToArray() is [{ Value: { Length: 36 } value }]
            && Guid.TryParseExact(value, "D", out tenantId)
            && tenantId != Guid.Empty;
    }

    /// <summary>
    /// A caller the API can act for: a valid tenant (<see cref="TryGetTenantId"/>) and a subject
    /// (<see cref="TryGetSubject"/>). The <see cref="Policies.Tenant"/> policy guarantees both.
    /// </summary>
    public static bool TryGetCaller(
        this ClaimsPrincipal user,
        out Guid tenantId,
        [NotNullWhen(true)] out string? subject
    )
    {
        subject = null;
        return user.TryGetTenantId(out tenantId) && user.TryGetSubject(out subject);
    }
}
