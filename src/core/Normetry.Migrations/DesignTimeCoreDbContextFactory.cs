using Microsoft.EntityFrameworkCore.Design;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Migrations;

/// <summary>
/// Lets <c>dotnet ef</c> build the context. <c>migrations add</c> never connects, so it works
/// without the variable; commands that do connect need it set to the <c>core_owner</c> role.
/// </summary>
internal sealed class DesignTimeCoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args) =>
        CoreMigrator.CreateDbContext(
            Environment.GetEnvironmentVariable(CoreMigrator.ConnectionStringVariable)
                ?? "Host=localhost;Database=normetry"
        );
}
