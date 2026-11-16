using Normetry.Domain.Identity;

namespace Normetry.Application.Identity;

/// <summary>
/// Profiles of the current tenant and the global permission catalog. Row-level security scopes
/// every profile to the tenant of the request's transaction. Changes are saved when the request's
/// transaction commits.
/// </summary>
public interface IProfileRepository
{
    void Add(Profile profile);

    void Remove(Profile profile);

    /// <summary>Tracked, with its permissions.</summary>
    Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<ProfileListItem>> ListAsync(CancellationToken cancellationToken);

    Task<bool> NameTakenAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the profile's row until the transaction ends, so changes to it (its permissions, who
    /// holds Owner) run one at a time.
    /// </summary>
    Task LockAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every permission name in <c>core.permissions</c>, ordered.</summary>
    Task<IReadOnlyList<string>> ListCatalogAsync(CancellationToken cancellationToken);
}
