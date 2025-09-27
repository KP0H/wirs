using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class RateLimitingTests : IClassFixture<RateLimitApiFactory>
{
    private readonly HttpClient _client;
    private const string Secret = "gh_test_secret";

    public RateLimitingTests(RateLimitApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Inbox_Should_Return_429_When_Exceeding_PerSource_RPM()
    {
        static string Sig(string payload)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var hash = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        var payload = "{\"ok\":true}";

        async Task<HttpStatusCode> SendAsync()
        {
            var req = new StringContent(payload, Encoding.UTF8, "application/json");
            req.Headers.Add("X-Hub-Signature-256", Sig(payload));
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var res = await _client.PostAsync("/api/inbox/github", req);
            return res.StatusCode;
        }

        // limit 2 per min (ref factory)
        var s1 = await SendAsync(); // 202
        var s2 = await SendAsync(); // 202
        var s3 = await SendAsync(); // 429

        s1.Should().Be(HttpStatusCode.Accepted);
        s2.Should().Be(HttpStatusCode.Accepted);
        s3.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

