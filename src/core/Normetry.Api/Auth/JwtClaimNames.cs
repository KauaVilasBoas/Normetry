namespace Normetry.Api.Auth;

/// <summary>
/// The claims the API reads from a validated token, under the names the realm issues them
/// (<c>MapInboundClaims = false</c> keeps them). No other file spells a claim name.
/// </summary>
internal static class JwtClaimNames
{
    /// <summary>The user's id. Users are identified by it, never by email.</summary>
    public const string Subject = "sub";

    /// <summary>The client the token was issued to; <c>AuthSetup</c> accepts only listed ones.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The caller's tenant, the only source of a request's tenant.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Display only, like <see cref="PreferredUsername"/> and <see cref="Email"/>.</summary>
    public const string Name = "name";

    public const string PreferredUsername = "preferred_username";

    public const string Email = "email";
}
