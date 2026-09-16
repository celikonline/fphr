using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sense.Modules.Integrations.Application;
using Sense.Modules.Integrations.Domain.Webhooks;

namespace Sense.Modules.Integrations.Infrastructure.Webhooks;

internal static class WebhookHttpClientNames
{
    public const string Default = "integrations.webhooks";
}

/// <summary>
/// Sends one signed HTTP POST per delivery. Never throws - a subscriber being down or misconfigured must not fail
/// the originating module's outbox message (WebhookDispatchService relies on this).
/// </summary>
public sealed partial class HttpWebhookSender(IHttpClientFactory httpClientFactory, ILogger<HttpWebhookSender> logger) : IWebhookSender
{
    private const string SignatureHeader = "X-Sense-Signature";
    private const string EventTypeHeader = "X-Sense-Event";
    private const int MaxErrorLength = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookDeliveryResult> SendAsync(WebhookSubscription subscription, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var client = httpClientFactory.CreateClient(WebhookHttpClientNames.Default);
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(EventTypeHeader, eventType);
            request.Headers.Add(SignatureHeader, Sign(subscription.Secret, json));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new WebhookDeliveryResult(true, (int)response.StatusCode, null)
                : new WebhookDeliveryResult(false, (int)response.StatusCode, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.DeliveryFailed(logger, ex, subscription.Id, eventType);
            var message = ex.Message;
            return new WebhookDeliveryResult(false, null, message.Length > MaxErrorLength ? message[..MaxErrorLength] : message);
        }
    }

    private static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Webhook delivery failed for subscription {SubscriptionId} ({EventType})")]
        public static partial void DeliveryFailed(ILogger logger, Exception exception, Guid subscriptionId, string eventType);
    }
}
