using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.GetCurrentUser;

/// <summary>Who the caller is, as the API sees it after validating the token.</summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;

/// <summary>
/// <see cref="TenantName"/> comes from the caller's <c>core.tenants</c> row; it is <c>null</c> for
/// a tenant the identity provider knows but the core has no row for yet.
/// </summary>
public sealed record CurrentUserResponse(
    string UserId,
    Guid TenantId,
    string? TenantName,
    IReadOnlyList<string> Roles
);
