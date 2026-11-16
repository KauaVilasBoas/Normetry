using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Identity.CreateProfile;
using Normetry.Application.Identity.DeleteProfile;
using Normetry.Application.Identity.GetProfile;
using Normetry.Application.Identity.ListProfiles;
using Normetry.Application.Identity.RenameProfile;
using Normetry.Application.Identity.SetProfilePermissions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Identity;

/// <summary>
/// The tenant's profiles. Another tenant's id answers 404 like an unknown one: RLS hides the row.
/// </summary>
[ApiController]
[Route("profiles")]
public sealed class ProfilesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Profiles.Read)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListProfilesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Profiles.Read)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetProfileQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    [HttpPost]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> Create(
        CreateProfileCommand command,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(command, cancellationToken);
        return result.IsSuccess
            ? Created($"/profiles/{result.Value}", new { id = result.Value })
            : result.ToActionResult();
    }

    [HttpPatch("{id:guid}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> Rename(
        Guid id,
        RenameRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new RenameProfileCommand(id, request.Name),
            cancellationToken
        );
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteProfileCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }

    /// <summary>Replaces what the profile grants and answers with the profile as stored.</summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<IActionResult> SetPermissions(
        Guid id,
        PermissionsRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new SetProfilePermissionsCommand(id, request.Permissions),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    public sealed record RenameRequest(string Name);

    public sealed record PermissionsRequest(IReadOnlyList<string> Permissions);
}
