using Normetry.Application.Usage;

namespace Normetry.Api.Chat;

internal static class ChatSetup
{
    /// <summary>
    /// The chat plans from the <c>Chat</c> section, validated now: the API refuses to start on an
    /// unknown plan name, a tenant key that is not a UUID or a limit below 1. Also the clock the
    /// daily counts use.
    /// </summary>
    public static WebApplicationBuilder AddChat(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(ChatOptions.Section).Get<ChatOptions>()
            ?? new ChatOptions();
        options.Validate();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        return builder;
    }
}
