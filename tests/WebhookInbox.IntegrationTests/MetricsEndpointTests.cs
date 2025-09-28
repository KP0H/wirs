using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class MetricsEndpointTests : IClassFixture<SignatureApiFactory>
{
    private readonly HttpClient _client;
    public MetricsEndpointTests(SignatureApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Metrics_Should_Expose_Prometheus_Text_And_Custom_Counters()
    {
        // 1) succes (ingested)
        var secret = "gh_test_secret";
        var payload = "{\"ok\":true}";
        var body = Encoding.UTF8.GetBytes(payload);
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = "sha256=" + Convert.ToHexString(h.ComputeHash(body)).ToLowerInvariant();

        var req = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.Add("X-Hub-Signature-256", sig);

        var ok = await _client.PostAsync("/api/inbox/github", req);
        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // 2) reject by sign
        var bad = new StringContent(payload, Encoding.UTF8, "application/json");
        bad.Headers.Add("X-Hub-Signature-256", "sha256=badsign");
        var resBad = await _client.PostAsync("/api/inbox/github", bad);
        resBad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3) /metrics
        var metrics = await _client.GetStringAsync("/metrics");
        metrics.Should().Contain("webhookinbox_events_total");
        metrics.Should().Contain("webhookinbox_signature_validation_failures_total");
    }
}
