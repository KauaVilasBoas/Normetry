namespace Normetry.Application.Identity;

/// <summary>
/// Users of the current tenant and what their profiles grant. Row-level security scopes every
/// call to the tenant of the request's transaction, so another tenant's users do not exist here.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Creates the user with <paramref name="sub"/> on first sight, or refreshes their display
    /// name and email when the token's differ. Does nothing when the core has no row for
    /// <paramref name="tenantId"/>.
    /// </summary>
    Task SyncAsync(
        Guid tenantId,
        string sub,
        string? displayName,
        string? email,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Every permission the user's profiles grant, ordered by name: the whole catalog for an
    /// Owner. Empty for an unknown user or one with no profiles.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(
        string sub,
        CancellationToken cancellationToken
    );

    /// <summary>The profiles the user holds, ordered by name. Empty for an unknown user.</summary>
    Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(
        string sub,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Every user of the tenant with their profiles, ordered by display name. The one whose
    /// <c>sub</c> is <paramref name="callerSub"/> is marked as the current user.
    /// </summary>
    Task<IReadOnlyList<UserResponse>> ListAsync(
        string callerSub,
        CancellationToken cancellationToken
    );

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The <c>core.users</c> id of the user with <paramref name="sub"/>, or none.</summary>
    Task<Guid?> FindIdAsync(string sub, CancellationToken cancellationToken);

    /// <summary>The display name of each of <paramref name="ids"/> that exists.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetDisplayNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken
    );

    /// <summary>Gives the user the profile; nothing changes when they already hold it.</summary>
    Task AssignAsync(
        Guid tenantId,
        Guid userId,
        Guid profileId,
        CancellationToken cancellationToken
    );

    /// <summary>Takes the profile from the user; nothing changes when they do not hold it.</summary>
    Task UnassignAsync(Guid userId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>The ids of the users who hold the profile.</summary>
    Task<IReadOnlyList<Guid>> ListHoldersAsync(Guid profileId, CancellationToken cancellationToken);
}
