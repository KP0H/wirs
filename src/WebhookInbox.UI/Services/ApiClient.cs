using System.Net.Http.Json;

namespace WebhookInbox.UI.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<EventListResponse> GetEventsAsync(int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/events?page={page}&pageSize={pageSize}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EventListResponse>(cancellationToken: cancellationToken);
        return payload ?? new EventListResponse(Array.Empty<EventListItem>(), 0, page, pageSize);
    }

    public async Task<EventDetail?> GetEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/events/{id}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventDetail>(cancellationToken: cancellationToken);
    }
}

public record EventListResponse(IReadOnlyList<EventListItem> Items, int Total, int Page, int PageSize);

public record EventListItem(Guid Id, string Source, string Status, string SignatureStatus, DateTimeOffset ReceivedAt);

public record EventDetail(Guid Id, string Source, string Status, string SignatureStatus, DateTimeOffset ReceivedAt, IReadOnlyDictionary<string, string[]> Headers, string Payload, bool PayloadIsJson, IReadOnlyList<DeliveryAttempt> Attempts);

public record DeliveryAttempt(Guid Id, Guid EndpointId, string? EndpointUrl, int Try, DateTimeOffset SentAt, int? ResponseCode, bool Success, string? ResponseBody, DateTimeOffset? NextAttemptAt);
