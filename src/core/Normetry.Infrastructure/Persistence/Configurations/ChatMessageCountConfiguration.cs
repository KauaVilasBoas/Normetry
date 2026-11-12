using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Tenancy;
using Normetry.Domain.Usage;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageCountConfiguration : IEntityTypeConfiguration<ChatMessageCount>
{
    public void Configure(EntityTypeBuilder<ChatMessageCount> builder)
    {
        builder.ToTable("chat_message_counts");
        // One row per tenant, user and UTC day. The key leads with tenant_id like every
        // tenant-scoped lookup (the RLS predicate included), is the conflict target of the
        // increment and indexes the foreign key.
        builder.HasKey(count => new
        {
            count.TenantId,
            count.UserId,
            count.Day,
        });
        builder.Property(count => count.UserId).HasMaxLength(ChatMessageCount.UserIdMaxLength);

        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(count => count.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
