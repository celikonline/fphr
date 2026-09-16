using Sense.Modules.Integrations.Domain.Webhooks;
using Shouldly;
using Xunit;

namespace Sense.Modules.Integrations.Tests;

public sealed class WebhookSubscriptionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithHttpsUrlAndKnownEventTypes_Succeeds()
    {
        var result = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsActive.ShouldBeTrue();
        result.Value.EventTypes.ShouldBe([WebhookEventTypes.EmployeeHired]);
        result.Value.Secret.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("http://payroll.example.com/hooks")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Create_WithNonHttpsUrl_Fails(string url)
    {
        var result = WebhookSubscription.Create(TenantId, url, [WebhookEventTypes.EmployeeHired]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithNoEventTypes_Fails()
    {
        var result = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", []);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithUnknownEventType_Fails()
    {
        var result = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", ["employee.unknown"]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Subscribes_WhenInactive_IsFalseEvenForASubscribedType()
    {
        var subscription = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]).Value;
        subscription.Deactivate();

        subscription.Subscribes(WebhookEventTypes.EmployeeHired).ShouldBeFalse();
    }

    [Fact]
    public void Subscribes_WhenActiveAndTypeNotIncluded_IsFalse()
    {
        var subscription = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]).Value;

        subscription.Subscribes(WebhookEventTypes.EmployeeTerminated).ShouldBeFalse();
    }

    [Fact]
    public void Reactivate_AfterDeactivate_RestoresSubscribes()
    {
        var subscription = WebhookSubscription.Create(TenantId, "https://payroll.example.com/hooks", [WebhookEventTypes.EmployeeHired]).Value;
        subscription.Deactivate();
        subscription.Reactivate();

        subscription.Subscribes(WebhookEventTypes.EmployeeHired).ShouldBeTrue();
    }
}
