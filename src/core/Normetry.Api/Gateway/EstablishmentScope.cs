using System.Globalization;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Usage;
using Normetry.Application.Usage.CountChatMessage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Establishments;

namespace Normetry.Api.Gateway;

/// <summary>
/// The establishment of a request on <c>/ai/establishments/{id}/...</c>, loaded under RLS before
/// anything is forwarded. The gateway turns it into <c>X-Normetry-Establishment-Id</c> and
/// <c>X-Normetry-Jurisdiction</c>.
/// </summary>
internal sealed record EstablishmentScope(Guid EstablishmentId, Jurisdiction Jurisdiction)
{
    public const string RouteId = "ai-establishment";

    /// <summary>The path after <c>/ai/establishments/</c>, starting with the establishment id.</summary>
    public const string RouteValue = "rest";

    /// <summary>
    /// Proxy pipeline step for the establishment route. A path the AI service could read
    /// differently (an encoded slash or percent, an empty segment) and an establishment id that is
    /// malformed, unknown or another tenant's (RLS hides it) get 404. A chat message is then counted
    /// against the daily quota: over the limit the answer is 429 with <c>Retry-After</c>. Either
    /// way nothing reaches the AI service. Other routes pass through untouched.
    /// </summary>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetReverseProxyFeature().Route.Config.RouteId != RouteId)
        {
            await next(context);
            return;
        }

        var rest = context.Request.RouteValues[RouteValue] as string ?? "";
        var segments = rest.Split('/');
        if (
            !IsPlainPath(rest, segments)
            // Only the canonical lower-case form, so the forwarded path holds exactly the header
            // value.
            || !Guid.TryParseExact(segments[0], "D", out var id)
            || segments[0] != id.ToString()
        )
        {
            await Results.Problem(statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
            return;
        }

        var dispatcher = context.RequestServices.GetRequiredService<IDispatcher>();
        var establishment = await dispatcher.QueryAsync(
            new GetEstablishmentQuery(id),
            context.RequestAborted
        );
        if (!establishment.IsSuccess)
        {
            await establishment.ToProblemDetails().ExecuteAsync(context);
            return;
        }

        if (
            IsChatMessage(context.Request.Method, segments)
            && !await CountAsync(context, dispatcher)
        )
        {
            return;
        }

        context.Features.Set(new EstablishmentScope(id, establishment.Value.Jurisdiction));
        await next(context);
    }

    // Kestrel decodes the path except %2F, so a '%' left in it comes from %2F or %25: the AI
    // service may decode it into a slash and route a path the core did not see, such as a message
    // the quota never counted. An empty segment (//) is refused for the same reason; one trailing
    // slash is fine.
    private static bool IsPlainPath(string rest, string[] segments) =>
        !rest.Contains('%') && !segments[..^1].Contains("");

    // POST {id}/conversations/{conversationId}/messages, in any case and with a trailing slash
    // too: counting a spelling the AI service rejects costs the caller one message, while missing
    // one it accepts would let messages past the quota.
    private static bool IsChatMessage(string method, string[] segments) =>
        HttpMethods.IsPost(method)
        && segments is [_, var conversations, { Length: > 0 }, var messages, .. var rest]
        && rest is [] or [""]
        && conversations.Equals("conversations", StringComparison.OrdinalIgnoreCase)
        && messages.Equals("messages", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Counts the message (committed before forwarding, so a failed upstream still counts). Writes
    /// the 429 and returns false over the limit.
    /// </summary>
    private static async Task<bool> CountAsync(HttpContext context, IDispatcher dispatcher)
    {
        var quota = await dispatcher.SendAsync(
            new CountChatMessageCommand(),
            context.RequestAborted
        );
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!quota.IsSuccess)
        {
            if (quota.Error.Type == ErrorType.RateLimited)
            {
                context.Response.Headers.RetryAfter = Seconds(
                    ChatQuotaResponse.NextReset(now) - now
                );
            }
            await quota.ToProblemDetails().ExecuteAsync(context);
            return false;
        }

        // An unlimited plan has nothing to report.
        if (quota.Value is { Limit: { } limit, Remaining: { } remaining, ResetsAt: var resetsAt })
        {
            var reset = Seconds(resetsAt - now);
            // Set as the response starts, after YARP copied the AI service's headers: the core's
            // values win, nothing is buffered (the answer streams as SSE), and an error response
            // from the proxy gets them too.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["RateLimit-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
                headers["RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
                headers["RateLimit-Reset"] = reset;
                return Task.CompletedTask;
            });
        }
        return true;
    }

    private static string Seconds(TimeSpan span) =>
        ((long)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
