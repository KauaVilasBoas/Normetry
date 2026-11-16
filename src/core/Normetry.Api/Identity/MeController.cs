using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Identity.GetCurrentUser;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api.Identity;

[ApiController]
[Route("me")]
public sealed class MeController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.Tenant)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetCurrentUserQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
