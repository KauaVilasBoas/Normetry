using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Normetry.Application.Identity.GetCurrentUserPermissions;
using Normetry.Application.Identity.SyncCurrentUser;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

namespace Normetry.Api.Auth;

/// <summary>The caller's profiles must grant <see cref="Permission"/>.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Turns <c>permission:{name}</c> (from <see cref="HasPermissionAttribute"/> or a YARP route) into
/// <see cref="Policies.Tenant"/> plus a <see cref="PermissionRequirement"/>. A name outside the
/// catalog throws, so a typo cannot become a check nobody passes.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    private readonly AuthorizationOptions options = options.Value;

    // The policy for a name never changes, so endpoints may cache it.
    public override bool AllowsCachingPolicies => true;

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
        if (!Permissions.All.Contains(permission))
        {
            throw new InvalidOperationException(
                $"'{permission}' is not in the permission catalog."
            );
        }
        return new AuthorizationPolicyBuilder()
            .Combine(options.GetPolicy(Policies.Tenant)!)
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

/// <summary>
/// Loads the caller's permissions once per authorization, under the request's tenant transaction
/// (RLS), and meets every <see cref="PermissionRequirement"/> they cover. A caller with none may
/// be signing in for the first time, so their user row is created then: the tenant's
/// administrators can then find them and assign a profile.
/// </summary>
internal sealed class PermissionHandler(IDispatcher dispatcher) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pending = context.PendingRequirements.OfType<PermissionRequirement>().ToArray();
        // Without a sub and a valid tenant the Tenant requirements of the same policy fail, and
        // nothing can be loaded.
        if (
            pending.Length == 0
            || context.User.FindFirstValue("sub") is null
            || !HttpCurrentUser.TryGetTenantId(context.User, out _)
        )
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? default;
        var permissions = await dispatcher.QueryAsync(
            new GetCurrentUserPermissionsQuery(),
            cancellationToken
        );
        if (permissions.Value.Count == 0)
        {
            await dispatcher.SendAsync(new SyncCurrentUserCommand(), cancellationToken);
            return;
        }

        foreach (var requirement in pending)
        {
            if (permissions.Value.Contains(requirement.Permission))
            {
                context.Succeed(requirement);
            }
        }
    }
}

/// <summary>
/// A caller with a valid tenant who lacks a permission gets a 403 problem with the code
/// <c>permission.denied</c>, so a client can tell it from a token without a tenant (a plain 403,
/// which <c>/me</c> answers too). Everything else goes to the default handling.
/// </summary>
internal sealed class PermissionDeniedResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        if (
            authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.ToArray()
                is { Length: > 0 } failed
            && failed.All(requirement => requirement is PermissionRequirement)
        )
        {
            var missing = string.Join(
                ", ",
                failed.Cast<PermissionRequirement>().Select(requirement => requirement.Permission)
            );
            await Result
                .Failure(
                    new Error(
                        "permission.denied",
                        $"Your profiles do not grant {missing}. Ask an administrator of your organization.",
                        ErrorType.Forbidden
                    )
                )
                .ToProblemDetails()
                .ExecuteAsync(context);
            return;
        }

        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
