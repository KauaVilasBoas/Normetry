using Normetry.BuildingBlocks.Time;

namespace Normetry.Application.Usage;

/// <summary>
/// The caller's chat quota for the current UTC day. <see cref="Limit"/> and
/// <see cref="Remaining"/> are <c>null</c> when the tenant's plan has no limit.
/// </summary>
public sealed record ChatQuotaResponse(int? Limit, int? Remaining, DateTimeOffset ResetsAt)
{
    internal static ChatQuotaResponse Create(int? limit, int used, DateTimeOffset now) =>
        new(limit, limit is { } max ? Math.Max(0, max - used) : null, now.NextUtcMidnight());
}
