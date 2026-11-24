using System.Globalization;
using Normetry.Application.Usage;
using Normetry.Application.Usage.CountChatMessage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.BuildingBlocks.Time;

namespace Normetry.Api.Gateway;

/// <summary>
/// The daily chat quota on the chat message route, which <see cref="EstablishmentScope"/> picks
/// out. A message is counted before it is forwarded, and the response reports what is left in
/// the <c>RateLimit-*</c> headers.
/// </summary>
internal static class ChatMessageQuota
{
    private const string LimitHeader = "RateLimit-Limit";

    private const string RemainingHeader = "RateLimit-Remaining";

    private const string ResetHeader = "RateLimit-Reset";

    /// <summary>
    /// Counts the message (committed before forwarding, so a failed upstream still counts). Writes
    /// the 429 and returns false over the limit.
    /// </summary>
    public static async Task<bool> CountAsync(HttpContext context)
    {
        var dispatcher = context.RequestServices.GetRequiredService<IDispatcher>();
        var quota = await dispatcher.SendAsync(
            new CountChatMessageCommand(),
            context.RequestAborted
        );
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!quota.IsSuccess)
        {
            await WriteRefusalAsync(context, quota, now);
            return false;
        }

        SetRateLimitHeadersWhenResponseStarts(context.Response, quota.Value, now);
        return true;
    }

    /// <summary>Over the limit, <c>Retry-After</c> counts down to the next UTC midnight.</summary>
    private static Task WriteRefusalAsync(HttpContext context, Result refusal, DateTimeOffset now)
    {
        if (refusal.Error.Type == ErrorType.RateLimited)
        {
            context.Response.SetRetryAfter(now.NextUtcMidnight() - now);
        }
        return refusal.ToProblemDetails().ExecuteAsync(context);
    }

    /// <summary>
    /// Set as the response starts, after YARP copied the AI service's headers: the core's values
    /// win, nothing is buffered (the answer streams as SSE), and an error response from the proxy
    /// gets them too. An unlimited plan has nothing to report.
    /// </summary>
    private static void SetRateLimitHeadersWhenResponseStarts(
        HttpResponse response,
        ChatQuotaResponse quota,
        DateTimeOffset now
    )
    {
        if (quota is not { Limit: { } limit, Remaining: { } remaining, ResetsAt: var resetsAt })
        {
            return;
        }

        var reset = HttpResponseExtensions.WholeSeconds(resetsAt - now);
        response.OnStarting(() =>
        {
            var headers = response.Headers;
            headers[LimitHeader] = limit.ToString(CultureInfo.InvariantCulture);
            headers[RemainingHeader] = remaining.ToString(CultureInfo.InvariantCulture);
            headers[ResetHeader] = reset;
            return Task.CompletedTask;
        });
    }
}
