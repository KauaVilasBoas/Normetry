using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Identity.ListPermissions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Identity;

[ApiController]
[Route("permissions")]
public sealed class PermissionsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The permission catalog, for the profile editor.</summary>
    [HttpGet]
    [HasPermission(Permissions.Profiles.Read)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListPermissionsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
