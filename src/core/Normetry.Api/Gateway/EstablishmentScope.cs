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

    /// <summary>
    /// Proxy pipeline step for the establishment route. An establishment id that is malformed,
    /// unknown or another tenant's (RLS hides it) gets 404 and nothing reaches the AI service.
    /// Other routes pass through untouched.
    /// </summary>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetReverseProxyFeature().Route.Config.RouteId != RouteId)
        {
            await next(context);
            return;
        }

        var segments = (context.Request.RouteValues[RouteValue] as string ?? "").Split('/');
        // Only the canonical lower-case form, so the forwarded path holds exactly the header value.
        if (!Guid.TryParseExact(segments[0], "D", out var id) || segments[0] != id.ToString())
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

        context.Features.Set(new EstablishmentScope(id, establishment.Value.Jurisdiction));
        await next(context);
    }
}
