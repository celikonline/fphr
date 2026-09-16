using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Integrations.Domain.Webhooks;

/// <summary>
/// One outbound delivery attempt (append-only; read by Auditor's "integrations.webhooks.deliveries"). No retry yet -
/// a failed delivery is recorded and left for a future sweep/redelivery feature.
/// </summary>
public sealed class WebhookDeliveryLog : TenantEntity<Guid>
{
    private WebhookDeliveryLog()
    {
    }

    private WebhookDeliveryLog(Guid id, Guid tenantId, Guid subscriptionId, string eventType, bool success, int? statusCode, string? error)
        : base(id, tenantId)
    {
        SubscriptionId = subscriptionId;
        EventType = eventType;
        Success = success;
        StatusCode = statusCode;
        Error = error;
    }

    public Guid SubscriptionId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public int? StatusCode { get; private set; }

    public string? Error { get; private set; }

    public static WebhookDeliveryLog Record(Guid tenantId, Guid subscriptionId, string eventType, bool success, int? statusCode, string? error) =>
        new(
            Guid.CreateVersion7(), tenantId, subscriptionId, eventType, success, statusCode,
            error is { Length: > IntegrationsLimits.DeliveryErrorMaxLength } ? error[..IntegrationsLimits.DeliveryErrorMaxLength] : error);
}
