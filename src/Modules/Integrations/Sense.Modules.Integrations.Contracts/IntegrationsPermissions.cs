using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Integrations.Contracts;

/// <summary>
/// Integrations module permissions. The keys themselves predate this module - "integrations.*.view",
/// "integrations.webhooks.deliveries" (Auditor) and "integrations.*" (ITAdmin) were already reserved in
/// SystemRoleDefinitions.cs; this defines the actual catalog entries they now resolve to.
/// </summary>
public static class IntegrationsPermissions
{
    private const string Module = "integrations";
    private const string Group = "Entegrasyonlar";

    public const string WebhooksManageKey = "integrations.webhooks.manage";
    public const string WebhooksViewKey = "integrations.webhooks.view";
    public const string WebhooksDeliveriesKey = "integrations.webhooks.deliveries";

    public static readonly Permission WebhooksManage = new(
        WebhooksManageKey, Module, "Webhook Yönetimi", Group, "Giden webhook aboneliklerini kaydetme, pasifleştirme ve yeniden etkinleştirme");

    public static readonly Permission WebhooksView = new(
        WebhooksViewKey, Module, "Webhookları Görüntüleme", Group, "Giden webhook aboneliklerini görüntüleme");

    public static readonly Permission WebhooksDeliveries = new(
        WebhooksDeliveriesKey, Module, "Webhook Teslimatlarını Görüntüleme", Group, "Giden webhook teslimat günlüğünü görüntüleme");

    public static IEnumerable<Permission> All => [WebhooksManage, WebhooksView, WebhooksDeliveries];
}
