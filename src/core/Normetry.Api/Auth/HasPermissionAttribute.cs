using Microsoft.AspNetCore.Authorization;

namespace Normetry.Api.Auth;

/// <summary>
/// Requires a permission of the catalog (<c>Normetry.Domain.Identity.Permissions</c>) on top of
/// <see cref="Policies.Tenant"/>. The caller has it when one of their profiles grants it, read from
/// the core database on each request (<see cref="PermissionHandler"/>). Every controller action
/// carries one, or <c>[AllowAnonymous]</c>; <c>AuthConfigurationTests</c> checks it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute(PolicyName(permission))
{
    /// <summary>Policy names <see cref="PermissionPolicyProvider"/> builds on demand.</summary>
    public const string PolicyPrefix = "permission:";

    public string Permission { get; } = permission;

    public static string PolicyName(string permission) => PolicyPrefix + permission;
}
