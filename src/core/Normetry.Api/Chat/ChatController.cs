using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Usage.GetChatQuota;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api.Chat;

[ApiController]
[Route("chat")]
public sealed class ChatController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>What the caller has left today, so the web app can show it before sending.</summary>
    [HttpGet("quota")]
    [Authorize(Policy = Policies.Tenant)]
    public async Task<IActionResult> GetQuota(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetChatQuotaQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }
}
