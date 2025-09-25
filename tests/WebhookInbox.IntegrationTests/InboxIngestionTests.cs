using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using WebhookInbox.Infrastructure;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class InboxIngestionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InboxIngestionTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Inbox_Should_Persist_And_Return_202_With_EventId()
    {
        var client = _factory.CreateClient();

        var content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json");
        content.Headers.Add("X-Test", "abc");

        var res = await client.PostAsync("/api/inbox/github", content);

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.TryGetProperty("eventId", out var idProp).Should().BeTrue();
        var eventId = idProp.GetGuid();

        // Check db record
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.Events.FindAsync(eventId);
        exists.Should().NotBeNull();
        exists!.Source.Should().Be("github");
    }

    [Fact]
    public async Task Post_Inbox_Empty_Source_Should_Return_400()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsync("/api/inbox/%20", new StringContent("{}", Encoding.UTF8, "application/json"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
