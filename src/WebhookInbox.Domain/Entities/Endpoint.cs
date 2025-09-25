namespace WebhookInbox.Domain.Entities;

public class Endpoint
{
    public Guid Id { get; set; }
    public string Url { get; set; } = default!;
    public string? Secret { get; set; }
    public bool IsActive { get; set; } = true;
    public int? RateLimitPerMinute { get; set; }
    public string? PolicyJson { get; set; }

    // Navigation
    public ICollection<DeliveryAttempt> Attempts { get; set; } = new List<DeliveryAttempt>();
}
