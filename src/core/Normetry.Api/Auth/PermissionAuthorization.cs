using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Normetry.Application.Identity.GetCurrentUserPermissions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

namespace Normetry.Api.Auth;

/// <summary>The caller's profiles must grant one of <see cref="AnyOf"/>.</summary>
public sealed record PermissionRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;

/// <summary>
/// Turns <c>permission:{name}</c> or <c>permission:{name}|{name}</c> (from
/// <see cref="HasPermissionAttribute"/> or a YARP route) into <see cref="Policies.Tenant"/> plus a
/// <see cref="PermissionRequirement"/>. A name outside the catalog throws, so a typo cannot become
/// a check nobody passes.
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

        var anyOf = policyName[HasPermissionAttribute.PolicyPrefix.Length..].Split('|');
        if (anyOf.FirstOrDefault(name => !Permissions.All.Contains(name)) is { } unknown)
        {
            throw new InvalidOperationException($"'{unknown}' is not in the permission catalog.");
        }
        return new AuthorizationPolicyBuilder()
            .Combine(options.GetPolicy(Policies.Tenant)!)
            .AddRequirements(new PermissionRequirement(anyOf))
            .Build();
    }
}

/// <summary>
/// Loads the caller's permissions once per authorization, under the request's tenant transaction
/// (RLS), and meets every <see cref="PermissionRequirement"/> they cover. It only reads: the user
/// row is created by <c>/me</c>, which the web app calls first at sign-in.
/// </summary>
internal sealed class PermissionHandler(IDispatcher dispatcher) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pending = context.PendingRequirements.OfType<PermissionRequirement>().ToArray();
        // Without a sub (an empty one counts as none) and a valid tenant the Tenant requirements
        // of the same policy fail, and nothing can be loaded.
        if (
            pending.Length == 0
            || context.User.FindFirstValue("sub") is not { Length: > 0 }
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
        foreach (var requirement in pending)
        {
            if (requirement.AnyOf.Any(permissions.Value.Contains))
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
                failed
                    .Cast<PermissionRequirement>()
                    .Select(requirement => string.Join(" or ", requirement.AnyOf))
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
