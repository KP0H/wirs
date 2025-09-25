using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebhookInbox.Domain.Entities;

namespace WebhookInbox.Infrastructure.EntityConfigurations;

public sealed class DeliveryAttemptConfig : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> e)
    {
        e.ToTable("delivery_attempts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Try).IsRequired();
        e.Property(x => x.SentAt).IsRequired();
        e.Property(x => x.ResponseBody).HasColumnType("text");
        e.HasIndex(x => new { x.EventId, x.EndpointId, x.Try }).IsUnique();
    }
}
