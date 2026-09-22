namespace Zetruv.Api.Features.Payments;

public enum PaymentWebhookEventOutcome
{
    Received,
    Applied,
    Rejected
}

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string ProviderReference { get; set; } = string.Empty;
    public string EventFingerprintSha256 { get; set; } = string.Empty;
    public PaymentWebhookStatus WebhookStatus { get; set; }
    public PaymentWebhookEventOutcome Outcome { get; set; } = PaymentWebhookEventOutcome.Received;
    public int DeliveryCount { get; set; } = 1;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultMessage { get; set; }
    public bool IsConfigurationError { get; set; }
    public bool IsNotFound { get; set; }
    public bool IsConflict { get; set; }
}
