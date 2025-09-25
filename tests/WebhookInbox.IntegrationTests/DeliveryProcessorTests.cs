using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;
using WebhookInbox.Worker;
using FluentAssertions;

namespace WebhookInbox.IntegrationTests;

public class DeliveryProcessorTests
{
    private static AppDbContext CreateDb(out SqliteConnection conn)
    {
        conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task ProcessOnce_Order_Should_Not_Throw_On_Sqlite_DateTimeOffset()
    {
        using var db = CreateDb(out _);
        // Добавим пару событий с разными ReceivedAt — не важно, что OrderBy по Id
        db.Events.AddRange(
            new Event { Id = Guid.NewGuid(), Source = "t1", ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new Event { Id = Guid.NewGuid(), Source = "t2", ReceivedAt = DateTimeOffset.UtcNow }
        );
        db.Endpoints.Add(new Endpoint { Id = Guid.NewGuid(), Url = "https://example.org/hook", IsActive = true });
        await db.SaveChangesAsync();

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var sp = new ServiceCollection().AddSingleton<IHttpClientFactory>(new SimpleFactory(new HttpClient(handler))).BuildServiceProvider();

        var proc = new DeliveryProcessor(
            db, sp.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new DeliveryOptions { BatchSize = 10 }),
            new SystemDateTimeProvider(),
            NullLogger<DeliveryProcessor>.Instance);

        var processed = await proc.ProcessOnceAsync(default);
        processed.Should().Be(2);
    }


    [Fact]
    public async Task ProcessOnce_Should_Record_Success_Attempt_And_Dispatch_Event()
    {
        using var db = CreateDb(out var conn);

        var ev = new Event { Id = Guid.NewGuid(), Source = "test", Payload = "{ }"u8.ToArray() };
        db.Events.Add(ev);
        var ep = new Endpoint { Id = Guid.NewGuid(), Url = "https://example.org/hook", IsActive = true };
        db.Endpoints.Add(ep);
        await db.SaveChangesAsync();

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        });
        var client = new HttpClient(handler);

        var sp = new ServiceCollection()
            .AddSingleton<IHttpClientFactory>(new SimpleFactory(client))
            .BuildServiceProvider();

        var proc = new DeliveryProcessor(
            db,
            sp.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new DeliveryOptions { BatchSize = 10 }),
            new SystemDateTimeProvider(),
            NullLogger<DeliveryProcessor>.Instance);

        var processed = await proc.ProcessOnceAsync(default);
        processed.Should().Be(1);

        (await db.DeliveryAttempts.CountAsync()).Should().Be(1);
        (await db.Events.FindAsync(ev.Id))!.Status.Should().Be(EventStatus.Dispatched);
    }

    [Fact]
    public async Task ProcessOnce_Should_Record_Fail_Attempt_And_Flag_Event()
    {
        using var db = CreateDb(out var conn);

        var ev = new Event { Id = Guid.NewGuid(), Source = "test", Payload = "{ }"u8.ToArray() };
        db.Events.Add(ev);
        var ep = new Endpoint { Id = Guid.NewGuid(), Url = "https://example.org/hook", IsActive = true };
        db.Endpoints.Add(ep);
        await db.SaveChangesAsync();

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        });
        var client = new HttpClient(handler);

        var sp = new ServiceCollection()
            .AddSingleton<IHttpClientFactory>(new SimpleFactory(client))
            .BuildServiceProvider();

        var proc = new DeliveryProcessor(
            db,
            sp.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new DeliveryOptions { BatchSize = 10 }),
            new SystemDateTimeProvider(),
            NullLogger<DeliveryProcessor>.Instance);

        var processed = await proc.ProcessOnceAsync(default);
        processed.Should().Be(1);

        (await db.DeliveryAttempts.CountAsync()).Should().Be(1);
        (await db.Events.FindAsync(ev.Id))!.Status.Should().Be(EventStatus.Failed);
    }

    private class SimpleFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
