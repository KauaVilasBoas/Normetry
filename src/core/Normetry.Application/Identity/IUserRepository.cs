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
}
