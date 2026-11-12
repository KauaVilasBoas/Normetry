namespace Normetry.Application.Usage;

/// <summary>
/// The caller's chat quota for the current UTC day. <see cref="Limit"/> and
/// <see cref="Remaining"/> are <c>null</c> when the tenant's plan has no limit.
/// </summary>
public sealed record ChatQuotaResponse(int? Limit, int? Remaining, DateTimeOffset ResetsAt)
{
    /// <summary>The UTC day a message sent at <paramref name="now"/> counts in.</summary>
    internal static DateOnly Day(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    /// <summary>The next UTC midnight, when every count starts again.</summary>
    public static DateTimeOffset NextReset(DateTimeOffset now) =>
        new(Day(now).AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    internal static ChatQuotaResponse Create(int? limit, int used, DateTimeOffset now) =>
        new(limit, limit is { } max ? Math.Max(0, max - used) : null, NextReset(now));
}
