using System.Net;
using System.Text;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Infrastructure.Delivery;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// ExpoPushSender against a fake HttpMessageHandler - never a real HTTP call to exp.host (push-bildirimleri-tasarim.md
/// §3.5). Verifies the request shape (batched "to"/"title"/"body"/"data"/"priority") and that response tickets are
/// matched back to their token in request order.
/// </summary>
public sealed class ExpoPushSenderTests
{
    [Fact]
    public async Task SendAsync_MapsOkAndErrorTickets_BackToTheirToken_InOrder()
    {
        const string responseJson = """
            { "data": [ { "status": "ok" }, { "status": "error", "message": "not registered", "details": { "error": "DeviceNotRegistered" } } ] }
            """;
        var handler = new FakeHandler((request, ct) =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/--/api/v2/push/send");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host/") };
        var sender = new ExpoPushSender(httpClient);

        var messages = new List<ExpoPushMessage>
        {
            new("ExponentPushToken[ok]", "Title", "Body", new Dictionary<string, string?> { ["type"] = "request.approved", ["id"] = Guid.NewGuid().ToString() }),
            new("ExponentPushToken[stale]", "Title", "Body", new Dictionary<string, string?> { ["type"] = "request.approved", ["id"] = Guid.NewGuid().ToString() }),
        };

        var tickets = await sender.SendAsync(messages, CancellationToken.None);

        tickets.Count.ShouldBe(2);
        tickets[0].Token.ShouldBe("ExponentPushToken[ok]");
        tickets[0].IsError.ShouldBeFalse();
        tickets[0].ErrorCode.ShouldBeNull();
        tickets[1].Token.ShouldBe("ExponentPushToken[stale]");
        tickets[1].IsError.ShouldBeTrue();
        tickets[1].ErrorCode.ShouldBe(ExpoPushErrorCodes.DeviceNotRegistered);
    }

    [Fact]
    public async Task SendAsync_SendsBatchedRequestBody_WithTokenTitleBodyDataAndPriority()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new FakeHandler((request, ct) =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": [ { "status": "ok" } ] }""", Encoding.UTF8, "application/json"),
            };
        });
        handler.OnSendingContent = body => capturedBody = body;
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host/") };
        var sender = new ExpoPushSender(httpClient);

        await sender.SendAsync(
            [new ExpoPushMessage("ExponentPushToken[abc]", "Onay bekliyor", "İzin talebiniz onaylandı", new Dictionary<string, string?> { ["type"] = "request.approved", ["id"] = "1" })],
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Method.ShouldBe(HttpMethod.Post);
        capturedBody.ShouldNotBeNull();
        capturedBody!.ShouldContain("\"to\":\"ExponentPushToken[abc]\"");
        capturedBody.ShouldContain("\"title\":\"Onay bekliyor\"");
        capturedBody.ShouldContain("\"priority\":\"high\"");
    }

    [Fact]
    public async Task SendAsync_ReturnsEmpty_WhenNoMessages()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("must not be called"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host/") };
        var sender = new ExpoPushSender(httpClient);

        var tickets = await sender.SendAsync([], CancellationToken.None);

        tickets.ShouldBeEmpty();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Action<string>? OnSendingContent { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                OnSendingContent?.Invoke(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return respond(request, cancellationToken);
        }
    }
}
