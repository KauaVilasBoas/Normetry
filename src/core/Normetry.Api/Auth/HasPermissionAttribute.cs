using Microsoft.AspNetCore.Authorization;

namespace Normetry.Api.Auth;

/// <summary>
/// Requires a permission of the catalog (<c>Normetry.Domain.Identity.Permissions</c>) on top of
/// <see cref="Policies.Tenant"/>. The caller has it when one of their profiles grants it, read from
/// the core database on each request (<see cref="PermissionHandler"/>). One attribute naming
/// several permissions needs any one of them (the handler then shows only what each covers);
/// several attributes all apply. Every controller action carries one, or <c>[AllowAnonymous]</c>;
/// <c>AuthConfigurationTests</c> checks it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission, params string[] alternatives)
    : AuthorizeAttribute(PolicyName([permission, .. alternatives]))
{
    /// <summary>Policy names <see cref="PermissionPolicyProvider"/> builds on demand.</summary>
    public const string PolicyPrefix = "permission:";

    /// <summary>Joins the names of a policy any one of which will do.</summary>
    public const char AnyOfSeparator = '|';

    /// <summary>The caller needs one of them.</summary>
    public IReadOnlyList<string> AnyOf { get; } = [permission, .. alternatives];

    /// <summary><c>permission:{name}</c>, the names joined by <c>|</c> when any of several will do.</summary>
    public static string PolicyName(params string[] anyOf) =>
        PolicyPrefix + string.Join(AnyOfSeparator, anyOf);
}
