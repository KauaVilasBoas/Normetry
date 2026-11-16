using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.UnassignProfile;

internal sealed class UnassignProfileHandler(IUserRepository users, IProfileRepository profiles)
    : ICommandHandler<UnassignProfileCommand>
{
    public async Task<Result> HandleAsync(
        UnassignProfileCommand request,
        CancellationToken cancellationToken
    )
    {
        if (!await users.ExistsAsync(request.UserId, cancellationToken))
        {
            return IdentityErrors.UserNotFound;
        }
        if (await profiles.GetAsync(request.ProfileId, cancellationToken) is not { } profile)
        {
            return IdentityErrors.ProfileNotFound;
        }

        if (profile.GrantsAll)
        {
            // Owner removals run one at a time, and the holders are read after the lock: two
            // Owners removing each other cannot both succeed.
            await profiles.LockAsync(profile.Id, cancellationToken);
            if (
                await users.ListHoldersAsync(profile.Id, cancellationToken) is [var only]
                && only == request.UserId
            )
            {
                return IdentityErrors.LastOwner;
            }
        }

        await users.UnassignAsync(request.UserId, request.ProfileId, cancellationToken);
        return Result.Success();
    }
}
