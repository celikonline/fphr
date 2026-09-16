using System.Security.Cryptography;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Integrations.Domain.Webhooks;

/// <summary>
/// A tenant's outbound webhook endpoint (Faz 8 #61). <see cref="Secret"/> signs each delivery (HMAC-SHA256) and is
/// generated once at creation - the API returns it only in the create response, never again afterwards.
/// </summary>
public sealed class WebhookSubscription : TenantAggregateRoot<Guid>, IAuditLogged
{
    private WebhookSubscription()
    {
    }

    private WebhookSubscription(Guid id, Guid tenantId, string url, string secret, IReadOnlyList<string> eventTypes)
        : base(id, tenantId)
    {
        Url = url;
        Secret = secret;
        EventTypes = eventTypes;
        IsActive = true;
    }

    public string Url { get; private set; } = string.Empty;

    public string Secret { get; private set; } = string.Empty;

    public IReadOnlyList<string> EventTypes { get; private set; } = [];

    public bool IsActive { get; private set; }

    static IReadOnlySet<string> IAuditLogged.SensitiveFields => new HashSet<string>(StringComparer.Ordinal) { nameof(Secret) };

    public static Result<WebhookSubscription> Create(Guid tenantId, string url, IReadOnlyCollection<string> eventTypes, Guid? id = null)
    {
        if (!IsHttpsUrl(url))
        {
            return Error.Validation(IntegrationsDomainErrors.UrlInvalid);
        }

        var distinctTypes = eventTypes.Distinct(StringComparer.Ordinal).ToList();
        if (distinctTypes.Count == 0)
        {
            return Error.Validation(IntegrationsDomainErrors.EventTypesRequired);
        }

        if (distinctTypes.Except(WebhookEventTypes.All, StringComparer.Ordinal).Any())
        {
            return Error.Validation(IntegrationsDomainErrors.EventTypeUnknown);
        }

        return new WebhookSubscription(
            id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(url.Trim(), IntegrationsLimits.UrlMaxLength), GenerateSecret(), distinctTypes);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public bool Subscribes(string eventType) => IsActive && EventTypes.Contains(eventType, StringComparer.Ordinal);

    private static bool IsHttpsUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
