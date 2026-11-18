using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Normetry.IntegrationTests.RateLimiting;

/// <summary>The rate limits are validated at startup: a bad one stops the API.</summary>
public sealed class RateLimitingConfigurationTests
{
    [Theory]
    [InlineData("GlobalPerMinute")]
    [InlineData("UploadsPerMinute")]
    [InlineData("InspectionStartsPerMinute")]
    [InlineData("ChatPerMinute")]
    public void The_API_refuses_to_start_on_a_limit_below_1(string limit)
    {
        using var factory = new ApiFactory().WithWebHostBuilder(host =>
            host.UseSetting($"RateLimiting:{limit}", "0")
        );

        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains($"RateLimiting:{limit}", error.Message);
    }
}
