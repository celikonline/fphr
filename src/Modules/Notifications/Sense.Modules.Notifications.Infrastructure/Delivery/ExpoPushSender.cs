using System.Text;
using System.Text.Json;
using Sense.Modules.Notifications.Application;

namespace Sense.Modules.Notifications.Infrastructure.Delivery;

/// <summary>
/// Expo Push API client (POST https://exp.host/--/api/v2/push/send, batched, one HTTP call per SendAsync). Only
/// ticket-level errors are handled here; the separate "receipt" endpoint (actual device delivery confirmation) is
/// out of scope for this phase (push-bildirimleri-tasarim.md §3.5).
/// </summary>
public sealed class ExpoPushSender(HttpClient httpClient) : IExpoPushSender
{
    private const string SendPath = "--/api/v2/push/send";
    private const string ErrorStatus = "error";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ExpoPushTicket>> SendAsync(IReadOnlyList<ExpoPushMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var requestBody = messages.Select(m => new ExpoPushRequestMessage(m.Token, m.Title, m.Body, m.Data, "high")).ToList();
        using var content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(SendPath, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync<ExpoPushResponseEnvelope>(responseStream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? new ExpoPushResponseEnvelope([]);

        var tickets = new List<ExpoPushTicket>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var ticket = i < envelope.Data.Count ? envelope.Data[i] : null;
            var isError = ticket is null || string.Equals(ticket.Status, ErrorStatus, StringComparison.Ordinal);
            tickets.Add(new ExpoPushTicket(messages[i].Token, isError, ticket?.Details?.Error));
        }

        return tickets;
    }

    private sealed record ExpoPushRequestMessage(string To, string Title, string Body, IReadOnlyDictionary<string, string?> Data, string Priority);

    private sealed record ExpoPushResponseEnvelope(IReadOnlyList<ExpoPushResponseTicket> Data);

    private sealed record ExpoPushResponseTicket(string Status, string? Message, ExpoPushResponseTicketDetails? Details);

    private sealed record ExpoPushResponseTicketDetails(string? Error);
}
