using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Establishments.CreateEstablishment;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Establishments.ListEstablishments;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Establishments;

[ApiController]
[Route("establishments")]
public sealed class EstablishmentsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The tenant comes from the token only; the body carries none.</summary>
    [HttpPost]
    [HasPermission(Permissions.Establishments.Manage)]
    public async Task<IActionResult> Create(
        CreateEstablishmentCommand command,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(command, cancellationToken);
        return result.IsSuccess
            ? Created(Url.Action(nameof(Get), new { id = result.Value }), new { id = result.Value })
            : result.ToActionResult();
    }

    [HttpGet]
    [HasPermission(Permissions.Establishments.Read)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListEstablishmentsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    /// <summary>Another tenant's id answers 404 like an unknown one: RLS hides the row.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Establishments.Read)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetEstablishmentQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
