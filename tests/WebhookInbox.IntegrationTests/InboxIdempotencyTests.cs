using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebhookInbox.Api.Signatures;
using WebhookInbox.Infrastructure;

namespace WebhookInbox.IntegrationTests;

public class InboxIdempotencyTests : IClassFixture<WebAppFactoryWithInMemoryIdem>
{
    private readonly WebAppFactoryWithInMemoryIdem _factory;
    private readonly string _githubSecret;
    private readonly string _stripeSecret;

    public InboxIdempotencyTests(WebAppFactoryWithInMemoryIdem factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SignatureOptions>>().Value;

        _githubSecret = options.Sources
            .FirstOrDefault(s => string.Equals(s.Source, "github", StringComparison.OrdinalIgnoreCase))?.Secret
            ?? throw new InvalidOperationException("GitHub signature secret not configured for tests.");

        _stripeSecret = options.Sources
            .FirstOrDefault(s => string.Equals(s.Source, "stripe", StringComparison.OrdinalIgnoreCase))?.Secret
            ?? throw new InvalidOperationException("Stripe signature secret not configured for tests.");
    }

    [Fact]
    public async Task Duplicate_By_Idempotency_Key_Should_Return_Same_EventId_And_Not_Insert_Second_Row()
    {
        var client = _factory.CreateClient();
        var payload = "{\"x\":1}";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Idempotency-Key", "req-123");
        content.Headers.Add("X-Hub-Signature-256", ComputeGithubSignature(payload));

        // first call -> 202
        var res1 = await client.PostAsync("/api/inbox/github", content);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        // second call (duplicate) -> 200 duplicate=true
        var content2 = new StringContent(payload, Encoding.UTF8, "application/json");
        content2.Headers.Add("Idempotency-Key", "req-123");
        content2.Headers.Add("X-Hub-Signature-256", ComputeGithubSignature(payload));

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

        var content1 = new StringContent(payload, Encoding.UTF8, "application/json");
        content1.Headers.Add("Stripe-Signature", CreateStripeSignatureHeader(payload));

        var res1 = await client.PostAsync("/api/inbox/stripe", content1);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        var content2 = new StringContent(payload, Encoding.UTF8, "application/json");
        content2.Headers.Add("Stripe-Signature", CreateStripeSignatureHeader(payload));

        var res2 = await client.PostAsync("/api/inbox/stripe", content2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var id2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetGuid();

        id2.Should().Be(id1);
    }

    private string ComputeGithubSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_githubSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string CreateStripeSignatureHeader(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_stripeSecret));
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = hmac.ComputeHash(signedPayload);
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}

