namespace Sense.Modules.Integrations.Domain;

/// <summary>Domain layer error codes (localized in the Web layer).</summary>
public static class IntegrationsDomainErrors
{
    public const string SubscriptionNotFound = "integrations.webhook.not_found";
    public const string UrlInvalid = "integrations.webhook.url_invalid";
    public const string EventTypesRequired = "integrations.webhook.event_types_required";
    public const string EventTypeUnknown = "integrations.webhook.event_type_unknown";
}
