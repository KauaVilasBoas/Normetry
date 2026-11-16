using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Identity.GetCurrentUser;
using Normetry.Application.Identity.SyncCurrentUser;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api.Identity;

[ApiController]
[Route("me")]
public sealed class MeController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>
    /// Who the caller is. The web app calls it right after sign-in, so it also creates the
    /// caller's user row the first time (no profiles yet) and refreshes their name and email.
    /// </summary>
    [HttpGet]
    // The one action that needs no permission: a user with no profile learns here that they
    // have none, and a token without a valid tenant gets its 403 here.
    [Authorize(Policy = Policies.Tenant)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var synced = await dispatcher.SendAsync(new SyncCurrentUserCommand(), cancellationToken);
        if (!synced.IsSuccess)
        {
            return synced.ToActionResult();
        }

        var result = await dispatcher.QueryAsync(new GetCurrentUserQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
