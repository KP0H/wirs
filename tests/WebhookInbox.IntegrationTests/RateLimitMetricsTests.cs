using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class RateLimitMetricsTests : IClassFixture<RateLimitApiFactory>
{
    private readonly HttpClient _client;
    private const string Secret = "gh_test_secret";

    public RateLimitMetricsTests(RateLimitApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Rate_Limit_Blocks_Should_Be_Exported_As_Prometheus_Counter()
    {
        static string Sign(string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        async Task<HttpStatusCode> SendAsync(string payload)
        {
            var body = new StringContent(payload, Encoding.UTF8, "application/json");
            body.Headers.Add("X-Hub-Signature-256", Sign(payload));
            body.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

            var response = await _client.PostAsync("/api/inbox/github", body);
            return response.StatusCode;
        }

        const string payload = "{\"ok\":true}";

        var first = await SendAsync(payload);
        var second = await SendAsync(payload);
        var third = await SendAsync(payload);

        first.Should().Be(HttpStatusCode.Accepted);
        second.Should().Be(HttpStatusCode.Accepted);
        third.Should().Be(HttpStatusCode.TooManyRequests);

        var metrics = await _client.GetStringAsync("/metrics");
        metrics.Should().Contain("webhookinbox_rate_limit_blocked_total");
        metrics.Should().Contain("source=\"github\"");
    }
}

