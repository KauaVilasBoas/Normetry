using Microsoft.EntityFrameworkCore;
using Normetry.Domain.Establishments;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Schema <c>core</c>. At runtime it connects as <c>app_core</c> and is built with
/// <see cref="CoreDatabase.UseTenantScopedCoreDatabase"/>; only the migrations use it without a
/// tenant, as <c>core_owner</c>.
/// </summary>
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Establishment> Establishments => Set<Establishment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(CoreDatabase.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
    }
}
