using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Contracts.Events;

/// <summary>
/// Published by the periodic expiry-reminder job (<c>JobType.DocumentExpiryReminder</c>) when a tenant has documents
/// expiring soon and/or already expired (see <c>GET /api/v1/documents/expiring</c>, same counts). No Notifications
/// consumer exists yet - today's <c>NotificationRecipientResolver</c> resolves by explicit employee id, not by
/// permission, and "everyone with documents.requirements.manage" fan-out is a capability that doesn't exist yet
/// (faz4 rapor). This event is the durable, tenant-scoped signal Notifications can subscribe to once that lands.
/// </summary>
public sealed record DocumentExpiryReminderDue(Guid TenantId, int ExpiringCount, int ExpiredCount) : IntegrationEvent(TenantId);
