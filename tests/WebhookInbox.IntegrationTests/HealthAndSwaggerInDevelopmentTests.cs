using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Text.Json;

namespace WebhookInbox.IntegrationTests;

public class HealthAndSwaggerInDevelopmentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public HealthAndSwaggerInDevelopmentTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b => b.UseSetting("ENVIRONMENT", "Development")).CreateClient();
    }

    [Fact]
    public async Task Healthz_Should_Return_Live()
    {
        var res = await _client.GetAsync("/healthz");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("status").GetString().Should().Be("live");
    }

    [Fact]
    public async Task Ready_Should_Return_200()
    {
        var res = await _client.GetAsync("/ready");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Version_Should_Return_Version_Field()
    {
        var res = await _client.GetAsync("/api/version");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        json.TryGetProperty("version", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerJson_Should_Return_200_In_Development()
    {
        var res = await _client.GetAsync("/swagger/v1/swagger.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}