using Normetry.Application.Abstractions;

namespace Normetry.Application.Identity;

/// <summary>The one place a handler finds the caller's <c>core.users</c> id.</summary>
internal static class CallerLookup
{
    /// <summary>The caller's user id. They hold a permission, so their user row exists.</summary>
    public static async Task<Guid> CallerIdAsync(
        this IUserRepository users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken
    ) =>
        await users.FindIdAsync(currentUser.Sub, cancellationToken)
        ?? throw new InvalidOperationException("The caller has no user row.");
}
