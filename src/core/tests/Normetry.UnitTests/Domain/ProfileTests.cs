using Normetry.Domain.Identity;
using Xunit;

namespace Normetry.UnitTests.Domain;

public sealed class ProfileTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static string[] Granted(Profile profile) =>
        profile
            .GrantedPermissions.Select(granted => granted.Permission)
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void The_system_profiles_are_Owner_Manager_and_Auditor()
    {
        var owner = Profile.CreateSystem(Tenant, Profile.SystemKeys.Owner);
        var manager = Profile.CreateSystem(Tenant, Profile.SystemKeys.Manager);
        var auditor = Profile.CreateSystem(Tenant, Profile.SystemKeys.Auditor);

        Assert.Equal(("Owner", true, true), (owner.Name, owner.IsSystem, owner.GrantsAll));
        Assert.Empty(owner.GrantedPermissions);
        Assert.False(manager.GrantsAll);
        Assert.Equal(
            Permissions
                .All.Where(name => name != Permissions.Profiles.Manage)
                .Order(StringComparer.Ordinal),
            Granted(manager)
        );
        Assert.Equal(
            [
                Permissions.Chat.Ask,
                Permissions.Documents.Read,
                Permissions.Establishments.Read,
                Permissions.Inspections.Read,
                Permissions.Profiles.Read,
            ],
            Granted(auditor)
        );
        // Manager can run and review inspections; Auditor only reads them.
        Assert.Contains(Permissions.Inspections.Run, Granted(manager));
        Assert.Contains(Permissions.Inspections.Review, Granted(manager));
        foreach (var profile in new[] { owner, manager, auditor })
        {
            Assert.Equal(
                Profile.DefaultPermissions(profile.SystemKey!).Order(StringComparer.Ordinal),
                Granted(profile)
            );
        }
    }

    [Fact]
    public void An_unknown_system_key_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile.CreateSystem(Tenant, "admin"));

    [Theory]
    [InlineData(new[] { "documents.upload" }, new[] { "documents.read", "documents.upload" })]
    [InlineData(
        new[] { "establishments.manage", "profiles.manage" },
        new[] { "establishments.manage", "establishments.read", "profiles.manage", "profiles.read" }
    )]
    [InlineData(new[] { "chat.ask" }, new[] { "chat.ask" })]
    [InlineData(new string[0], new string[0])]
    public void Setting_permissions_adds_the_read_each_area_implies(
        string[] given,
        string[] expected
    )
    {
        var profile = Profile.Create(Tenant, "Kitchen");

        profile.SetPermissions(given);

        Assert.Equal(expected, Granted(profile));
    }

    [Fact]
    public void Setting_permissions_replaces_the_previous_ones()
    {
        var profile = Profile.Create(Tenant, "Kitchen");
        profile.SetPermissions([Permissions.Documents.Delete, Permissions.Chat.Ask]);

        profile.SetPermissions([Permissions.Chat.Ask]);

        Assert.Equal([Permissions.Chat.Ask], Granted(profile));
    }

    [Fact]
    public void A_permission_outside_the_catalog_is_refused() =>
        Assert.Throws<ArgumentException>(() =>
            Profile.Create(Tenant, "Kitchen").SetPermissions(["reports.export"])
        );

    [Fact]
    public void Owner_cannot_be_changed_and_system_profiles_keep_their_name()
    {
        var owner = Profile.CreateSystem(Tenant, Profile.SystemKeys.Owner);
        var manager = Profile.CreateSystem(Tenant, Profile.SystemKeys.Manager);

        Assert.Throws<InvalidOperationException>(() => owner.SetPermissions([]));
        Assert.Throws<InvalidOperationException>(() => manager.Rename("Boss"));
        manager.SetPermissions([Permissions.Chat.Ask]);
        Assert.Equal([Permissions.Chat.Ask], Granted(manager));
    }
}
