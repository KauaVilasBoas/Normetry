using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Inspections.GetInspection;
using Normetry.Application.Inspections.ListInspections;
using Normetry.Application.Inspections.StartInspection;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

namespace Normetry.Api.Inspections;

/// <summary>
/// An establishment's simulated inspections. A start answers 202: the AI service runs it
/// afterwards, and its progress reaches the web app over <c>/hubs/inspections</c>. Another
/// tenant's ids answer 404 like unknown ones: RLS hides the rows.
/// </summary>
[ApiController]
[Route("establishments/{establishmentId:guid}/inspections")]
public sealed class InspectionsController(IDispatcher dispatcher, TimeProvider clock)
    : ControllerBase
{
    /// <summary>
    /// Starts one. 409 <c>inspection_running</c> or <c>no_indexed_documents</c>; 429
    /// <c>inspection_quota</c> with <c>Retry-After</c> to the next UTC midnight.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Inspections.Run)]
    public async Task<IActionResult> Start(
        Guid establishmentId,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new StartInspectionCommand(establishmentId),
            cancellationToken
        );
        if (result.IsSuccess)
        {
            return Accepted(
                $"/establishments/{establishmentId}/inspections/{result.Value.Id}",
                result.Value
            );
        }
        if (result.Error.Type == ErrorType.RateLimited)
        {
            var now = clock.GetUtcNow();
            Response.Headers.RetryAfter = (
                (long)Math.Ceiling((ChatQuotaResponse.NextReset(now) - now).TotalSeconds)
            ).ToString(CultureInfo.InvariantCulture);
        }
        return result.ToActionResult();
    }

    /// <summary>Newest first, with their findings counted by verdict.</summary>
    [HttpGet]
    [HasPermission(Permissions.Inspections.Read)]
    public async Task<IActionResult> List(Guid establishmentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new ListInspectionsQuery(establishmentId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    /// <summary>One inspection with its per-theme progress, findings and action items.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inspections.Read)]
    public async Task<IActionResult> Get(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.QueryAsync(
            new GetInspectionQuery(establishmentId, id),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
