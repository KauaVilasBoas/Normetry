namespace Normetry.Domain.Identity;

/// <summary>
/// A person of one tenant, identified by the identity provider's <c>sub</c>. The display name and
/// email come from the token's claims, only so that the tenant's administrators can tell users
/// apart; nothing authorizes on them. What the user may do comes from their profiles.
/// </summary>
public sealed class User
{
    /// <summary>The OpenID Connect limit for a <c>sub</c> claim.</summary>
    public const int SubMaxLength = 255;

    private readonly List<UserProfile> profiles = [];

    private User(Guid id, Guid tenantId, string sub, string? displayName, string? email)
    {
        Id = id;
        TenantId = tenantId;
        Sub = sub;
        DisplayName = displayName;
        Email = email;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Sub { get; private set; }

    public string? DisplayName { get; private set; }

    public string? Email { get; private set; }

    public IReadOnlyCollection<UserProfile> Profiles => profiles;

    /// <summary>A new user with an id generated here (UUIDv7).</summary>
    public static User Create(Guid tenantId, string sub, string? displayName, string? email) =>
        new(
            Guid.CreateVersion7(),
            Names.RequiredId(tenantId, nameof(tenantId)),
            Names.Required(sub, SubMaxLength, nameof(sub)),
            string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim()
        );

    /// <summary>Gives the user <paramref name="profile"/>, which must be of the same tenant.</summary>
    public void Assign(Profile profile)
    {
        if (profile.TenantId != TenantId)
        {
            throw new ArgumentException("The profile belongs to another tenant.", nameof(profile));
        }
        if (!profiles.Any(held => held.ProfileId == profile.Id))
        {
            profiles.Add(new UserProfile(TenantId, Id, profile.Id));
        }
    }
}

/// <summary>One profile a user holds (<c>core.user_profiles</c>).</summary>
public sealed class UserProfile
{
    internal UserProfile(Guid tenantId, Guid userId, Guid profileId)
    {
        TenantId = tenantId;
        UserId = userId;
        ProfileId = profileId;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ProfileId { get; private set; }
}
