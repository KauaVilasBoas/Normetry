using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Normetry.IntegrationTests.Gateway;

/// <summary>The chat plans are validated at startup: a bad setting stops the API.</summary>
public sealed class ChatConfigurationTests
{
    [Theory]
    [InlineData("Chat:DefaultPlan", "gold", "'gold'")]
    [InlineData("Chat:TenantPlans:33333333-3333-4333-8333-333333333333", "gold", "'gold'")]
    [InlineData("Chat:TenantPlans:demo-bistro", "standard", "Chat:TenantPlans:demo-bistro")]
    [InlineData("Chat:Plans:demo:DailyMessagesPerUser", "0", "Chat:Plans:demo")]
    public void The_API_refuses_to_start_on_an_invalid_chat_setting(
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
