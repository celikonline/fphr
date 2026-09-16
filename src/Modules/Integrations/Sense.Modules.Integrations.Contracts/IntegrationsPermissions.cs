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
    private const string Group = "Integrations";

    public const string WebhooksManageKey = "integrations.webhooks.manage";
    public const string WebhooksViewKey = "integrations.webhooks.view";
    public const string WebhooksDeliveriesKey = "integrations.webhooks.deliveries";

    public static readonly Permission WebhooksManage = new(
        WebhooksManageKey, Module, "Manage Webhooks", Group, "Register, deactivate and reactivate outbound webhook subscriptions");

    public static readonly Permission WebhooksView = new(
        WebhooksViewKey, Module, "View Webhooks", Group, "View outbound webhook subscriptions");

    public static readonly Permission WebhooksDeliveries = new(
        WebhooksDeliveriesKey, Module, "View Webhook Deliveries", Group, "View the outbound webhook delivery log");

    public static IEnumerable<Permission> All => [WebhooksManage, WebhooksView, WebhooksDeliveries];
}
