using Normetry.Application.Establishments.GetEstablishment;
using Normetry.BuildingBlocks.Cqrs;
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

    /// <summary>The path after <c>/ai/</c> on the catch-all route.</summary>
    public const string CatchAllRouteValue = "catch-all";

    /// <summary>The first segment after <c>/ai/</c> of every establishment path.</summary>
    public const string EstablishmentsSegment = "establishments";

    private const string ConversationsSegment = "conversations";

    private const string MessagesSegment = "messages";

    private const char SegmentSeparator = '/';

    // Kestrel decodes the path except %2F, so a '%' left in it comes from %2F or %25.
    private const char UndecodedEscape = '%';

    /// <summary>
    /// Proxy pipeline step for the establishment route. A path the AI service could read
    /// differently (an encoded slash or percent, an empty segment) and an establishment id that is
    /// malformed, unknown or another tenant's (RLS hides it) get 404. A chat message is then counted
    /// against the daily quota: over the limit the answer is 429 with <c>Retry-After</c>. Either
    /// way nothing reaches the AI service. On the catch-all route, an establishment path in
    /// disguise gets 404; anything else passes through untouched.
    /// </summary>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetReverseProxyFeature().Route.Config.RouteId != RouteId)
        {
            await RefuseDisguisedEstablishmentPathAsync(context, next);
            return;
        }

        if (!TryReadEstablishmentId(context, out var establishmentId, out var segments))
        {
            await NotFoundAsync(context);
            return;
        }

        if (await LoadEstablishmentAsync(context, establishmentId) is not { } establishment)
        {
            return;
        }

        if (
            IsChatMessage(context.Request.Method, segments)
            && !await ChatMessageQuota.CountAsync(context)
        )
        {
            return;
        }

        context.Features.Set(establishment);
        await next(context);
    }

    /// <summary>
    /// Establishment paths are forwarded only by the establishment route, with its checks, count
    /// and headers. Through the catch-all (<c>/ai//establishments/...</c>, or a slash hidden as
    /// <c>%2F</c> or <c>%252F</c>) the AI service could still route them, so they get 404.
    /// Anything else goes on.
    /// </summary>
    private static Task RefuseDisguisedEstablishmentPathAsync(
        HttpContext context,
        RequestDelegate next
    )
    {
        var path = context.Request.RouteValues[CatchAllRouteValue] as string ?? "";
        var disguised =
            path.Contains(UndecodedEscape)
            || path.Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries)
                is [var first, ..]
                && first.Equals(EstablishmentsSegment, StringComparison.OrdinalIgnoreCase);
        return disguised ? NotFoundAsync(context) : next(context);
    }

    /// <summary>
    /// The establishment id the route's path starts with, on a plain path only, and in the
    /// canonical lower-case form only, so the forwarded path holds exactly the header value.
    /// <paramref name="segments"/> is that path split at each slash.
    /// </summary>
    private static bool TryReadEstablishmentId(
        HttpContext context,
        out Guid establishmentId,
        out string[] segments
    )
    {
        var rest = context.Request.RouteValues[RouteValue] as string ?? "";
        segments = rest.Split(SegmentSeparator);
        establishmentId = Guid.Empty;
        return IsPlainPath(rest, segments)
            && Guid.TryParseExact(segments[0], "D", out establishmentId)
            && segments[0] == establishmentId.ToString();
    }

    // A '%' left in the path may be decoded by the AI service into a slash, routing a path the
    // core did not see, such as a message the quota never counted. An empty segment (//) is
    // refused for the same reason; one trailing slash is fine.
    private static bool IsPlainPath(string rest, string[] segments) =>
        !rest.Contains(UndecodedEscape) && !segments[..^1].Contains("");

    /// <summary>
    /// Loads the establishment under the caller's tenant (RLS). A failure, such as an unknown or
    /// another tenant's id (404), writes its problem and returns null.
    /// </summary>
    private static async Task<EstablishmentScope?> LoadEstablishmentAsync(
        HttpContext context,
        Guid establishmentId
    )
    {
        var dispatcher = context.RequestServices.GetRequiredService<IDispatcher>();
        var establishment = await dispatcher.QueryAsync(
            new GetEstablishmentQuery(establishmentId),
            context.RequestAborted
        );
        if (!establishment.IsSuccess)
        {
            await establishment.ToProblemDetails().ExecuteAsync(context);
            return null;
        }
        return new EstablishmentScope(establishmentId, establishment.Value.Jurisdiction);
    }

    // POST {id}/conversations/{conversationId}/messages, in any case and with a trailing slash
    // too: counting a spelling the AI service rejects costs the caller one message, while missing
    // one it accepts would let messages past the quota.
    private static bool IsChatMessage(string method, string[] segments) =>
        HttpMethods.IsPost(method)
        && segments is [_, var conversations, { Length: > 0 }, var messages, .. var rest]
        && rest is [] or [""]
        && conversations.Equals(ConversationsSegment, StringComparison.OrdinalIgnoreCase)
        && messages.Equals(MessagesSegment, StringComparison.OrdinalIgnoreCase);

    private static Task NotFoundAsync(HttpContext context) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
}
