using Normetry.Application.Usage;

namespace Normetry.Api.Chat;

internal static class ChatSetup
{
    /// <summary>
    /// The chat plans from the <c>Chat</c> section, validated now: the API refuses to start on an
    /// unknown plan name, a tenant key that is not a UUID or a limit below 1. Returned for
    /// <c>AddInspections</c>, whose quotas use the same plans. Also the clock the daily counts use.
    /// </summary>
    public static ChatOptions AddChat(this WebApplicationBuilder builder)
    {
        var chatPlans = builder.BindValidated<ChatOptions>(ChatOptions.Section, options =>
            options.Validate()
        );
        builder.Services.AddSingleton(TimeProvider.System);
        return chatPlans;
    }
}
