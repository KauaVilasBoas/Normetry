using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

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
        if (await IsLastOwnerAsync(profile, request.UserId, cancellationToken))
        {
            return IdentityErrors.LastOwner;
        }

        await users.UnassignAsync(request.UserId, request.ProfileId, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// The profile is Owner and <paramref name="userId"/> is its only holder, so removing it would
    /// leave the tenant without one.
    /// </summary>
    private async Task<bool> IsLastOwnerAsync(
        Profile profile,
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        if (!profile.GrantsAll)
        {
            return false;
        }
        // Owner removals run one at a time, and the holders are read after the lock: two Owners
        // removing each other cannot both succeed.
        await profiles.LockAsync(profile.Id, cancellationToken);
        return await users.ListHoldersAsync(profile.Id, cancellationToken) is [var only]
            && only == userId;
    }
}
