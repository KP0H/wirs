using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebhookInbox.Domain.Entities;

namespace WebhookInbox.Infrastructure.EntityConfigurations;

public sealed class EndpointConfig : IEntityTypeConfiguration<Endpoint>
{
    public void Configure(EntityTypeBuilder<Endpoint> e)
    {
        e.ToTable("endpoints");
        e.HasKey(x => x.Id);
        e.Property(x => x.Url).IsRequired();
        e.Property(x => x.Secret);
        e.Property(x => x.IsActive).HasDefaultValue(true);
        e.Property(x => x.RateLimitPerMinute);
        e.Property(x => x.PolicyJson).HasColumnType("jsonb");
        e.HasMany(x => x.Attempts).WithOne(a => a.Endpoint).HasForeignKey(a => a.EndpointId);
    }
}