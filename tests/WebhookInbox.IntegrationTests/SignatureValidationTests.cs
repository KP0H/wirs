using FluentAssertions;
using System.Net;
using System.Text;
using WebhookInbox.IntegrationTests.Factories;

namespace WebhookInbox.IntegrationTests;

public class SignatureValidationTests : IClassFixture<SignatureApiFactory>
{
    private readonly SignatureApiFactory _factory;
    public SignatureValidationTests(SignatureApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Github_Valid_Signature_Should_Accept_202()
    {
        var secret = "gh_test_secret";
        var payload = "{\"ok\":true}";
        var body = Encoding.UTF8.GetBytes(payload);
        var sig = "sha256=" + ToHex(HmacSha256(secret, body));

        var client = _factory.CreateClient();
        var req = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.Add("X-Hub-Signature-256", sig);

        var res = await client.PostAsync("/api/inbox/github", req);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Github_Bad_Signature_Should_401()
    {
        var client = _factory.CreateClient();
        var req = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json");
        req.Headers.Add("X-Hub-Signature-256", "sha256=badsignature");
        var res = await client.PostAsync("/api/inbox/github", req);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Stripe_Valid_Signature_Should_Accept_202()
    {
        var secret = "stripe_test_secret";
        var payload = "{\"x\":1}";
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedPayload = $"{t}.{payload}";
        var v1 = ToHex(HmacSha256(secret, Encoding.UTF8.GetBytes(signedPayload)));

        var client = _factory.CreateClient();
        var req = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.Add("Stripe-Signature", $"t={t},v1={v1}");

        var res = await client.PostAsync("/api/inbox/stripe", req);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Stripe_Old_Timestamp_Should_401()
    {
        var secret = "stripe_test_secret";
        var payload = "{}";
        var t = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10_000).ToString(); // too old
        var signedPayload = $"{t}.{payload}";
        var v1 = ToHex(HmacSha256(secret, Encoding.UTF8.GetBytes(signedPayload)));

        var client = _factory.CreateClient();
        var req = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.Add("Stripe-Signature", $"t={t},v1={v1}");

        var res = await client.PostAsync("/api/inbox/stripe", req);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static byte[] HmacSha256(string secret, byte[] data)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return h.ComputeHash(data);
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
