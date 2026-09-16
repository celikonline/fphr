using NSubstitute;
using Sense.Modules.Integrations.Application;
using Sense.Modules.Integrations.Application.Webhooks;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Domain.Webhooks;
using Shouldly;
using Xunit;

namespace Sense.Modules.Integrations.Tests;

public sealed class WebhookDispatchServiceTests
{
    private readonly IWebhookSubscriptionRepository _subscriptions = Substitute.For<IWebhookSubscriptionRepository>();
    private readonly IWebhookDeliveryLogRepository _deliveries = Substitute.For<IWebhookDeliveryLogRepository>();
    private readonly IWebhookSender _sender = Substitute.For<IWebhookSender>();
    private readonly IIntegrationsUnitOfWork _unitOfWork = Substitute.For<IIntegrationsUnitOfWork>();
    private readonly WebhookDispatchService _sut;

    public WebhookDispatchServiceTests()
    {
        _sut = new WebhookDispatchService(_subscriptions, _deliveries, _sender, _unitOfWork);
    }

    [Fact]
    public async Task DispatchAsync_WithNoSubscribers_DoesNotSendOrSave()
    {
        _subscriptions.ListSubscribedToAsync(WebhookEventTypes.EmployeeHired, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.DispatchAsync(WebhookEventTypes.EmployeeHired, new { EmployeeId = Guid.NewGuid() }, CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task DispatchAsync_WithOneSubscriber_SendsAndLogsThenSaves()
    {
        var subscription = WebhookSubscription.Create(Guid.NewGuid(), "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]).Value;
        _subscriptions.ListSubscribedToAsync(WebhookEventTypes.EmployeeHired, Arg.Any<CancellationToken>())
            .Returns([subscription]);
        _sender.SendAsync(subscription, WebhookEventTypes.EmployeeHired, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookDeliveryResult(true, 200, null));

        await _sut.DispatchAsync(WebhookEventTypes.EmployeeHired, new { EmployeeId = Guid.NewGuid() }, CancellationToken.None);

        _deliveries.Received(1).Add(Arg.Is<WebhookDeliveryLog>(d => d.Success && d.StatusCode == 200));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenSenderReportsFailure_LogsFailureAndStillSaves()
    {
        var subscription = WebhookSubscription.Create(Guid.NewGuid(), "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]).Value;
        _subscriptions.ListSubscribedToAsync(WebhookEventTypes.EmployeeHired, Arg.Any<CancellationToken>())
            .Returns([subscription]);
        _sender.SendAsync(subscription, WebhookEventTypes.EmployeeHired, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookDeliveryResult(false, 500, "HTTP 500"));

        await _sut.DispatchAsync(WebhookEventTypes.EmployeeHired, new { EmployeeId = Guid.NewGuid() }, CancellationToken.None);

        _deliveries.Received(1).Add(Arg.Is<WebhookDeliveryLog>(d => !d.Success && d.Error == "HTTP 500"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
