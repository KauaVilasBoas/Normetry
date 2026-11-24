namespace Normetry.Application.Usage;

/// <summary>
/// Daily inspection start counts of the current tenant. Row-level security scopes every call to
/// the tenant of the request's transaction. Changes are saved when that transaction commits.
/// </summary>
public interface IInspectionStartCounter
{
    /// <summary>
    /// Counts one start on <paramref name="day"/> unless the count already reached
    /// <paramref name="limit"/> (<c>null</c>: no limit). Atomic, so concurrent starts never pass
    /// the limit; part of the transaction, so a start refused later uncounts itself. Returns the
    /// new count, or <c>null</c> when nothing was counted.
    /// </summary>
    Task<int?> TryCountStartAsync(DateOnly day, int? limit, CancellationToken cancellationToken);
}
