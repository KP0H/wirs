using System.Net;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;
using WebhookInbox.Worker;

namespace WebhookInbox.IntegrationTests;

public class DeliveryRetryTests
{
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private static AppDbContext CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Failure_Should_Schedule_NextAttempt_According_To_Backoff()
    {
        using var db = CreateDb();

        var ev = new Event { Id = Guid.NewGuid(), Source = "t", Payload = "{ }"u8.ToArray() };
        var ep = new Endpoint { Id = Guid.NewGuid(), Url = "https://example.org/hook", IsActive = true };
        db.Events.Add(ev); db.Endpoints.Add(ep); await db.SaveChangesAsync();

        // Response 500
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        });
        var client = new HttpClient(handler);

        var sp = new ServiceCollection()
            .AddSingleton<IHttpClientFactory>(new SimpleFactory(client))
            .BuildServiceProvider();

        var now = new DateTimeOffset(2025, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var opts = Options.Create(new DeliveryOptions { BackoffSeconds = [5, 10], MaxAttempts = 6 });

        var proc = new DeliveryProcessor(
            db,
            sp.GetRequiredService<IHttpClientFactory>(),
            opts,
            new FixedClock(now),
            NullLogger<DeliveryProcessor>.Instance);

        var processed = await proc.ProcessOnceAsync(default);
        processed.Should().Be(1);

        var attempt = await db.DeliveryAttempts.SingleAsync();
        attempt.Success.Should().BeFalse();
        attempt.Try.Should().Be(1);
        attempt.NextAttemptAt.Should().Be(now.AddSeconds(5)); // первый интервал
        (await db.Events.FindAsync(ev.Id))!.Status.Should().Be(EventStatus.Failed);
    }

    [Fact]
    public async Task Exhausted_Backoff_Should_Mark_DeadLetter()
    {
        using var db = CreateDb();

        var ev = new Event { Id = Guid.NewGuid(), Source = "t", Payload = "{ }"u8.ToArray() };
        var ep = new Endpoint { Id = Guid.NewGuid(), Url = "https://example.org/hook", IsActive = true };
        db.Events.Add(ev); db.Endpoints.Add(ep); await db.SaveChangesAsync();

        // Always 500
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler);
        var sp = new ServiceCollection().AddSingleton<IHttpClientFactory>(new SimpleFactory(client)).BuildServiceProvider();

        var now = new DateTimeOffset(2025, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var opts = Options.Create(new DeliveryOptions { BackoffSeconds = [1], MaxAttempts = 2 });

        var proc = new DeliveryProcessor(db, sp.GetRequiredService<IHttpClientFactory>(), opts, new FixedClock(now), NullLogger<DeliveryProcessor>.Instance);

        // first attemps
        (await proc.ProcessOnceAsync(default)).Should().Be(1);

        // imitate dalay 1 after 1 sec and second attempt
        var proc2 = new DeliveryProcessor(db, sp.GetRequiredService<IHttpClientFactory>(), opts, new FixedClock(now.AddSeconds(2)), NullLogger<DeliveryProcessor>.Instance);
        (await proc2.ProcessOnceAsync(default)).Should().Be(1);

        // reach limit - DLQ
        (await db.Events.FindAsync(ev.Id))!.Status.Should().Be(EventStatus.DeadLetter);
    }

    private class SimpleFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
