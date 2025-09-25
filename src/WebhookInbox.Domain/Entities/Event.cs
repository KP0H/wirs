using System.Text.Json;

namespace WebhookInbox.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public string Source { get; set; } = default!;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    // For headers keep JSON to preserve original casing/multiplicity if needed
    public JsonDocument Headers { get; set; } = JsonDocument.Parse("{}");

    // Raw payload to support any content type (JSON/binary)
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public SignatureStatus SignatureStatus { get; set; } = SignatureStatus.None;
    public EventStatus Status { get; set; } = EventStatus.New;

    // Navigation
    public ICollection<DeliveryAttempt> Attempts { get; set; } = new List<DeliveryAttempt>();
}
