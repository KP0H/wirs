using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebhookInbox.Api.Signatures;
using WebhookInbox.Infrastructure;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class InboxIngestionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly string _githubSecret;

    public InboxIngestionTests(ApiFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SignatureOptions>>();
        _githubSecret = options.Value.Sources
            .FirstOrDefault(s => string.Equals(s.Source, "github", StringComparison.OrdinalIgnoreCase))?.Secret
            ?? throw new InvalidOperationException("GitHub signature secret not configured for tests.");
    }

    [Fact]
    public async Task Post_Inbox_Should_Persist_And_Return_202_With_EventId()
    {
        var client = _factory.CreateClient();
        var payload = "{\"ok\":true}";

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Test", "abc");
        content.Headers.Add("X-Hub-Signature-256", ComputeGithubSignature(payload));

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

    private string ComputeGithubSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_githubSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

