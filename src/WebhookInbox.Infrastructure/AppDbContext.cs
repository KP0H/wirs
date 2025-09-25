using Microsoft.EntityFrameworkCore;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure.EntityConfigurations;

namespace WebhookInbox.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Endpoint> Endpoints => Set<Endpoint>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new EventConfig());
        b.ApplyConfiguration(new EndpointConfig());
        b.ApplyConfiguration(new DeliveryAttemptConfig());
    }
}
