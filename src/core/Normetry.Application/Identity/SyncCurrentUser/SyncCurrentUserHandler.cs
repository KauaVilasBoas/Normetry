using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.SyncCurrentUser;

internal sealed class SyncCurrentUserHandler(IUserRepository users, ICurrentUser currentUser)
    : ICommandHandler<SyncCurrentUserCommand>
{
    public async Task<Result> HandleAsync(
        SyncCurrentUserCommand request,
        CancellationToken cancellationToken
    )
    {
        await users.SyncAsync(
            currentUser.Sub,
            currentUser.DisplayName,
            currentUser.Email,
            cancellationToken
        );
        return Result.Success();
    }
}
