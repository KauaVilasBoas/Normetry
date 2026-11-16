using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Tenancy;
using Normetry.Infrastructure.Messaging;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => new { message.TenantId, message.Id });
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(100);
        builder.Property(message => message.RoutingKey).HasMaxLength(100);
        // json keeps the body exactly as serialized (jsonb would reorder its keys).
        builder.Property(message => message.Body).HasColumnType("json");
        builder.Property(message => message.TraceParent).HasMaxLength(55);
        builder.Property(message => message.TraceState).HasMaxLength(512);
        // The dispatcher's batch: a tenant's pending rows, oldest first.
        builder
            .HasIndex(message => new { message.TenantId, message.CreatedAt })
            .HasFilter("sent_at IS NULL");
        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(message => message.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OutboxTenantConfiguration : IEntityTypeConfiguration<OutboxTenant>
{
    public void Configure(EntityTypeBuilder<OutboxTenant> builder)
    {
        builder.ToTable("outbox_tenants");
        builder.HasKey(tenant => tenant.TenantId);
        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(tenant => tenant.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        // Leads with tenant_id like every tenant-scoped lookup. The AI service derives its event
        // ids from the tenant too, so two tenants never share one anyway.
        builder.HasKey(message => new { message.TenantId, message.MessageId });
        builder.Property(message => message.Type).HasMaxLength(100);
    }
}
