using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Domain.Activity;

public sealed class WebhookDelivery : Entity
{
    public long WebhookId { get; set; }
    public Webhook Webhook { get; set; } = null!;
    public NotificationEvent Event { get; set; }
    public required string GameTitle { get; set; }
    public string? ReleaseTitle { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
}
