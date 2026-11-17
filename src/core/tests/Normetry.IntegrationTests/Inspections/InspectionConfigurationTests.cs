using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Inspections;
using Xunit;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>The inspection settings are validated at startup: a bad one stops the API.</summary>
public sealed class InspectionConfigurationTests
{
    [Fact]
    public void The_shipped_settings_give_each_plan_its_quota()
    {
        using var factory = new ApiFactory();

        var options = factory.Services.GetRequiredService<InspectionsOptions>();

        Assert.Equal(
            (5, 20, (int?)null, 5, 300_000, TimeSpan.FromMinutes(30)),
            (
                options.DailyQuotaOf("demo"),
                options.DailyQuotaOf("standard"),
                options.DailyQuotaOf("unlimited"),
                options.DailyQuotaOf("a plan not listed"),
                options.TokenCeiling,
                options.RunningTimeout
            )
        );
    }

    [Theory]
    [InlineData("Inspections:DailyQuota:gold", "3", "Inspections:DailyQuota:gold")]
    [InlineData("Inspections:DailyQuota:demo", "0", "Inspections:DailyQuota:demo")]
    [InlineData("Inspections:TokenCeiling", "0", "Inspections:TokenCeiling")]
    [InlineData("Inspections:RunningTimeout", "00:00:00", "Inspections:RunningTimeout")]
    public void The_API_refuses_to_start_on_an_invalid_inspection_setting(
        string key,
        string value,
        string expected
    )
    {
        using var factory = new ApiFactory().WithWebHostBuilder(host =>
            host.UseSetting(key, value)
        );

        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains(expected, error.Message);
    }
}
