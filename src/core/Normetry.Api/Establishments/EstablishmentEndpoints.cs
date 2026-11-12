using Normetry.Api.Auth;
using Normetry.Application.Establishments.CreateEstablishment;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Establishments.ListEstablishments;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api.Establishments;

internal static class EstablishmentEndpoints
{
    private const string Route = "/establishments";

    public static IEndpointRouteBuilder MapEstablishmentEndpoints(this IEndpointRouteBuilder app)
    {
        // The tenant comes from the token only; the body carries none.
        app.MapPost(
                Route,
                async (
                    CreateEstablishmentCommand command,
                    IDispatcher dispatcher,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await dispatcher.SendAsync(command, cancellationToken);
                    return result.IsSuccess
                        ? Results.Created($"{Route}/{result.Value}", new { id = result.Value })
                        : result.ToProblemDetails();
                }
            )
            .RequireAuthorization(Policies.OwnerOrManager);

        app.MapGet(Route, async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.QueryAsync(
                    new ListEstablishmentsQuery(),
                    cancellationToken
                );
                return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblemDetails();
            })
            .RequireAuthorization(Policies.Tenant);

        // Another tenant's id answers 404 like an unknown one: RLS hides the row.
        app.MapGet(
                $"{Route}/{{id:guid}}",
                async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.QueryAsync(
                        new GetEstablishmentQuery(id),
                        cancellationToken
                    );
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblemDetails();
                }
            )
            .RequireAuthorization(Policies.Tenant);

        return app;
    }
}
