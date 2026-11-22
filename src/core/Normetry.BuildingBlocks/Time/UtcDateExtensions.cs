namespace Normetry.BuildingBlocks.Time;

/// <summary>
/// The UTC calendar day, the one the daily counts, due dates and expiry dates use. Taking the
/// day of the instant's own offset instead would move it near midnight.
/// </summary>
public static class UtcDateExtensions
{
    public static DateOnly UtcDate(this DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.UtcDateTime);

    public static DateOnly UtcToday(this TimeProvider clock) => clock.GetUtcNow().UtcDate();

    /// <summary>The UTC midnight after the instant, when every daily count starts again.</summary>
    public static DateTimeOffset NextUtcMidnight(this DateTimeOffset instant) =>
        new(instant.UtcDate().AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
