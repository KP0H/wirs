using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebhookInbox.Domain.Entities;
using WebhookInbox.Infrastructure;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class EventQueryTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EventQueryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Events_List_Should_Return_Seeded_Event()
    {
        var eventId = Guid.NewGuid();
        await SeedAsync(eventId, withAttempt: false);

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/events?page=1&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        root.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == eventId)
            .Should().BeTrue("seeded event should be returned in listing");
    }

    [Fact]
    public async Task Event_Detail_Should_Include_Attempts_And_Payload()
    {
        var eventId = Guid.NewGuid();
        await SeedAsync(eventId, withAttempt: true);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/events/{eventId}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(eventId);
        root.GetProperty("payloadIsJson").GetBoolean().Should().BeTrue();
        root.GetProperty("payload").GetString().Should().Contain("sample");

        var attempts = root.GetProperty("attempts").EnumerateArray().ToArray();
        attempts.Should().HaveCount(1);
        attempts[0].GetProperty("endpointUrl").GetString().Should().Be("https://example.com/webhook");
        attempts[0].GetProperty("responseCode").GetInt32().Should().Be(200);
    }

    private async Task SeedAsync(Guid eventId, bool withAttempt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var headers = JsonSerializer.SerializeToDocument(new Dictionary<string, string[]> { ["X-Test"] = ["value"] });
        var payload = Encoding.UTF8.GetBytes("{\"sample\":true}");

        var entity = new Event
        {
            Id = eventId,
            Source = "github",
            Status = EventStatus.New,
            SignatureStatus = SignatureStatus.Verified,
            Headers = headers,
            Payload = payload,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        db.Events.Add(entity);

        if (withAttempt)
        {
            var endpoint = new Endpoint
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/webhook",
                IsActive = true
            };

            db.Endpoints.Add(endpoint);

            db.DeliveryAttempts.Add(new DeliveryAttempt
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Event = entity,
                EndpointId = endpoint.Id,
                Endpoint = endpoint,
                Try = 1,
                SentAt = DateTimeOffset.UtcNow,
                ResponseCode = 200,
                Success = true
            });
        }

        await db.SaveChangesAsync();
    }
}



