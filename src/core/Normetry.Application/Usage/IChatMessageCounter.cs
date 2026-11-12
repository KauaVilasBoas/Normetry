namespace Normetry.Application.Usage;

/// <summary>
/// Daily chat message counts of the current tenant. Row-level security scopes every call to the
/// tenant of the request's transaction. Changes are saved when that transaction commits.
/// </summary>
public interface IChatMessageCounter
{
    /// <summary>
    /// Adds one message for the user and day unless the count already reached
    /// <paramref name="limit"/> (<c>null</c>: no limit). Atomic, so concurrent messages never
    /// exceed the limit or lose a count. Returns the new count, or <c>null</c> when nothing was
    /// added.
    /// </summary>
    Task<int?> TryIncrementAsync(
        Guid tenantId,
        string userId,
        DateOnly day,
        int? limit,
        CancellationToken cancellationToken
    );

    /// <summary>The user's count for the day, 0 when there is none.</summary>
    Task<int> GetAsync(string userId, DateOnly day, CancellationToken cancellationToken);
}
