using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Identity.GetCurrentUser;

/// <summary>Who the caller is, as the API sees it after validating the token.</summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>, INoTransaction;

public sealed record CurrentUserResponse(string UserId, Guid TenantId, IReadOnlyList<string> Roles);
