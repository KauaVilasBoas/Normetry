using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Overview.GetOverview;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

namespace Normetry.Api.Overview;

/// <summary>
/// An establishment's overview: the latest completed inspection's results, its open actions and
/// pending reviews, compliance across inspections, and the documents expired or expiring soon.
/// Another tenant's establishment answers 404: RLS hides the row.
/// </summary>
[ApiController]
public sealed class OverviewController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>
    /// Open to <c>inspections.read</c> or <c>documents.read</c>; each section appears only with its
    /// own. <c>Ok</c> writes the value's runtime type, which carries the inspection sections only
    /// for a caller allowed to see them.
    /// </summary>
    [HttpGet("establishments/{establishmentId:guid}/overview")]
    [HasPermission(Permissions.Inspections.Read, Permissions.Documents.Read)]
    public async Task<IActionResult> Get(Guid establishmentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new GetOverviewQuery(establishmentId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
