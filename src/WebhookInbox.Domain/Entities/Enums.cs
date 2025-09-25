namespace WebhookInbox.Domain.Entities;

public enum SignatureStatus
{
    None = 0,
    Verified = 1,
    Failed = 2
}

public enum EventStatus
{
    New = 0,
    Dispatched = 1,
    Failed = 2,
    DeadLetter = 3
}