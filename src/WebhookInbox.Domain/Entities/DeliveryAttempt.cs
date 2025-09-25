namespace WebhookInbox.Domain.Entities;

public class DeliveryAttempt
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid EndpointId { get; set; }

    public int Try { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public int? ResponseCode { get; set; }
    public string? ResponseBody { get; set; }
    public bool Success { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    // Navigation
    public Event Event { get; set; } = default!;
    public Endpoint Endpoint { get; set; } = default!;
}
