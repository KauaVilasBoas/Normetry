namespace Normetry.Domain.Identity;

/// <summary>
/// A named set of permissions in one tenant. Every tenant has three system profiles (Owner,
/// Manager, Auditor), created with the tenant, plus any it adds. System profiles keep their name
/// and are never deleted; Owner grants every permission and cannot be changed at all.
/// </summary>
public sealed class Profile
{
    public const int NameMaxLength = 100;

    private readonly List<ProfilePermission> grantedPermissions = [];

    private Profile(Guid id, Guid tenantId, string name, string? systemKey, bool grantsAll)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        SystemKey = systemKey;
        GrantsAll = grantsAll;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// One of <see cref="SystemKeys"/> for a system profile, <c>null</c> for the tenant's own. It
    /// never changes, so clients and the seed find system profiles by it, not by name.
    /// </summary>
    public string? SystemKey { get; private set; }

    /// <summary>Created with the tenant: it keeps its name and cannot be deleted.</summary>
    public bool IsSystem => SystemKey is not null;

    /// <summary>
    /// Only Owner: every permission of the catalog, present and future, with no rows in
    /// <see cref="GrantedPermissions"/>.
    /// </summary>
    public bool GrantsAll { get; private set; }

    public IReadOnlyCollection<ProfilePermission> GrantedPermissions => grantedPermissions;

    /// <summary>A tenant's own profile, with no permissions yet. The id is UUIDv7, generated here.</summary>
    public static Profile Create(Guid tenantId, string name) =>
        new(
            Guid.CreateVersion7(),
            Names.RequiredId(tenantId, nameof(tenantId)),
            Names.Required(name, NameMaxLength, nameof(name)),
            systemKey: null,
            grantsAll: false
        );

    /// <summary>
    /// The system profile for <paramref name="systemKey"/>: Owner (every permission), Manager
    /// (every permission but <c>profiles.manage</c>) or Auditor (read and ask). Created in the
    /// transaction that creates the tenant, or by the startup step for a tenant that lacks it;
    /// never by a migration.
    /// </summary>
    public static Profile CreateSystem(Guid tenantId, string systemKey)
    {
        Names.RequiredId(tenantId, nameof(tenantId));
        var (name, permissions) = systemKey switch
        {
            SystemKeys.Owner => ("Owner", Array.Empty<string>()),
            SystemKeys.Manager => (
                "Manager",
                Permissions.All.Where(name => name != Permissions.Profiles.Manage).ToArray()
            ),
            SystemKeys.Auditor => (
                "Auditor",
                new[]
                {
                    Permissions.Establishments.Read,
                    Permissions.Documents.Read,
                    Permissions.Chat.Ask,
                    Permissions.Profiles.Read,
                }
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(systemKey),
                systemKey,
                "Unknown system profile."
            ),
        };
        var profile = new Profile(
            Guid.CreateVersion7(),
            tenantId,
            name,
            systemKey,
            grantsAll: systemKey == SystemKeys.Owner
        );
        profile.Grant(permissions);
        return profile;
    }

    public void Rename(string name)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("A system profile keeps its name.");
        }
        Name = Names.Required(name, NameMaxLength, nameof(name));
    }

    /// <summary>
    /// Replaces the profile's permissions (rows are added and removed, never changed). A
    /// permission of an area implies that area's <c>.read</c>, so it is added when the catalog has
    /// one: <c>documents.upload</c> brings <c>documents.read</c>.
    /// </summary>
    public void SetPermissions(IEnumerable<string> names)
    {
        if (GrantsAll)
        {
            throw new InvalidOperationException("The Owner profile cannot be changed.");
        }

        var wanted = WithImpliedReads(names);
        grantedPermissions.RemoveAll(granted => !wanted.Contains(granted.Permission));
        Grant(wanted);
    }

    /// <summary><paramref name="names"/> plus the <c>area.read</c> each other permission implies.</summary>
    public static IReadOnlySet<string> WithImpliedReads(IEnumerable<string> names)
    {
        var result = names.ToHashSet(StringComparer.Ordinal);
        foreach (var name in result.ToArray())
        {
            var read = $"{name[..Math.Max(name.IndexOf('.'), 0)]}.read";
            if (Permissions.All.Contains(read))
            {
                result.Add(read);
            }
        }
        return result;
    }

    private void Grant(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!Permissions.All.Contains(name))
            {
                throw new ArgumentException(
                    $"'{name}' is not in the permission catalog.",
                    nameof(names)
                );
            }
            if (!grantedPermissions.Any(granted => granted.Permission == name))
            {
                grantedPermissions.Add(new ProfilePermission(TenantId, Id, name));
            }
        }
    }

    /// <summary>The stable keys of the three system profiles.</summary>
    public static class SystemKeys
    {
        public const string Owner = "owner";
        public const string Manager = "manager";
        public const string Auditor = "auditor";

        public static readonly IReadOnlyList<string> All = [Owner, Manager, Auditor];
    }
}

/// <summary>One permission a profile grants (<c>core.profile_permissions</c>).</summary>
public sealed class ProfilePermission
{
    internal ProfilePermission(Guid tenantId, Guid profileId, string permission)
    {
        TenantId = tenantId;
        ProfileId = profileId;
        Permission = permission;
    }

    public Guid TenantId { get; private set; }

    public Guid ProfileId { get; private set; }

    public string Permission { get; private set; }
}
