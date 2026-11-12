using Normetry.Api.Auth;
using Normetry.Application.Usage;
using Normetry.Application.Usage.GetChatQuota;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api.Chat;

internal static class ChatEndpoints
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

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // What the caller has left today, so the web app can show it before sending.
        app.MapGet(
                "/chat/quota",
                async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.QueryAsync(
                        new GetChatQuotaQuery(),
                        cancellationToken
                    );
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblemDetails();
                }
            )
            .RequireAuthorization(Policies.Tenant);
        return app;
    }
}
