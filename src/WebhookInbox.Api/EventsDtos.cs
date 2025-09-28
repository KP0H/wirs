namespace WebhookInbox.Api;

public record EventListResponse(IReadOnlyList<EventListItemDto> Items, int Total, int Page, int PageSize);

public record EventListItemDto(Guid Id, string Source, string Status, string SignatureStatus, DateTimeOffset ReceivedAt);

public record EventDetailDto(
    Guid Id,
    string Source,
    string Status,
    string SignatureStatus,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string[]> Headers,
    string Payload,
    bool PayloadIsJson,
    IReadOnlyList<DeliveryAttemptDto> Attempts);

public record DeliveryAttemptDto(
    Guid Id,
    Guid EndpointId,
    string? EndpointUrl,
    int Try,
    DateTimeOffset SentAt,
    int? ResponseCode,
    bool Success,
    string? ResponseBody,
    DateTimeOffset? NextAttemptAt);
