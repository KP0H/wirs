using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using WebhookInbox.Infrastructure;

namespace WebhookInbox.IntegrationTests;

public class InboxIdempotencyTests : IClassFixture<WebAppFactoryWithInMemoryIdem>
{
    private readonly WebAppFactoryWithInMemoryIdem _factory;
    public InboxIdempotencyTests(WebAppFactoryWithInMemoryIdem factory) => _factory = factory;

    [Fact]
    public async Task Duplicate_By_Idempotency_Key_Should_Return_Same_EventId_And_Not_Insert_Second_Row()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json");
        content.Headers.Add("Idempotency-Key", "req-123");

        // first call -> 202
        var res1 = await client.PostAsync("/api/inbox/github", content);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        // second call (duplicate) -> 200 duplicate=true
        var content2 = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json");
        content2.Headers.Add("Idempotency-Key", "req-123");
        var res2 = await client.PostAsync("/api/inbox/github", content2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement;
        var id2 = body2.GetProperty("eventId").GetGuid();
        body2.GetProperty("duplicate").GetBoolean().Should().BeTrue();

        id2.Should().Be(id1);

        // DB should have only one row
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Events.CountAsync(e => e.Id == id1)).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_By_Payload_Hash_When_No_Header_Should_Work()
    {
        var client = _factory.CreateClient();
        var payload = "{\"a\":42}";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var res1 = await client.PostAsync("/api/inbox/stripe", content);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        var res2 = await client.PostAsync("/api/inbox/stripe", new StringContent(payload, Encoding.UTF8, "application/json"));
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var id2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        id2.Should().Be(id1);
    }
}
