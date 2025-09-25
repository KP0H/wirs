using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using WebhookInbox.Domain.Entities;

namespace WebhookInbox.Infrastructure.EntityConfigurations;

public sealed class EventConfig : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> e)
    {
        e.ToTable("events");
        e.HasKey(x => x.Id);
        e.Property(x => x.Source).IsRequired();
        e.Property(x => x.ReceivedAt).IsRequired();

        // jsonb for headers, bytea for payload (Npgsql maps automatically)
        e.Property(x => x.Headers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v.RootElement.GetRawText(),
                v => JsonDocument.Parse(v, default));

        e.Property(x => x.Payload).HasColumnType("bytea");

        e.Property(x => x.SignatureStatus).HasConversion<int>();
        e.Property(x => x.Status).HasConversion<int>();

        e.HasIndex(x => new { x.Source, x.ReceivedAt });
        e.HasMany(x => x.Attempts).WithOne(a => a.Event).HasForeignKey(a => a.EventId);
    }
}