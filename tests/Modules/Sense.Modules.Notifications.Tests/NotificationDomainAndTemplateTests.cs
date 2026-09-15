using System.Globalization;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Modules.Notifications.Infrastructure.Messaging;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

public sealed class NotificationDomainAndTemplateTests
{
    private static readonly Dictionary<string, string> LeaveArgs = new()
    {
        [NotificationTemplateArgs.RequestType] = "Leave",
        [NotificationTemplateArgs.RequesterName] = "Ayşe Yılmaz",
    };

    [Fact]
    public void Create_RequiresRecipient()
    {
        var result = Notification.Create(Guid.NewGuid(), Guid.Empty, NotificationTemplateKeys.RequestApproved, NotificationCategory.Request, null, null, DateTime.UtcNow);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(NotificationsDomainErrors.RecipientRequired);
    }

    [Fact]
    public void MarkRead_IsIdempotent_KeepsFirstTimestamp()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), NotificationTemplateKeys.RequestApproved, NotificationCategory.Request, "{}", "/app/requests", DateTime.UtcNow).Value;
        var first = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
        n.MarkRead(first);
        n.MarkRead(first.AddHours(1));
        n.ReadAt.ShouldBe(first);
        n.IsRead.ShouldBeTrue();
    }

    [Fact]
    public void EveryCatalogTemplate_HasTitleAndBody_InTrEnDe()
    {
        var renderer = new ResourceNotificationTemplateRenderer();
        foreach (var key in NotificationTemplateKeys.All)
        {
            NotificationTemplateCatalog.Find(key).ShouldNotBeNull();
            foreach (var culture in new[] { "tr-TR", "en-US", "de-DE" })
            {
                var rendered = renderer.Render(key, LeaveArgs, CultureInfo.GetCultureInfo(culture));
                rendered.Title.ShouldNotBe(key, $"{key} title missing for {culture}");
                rendered.Body.ShouldNotBeNullOrWhiteSpace();
                rendered.Body.ShouldNotContain("{");
            }
        }
    }

    [Fact]
    public void Render_LocalizesRequestTypeAndSubstitutesArgs()
    {
        var renderer = new ResourceNotificationTemplateRenderer();

        var tr = renderer.Render(NotificationTemplateKeys.ApprovalTaskAssigned, LeaveArgs, CultureInfo.GetCultureInfo("tr-TR"));
        tr.Title.ShouldBe("Onayınız bekleniyor");
        tr.Body.ShouldBe("Ayşe Yılmaz tarafından oluşturulan İzin talebi onayınızı bekliyor.");

        var en = renderer.Render(NotificationTemplateKeys.RequestApproved, LeaveArgs, CultureInfo.GetCultureInfo("en-US"));
        en.Body.ShouldBe("Your leave request was approved.");

        var de = renderer.Render(NotificationTemplateKeys.RequestRejected, LeaveArgs, CultureInfo.GetCultureInfo("de-DE"));
        de.Title.ShouldStartWith("[DE]");
    }
}
