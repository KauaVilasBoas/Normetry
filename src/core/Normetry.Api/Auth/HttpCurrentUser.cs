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

    public string Sub =>
        User.TryGetSubject(out var subject)
            ? subject
            : throw new InvalidOperationException("The token has no sub claim.");

    public string? DisplayName =>
        User.FindFirstValue(JwtClaimNames.Name)
        ?? User.FindFirstValue(JwtClaimNames.PreferredUsername);

    public string? Email => User.FindFirstValue(JwtClaimNames.Email);

    public Guid TenantId =>
        User.TryGetTenantId(out var tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                $"The request has no valid tenant: the endpoint must require a policy that includes {Policies.Tenant}."
            );
}
