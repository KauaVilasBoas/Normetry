using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments;

/// <summary>
/// Establishments of the current tenant. Row-level security scopes every call to the tenant of
/// the request's transaction, so there is no tenant parameter: another tenant's rows do not exist
/// here. Changes are saved when the request's transaction commits.
/// </summary>
public interface IEstablishmentRepository
{
    void Add(Establishment establishment);

    Task<Establishment?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<Establishment>> ListAsync(CancellationToken cancellationToken);
}
