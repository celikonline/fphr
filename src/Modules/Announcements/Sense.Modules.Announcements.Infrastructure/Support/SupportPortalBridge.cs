using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Infrastructure.Support;

public sealed class SupportPortalOptions
{
    public Uri? BaseUri { get; }
    public string? ApiKey { get; }
    public string Path { get; } = "";
    public bool Enabled => BaseUri is not null;

    public SupportPortalOptions(IConfiguration configuration)
    {
        var url = configuration["FP_SUPPORT_PORTAL_URL"];
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !Guid.TryParse(configuration["FP_SUPPORT_TENANT_ID"], out var tenant)
            || !Guid.TryParse(configuration["FP_SUPPORT_PROJECT_ID"], out var project)
            || string.IsNullOrWhiteSpace(configuration["FP_SUPPORT_API_KEY"]))
            throw new InvalidOperationException("Support portal server configuration is invalid.");
        BaseUri = uri;
        ApiKey = configuration["FP_SUPPORT_API_KEY"];
        Path = $"api/v1/tenants/{tenant:D}/projects/{project:D}/customer-tickets";
    }
}

public sealed class SupportPortalBridge(HttpClient client, SupportPortalOptions options) : ISupportPortalBridge
{
    public bool Enabled => options.Enabled;
    private static PortalSupportTicketDto State(string state) => new(state, []);
    public async Task<Guid> CreateAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(ticket, HttpMethod.Post, "", new
        {
            title = ticket.Subject, description = ticket.Body, sourceProduct = "fphr",
            externalReference = ticket.Id.ToString("D"), priority = "normal"
        }, ticket.Id, cancellationToken);
        if (!Guid.TryParse(json.RootElement.GetProperty("id").GetString(), out var id))
            throw new InvalidOperationException("Support portal delivery failed.");
        return id;
    }

    public async Task<PortalSupportTicketDto> ReadAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        if (!RemoteId(ticket, out var id)) return State("pending");
        try
        {
            using var json = await SendAsync(ticket, HttpMethod.Get, $"/{id:D}", null, null, cancellationToken);
            return Map(json.RootElement);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return State("unavailable"); }
    }

    public async Task<IReadOnlyDictionary<Guid, PortalSupportTicketDto>> ListAsync(IReadOnlyList<SupportTicket> tickets, CancellationToken cancellationToken)
    {
        var result = tickets.ToDictionary(t => t.Id, t => State(RemoteId(t, out _) ? "unavailable" : "pending"));
        // One paged subject query, never an HTTP request per local record. Bound requests during outages/large histories.
        var linked = tickets.Where(t => RemoteId(t, out _)).ToDictionary(t => t.ExternalRef![11..], t => t.Id);
        if (linked.Count == 0) return result;
        try
        {
            for (var page = 1; page <= 20; page++)
            {
                using var json = await SendAsync(tickets[0], HttpMethod.Get, $"?page={page}&pageSize=20&includeComments=1", null, null, cancellationToken);
                foreach (var item in json.RootElement.GetProperty("results").EnumerateArray())
                {
                    if (linked.Remove(item.GetProperty("id").GetString()!, out var localId)) result[localId] = Map(item);
                }
                if (linked.Count == 0 || page * 20 >= json.RootElement.GetProperty("total").GetInt32()) break;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { /* Local records remain visible. */ }
        return result;
    }

    public async Task AddCommentAsync(SupportTicket ticket, string body, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        if (!RemoteId(ticket, out var id)) throw new InvalidOperationException("Support portal ticket is pending.");
        using var json = await SendAsync(ticket, HttpMethod.Post, $"/{id:D}/comments", new { body }, idempotencyKey, cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(SupportTicket ticket, HttpMethod method, string suffix, object? body, Guid? key, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(options.BaseUri!, options.Path + suffix));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Add("X-Support-Subject", $"tenant:{ticket.TenantId:D}:user:{ticket.RequestedByUserId:D}");
            if (key is { } value) request.Headers.Add("Idempotency-Key", value.ToString("D"));
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Support portal request failed.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Outbox logs exception text: never retain URL, headers, response or customer content in an inner exception.
            throw new InvalidOperationException("Support portal request failed.");
        }
    }

    private static bool RemoteId(SupportTicket ticket, out Guid id)
    {
        id = default;
        return ticket.ExternalRef?.StartsWith("fp-support:", StringComparison.Ordinal) == true
            && Guid.TryParse(ticket.ExternalRef[11..], out id);
    }
    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static PortalSupportTicketDto Map(JsonElement item)
    {
        var comments = new List<PortalSupportCommentDto>();
        if (item.TryGetProperty("comments", out var list))
            foreach (var comment in list.EnumerateArray())
            {
                if (Text(comment, "visibility") != "public") continue;
                var author = comment.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? Text(a, "name") : null;
                comments.Add(new(Text(comment, "id")!, Text(comment, "body")!,
                    DateTime.Parse(Text(comment, "createdAt")!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), author, Text(comment, "source")));
            }
        bool? truncated = item.TryGetProperty("commentsTruncated", out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        return new("connected", comments, Text(item, "id"), Text(item, "number"), Text(item, "status"), truncated);
    }
}
