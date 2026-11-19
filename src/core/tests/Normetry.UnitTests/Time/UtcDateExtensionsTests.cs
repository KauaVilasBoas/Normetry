using System.Globalization;
using Normetry.BuildingBlocks.Time;
using Xunit;

namespace Normetry.UnitTests.Time;

public sealed class UtcDateExtensionsTests
{
    // Near midnight the local day and the UTC day differ: 01:30 at +03:00 is the day before in
    // UTC, and 22:30 in São Paulo (-03:00) is already the next day.
    [Theory]
    [InlineData("2026-03-01T01:30:00+03:00", "2026-02-28")]
    [InlineData("2026-03-01T22:30:00-03:00", "2026-03-02")]
    [InlineData("2026-03-01T00:00:00+00:00", "2026-03-01")]
    public void The_date_is_the_UTC_day_of_the_instant(string instant, string utcDay) =>
        Assert.Equal(
            DateOnly.Parse(utcDay, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).UtcDate()
        );

    [Fact]
    public void Today_is_the_clock_s_UTC_day() =>
        Assert.Equal(
            new DateOnly(2026, 2, 28),
            new FixedClock(
                DateTimeOffset.Parse("2026-03-01T01:30:00+03:00", CultureInfo.InvariantCulture)
            ).UtcToday()
        );

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
