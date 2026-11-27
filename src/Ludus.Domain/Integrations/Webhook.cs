using Ludus.Domain.Common;

namespace Ludus.Domain.Integrations;

public sealed class Webhook : Entity
{
    public required string Name { get; set; }
    public WebhookKind Kind { get; set; } = WebhookKind.Discord;
    public required ProtectedSecret Url { get; set; }
    public ProtectedSecret? Authorization { get; set; }
    public ProtectedSecret? Token { get; set; }
    public string? Target { get; set; }
    public bool Enabled { get; set; } = true;
    public List<NotificationEvent> Events { get; set; } =
        [NotificationEvent.Grabbed, NotificationEvent.Imported, NotificationEvent.Upgraded, NotificationEvent.DownloadFailed];
}
