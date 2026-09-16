namespace Sense.Modules.Integrations.Application.Webhooks;

/// <summary>Read model for a subscription. Never carries the secret - it is only ever returned once, at creation.</summary>
public sealed record WebhookSubscriptionDto(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive);

/// <summary>Create response: the only time the caller sees <paramref name="Secret"/>.</summary>
public sealed record WebhookSubscriptionCreatedDto(Guid Id, string Secret);

public sealed record WebhookDeliveryDto(Guid Id, Guid SubscriptionId, string EventType, bool Success, int? StatusCode, string? Error, DateTime AttemptedAt);
