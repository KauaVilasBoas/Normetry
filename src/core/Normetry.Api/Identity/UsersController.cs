using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Identity.AssignProfile;
using Normetry.Application.Identity.ListUsers;
using Normetry.Application.Identity.UnassignProfile;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Identity;

/// <summary>
/// The tenant's users and the profiles they hold. A user appears after their first sign-in.
/// Assigning profiles is part of <c>profiles.manage</c>.
/// </summary>
[ApiController]
[Route("users")]
public sealed class UsersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Profiles.Read)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListUsersQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    [HttpPut("{userId:guid}/profiles/{profileId:guid}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> Assign(
        Guid userId,
        Guid profileId,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new AssignProfileCommand(userId, profileId),
            cancellationToken
        );
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }

    /// <summary>Refused with <c>profile.last_owner</c> for the tenant's last Owner.</summary>
    [HttpDelete("{userId:guid}/profiles/{profileId:guid}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> Unassign(
        Guid userId,
        Guid profileId,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new UnassignProfileCommand(userId, profileId),
            cancellationToken
        );
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }
}
