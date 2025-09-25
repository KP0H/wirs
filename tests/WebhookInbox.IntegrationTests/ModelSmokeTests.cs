using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;

namespace WebhookInbox.IntegrationTests;

public class ModelSmokeTests
{
    [Fact]
    public async Task Model_Should_Create_InMemory_Sqlite()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;

        using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync(); // not migrations, just model

        db.Events.Add(new Event { Source = "test" });
        await db.SaveChangesAsync();

        var count = await db.Events.CountAsync();
        count.Should().Be(1);
    }
}
