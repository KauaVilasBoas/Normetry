using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.ListUsers;

internal sealed class ListUsersHandler(IUserRepository users, ICurrentUser currentUser)
    : IQueryHandler<ListUsersQuery, IReadOnlyList<UserResponse>>
{
    public async Task<Result<IReadOnlyList<UserResponse>>> HandleAsync(
        ListUsersQuery request,
        CancellationToken cancellationToken
    ) =>
        Result<IReadOnlyList<UserResponse>>.Success(
            await users.ListAsync(currentUser.UserId, cancellationToken)
        );
}
