using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Sense.Shared.Contracts.Notifications;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>employee.*, announcement.published and payroll.payslip_ready: template texts and send-time PDF attachments.</summary>
public sealed class EmployeeAnnouncementPayrollNotificationTests
{
    private static readonly ScribanNotificationTemplateRenderer Renderer = new(
        Options.Create(new NotificationsOptions()),
        Options.Create(new NotificationsFrontendOptions { BaseUrl = "https://app.senseik.test" }));

    [Fact]
    public void AnnouncementPublished_HighPriority_MarksTheSubject_AndLinksToAnnouncements()
    {
        var model = Model(
            (AnnouncementPayrollTemplateModel.AnnouncementTitle, "Yemekhane kapalı"),
            (AnnouncementPayrollTemplateModel.AnnouncementExcerpt, "Yarın <tadilat> var"),
            (AnnouncementPayrollTemplateModel.IsHighPriority, "true"),
            (NotificationPublisher.ActionPathKey, AnnouncementPayrollTemplateModel.AnnouncementsPath));

        var rendered = Renderer.Render(NotificationTemplateKeys.AnnouncementPublished, NotificationChannels.Email, "tr", model);

        rendered.Subject.ShouldBe("[Önemli] Yeni duyuru: Yemekhane kapalı");
        rendered.HtmlBody.ShouldContain("Yarın &lt;tadilat&gt; var");
        rendered.HtmlBody.ShouldContain("href=\"https://app.senseik.test/app/announcements\"");
    }

    [Fact]
    public void AnnouncementPublished_NormalPriority_HasPlainSubject()
    {
        var model = Model(
            (AnnouncementPayrollTemplateModel.AnnouncementTitle, "Piknik"),
            (AnnouncementPayrollTemplateModel.IsHighPriority, "false"));

        Renderer.Render(NotificationTemplateKeys.AnnouncementPublished, NotificationChannels.Email, "tr", model)
            .Subject.ShouldBe("Yeni duyuru: Piknik");
    }

    [Fact]
    public void PayslipReady_And_WorkCertificate_And_Birthday_RenderTheirVariables()
    {
        Renderer.Render(NotificationTemplateKeys.PayslipReady, NotificationChannels.Email, "tr", Model((AnnouncementPayrollTemplateModel.Period, "2026-09")))
            .Subject.ShouldBe("2026-09 dönemi ücret pusulanız");

        Renderer.Render(NotificationTemplateKeys.EmployeeWorkCertificate, NotificationChannels.Email, "tr", Model((EmployeeTemplateModel.TerminationDate, "30.09.2026")))
            .Body.ShouldContain("30.09.2026");

        Renderer.Render(NotificationTemplateKeys.EmployeeBirthday, NotificationChannels.Email, "tr", Model())
            .Subject.ShouldBe("İyi ki doğdunuz, Mehmet Demir!");

        Renderer.Render(NotificationTemplateKeys.EmployeeHired, NotificationChannels.InApp, "tr",
                Model((EmployeeTemplateModel.EmployeeName, "Ayşe Kaya"), (EmployeeTemplateModel.HireDate, "01.10.2026")))
            .Body.ShouldBe("Ayşe Kaya 01.10.2026 tarihinde işe başlıyor.");
    }

    [Theory]
    [InlineData(-31, false)]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(45, true)]
    public void HireAndExitNotices_SkipDatesOlderThanTheWindow(int daysFromToday, bool expected)
    {
        var today = new DateOnly(2026, 9, 22);

        EmployeeTemplateModel.IsRecent(today.AddDays(daysFromToday), today).ShouldBe(expected);
    }

    [Fact]
    public async Task EmailDeliverySender_AttachesTheProvidersFile()
    {
        var sender = new RecordingEmailSender();
        var provider = Substitute.For<IEmailAttachmentProvider>();
        provider.TemplateKey.Returns(NotificationTemplateKeys.PayslipReady);
        var pdf = new EmailAttachment("ucret-pusulasi.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);
        provider.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(EmailAttachmentResult.With(pdf));
        var delivery = Delivery(NotificationTemplateKeys.PayslipReady, (AnnouncementPayrollTemplateModel.Period, "2026-09"));

        await DeliverySender(sender, provider).SendAsync(delivery, CancellationToken.None);

        delivery.Status.ShouldBe(NotificationDeliveryStatus.Sent);
        sender.Sent.ShouldHaveSingleItem().Attachments.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(pdf);
    }

    [Fact]
    public async Task EmailDeliverySender_SkipsWhenTheAttachmentIsGone()
    {
        var sender = new RecordingEmailSender();
        var provider = Substitute.For<IEmailAttachmentProvider>();
        provider.TemplateKey.Returns(NotificationTemplateKeys.EmployeeWorkCertificate);
        provider.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(EmailAttachmentResult.SkipWith(WorkCertificateAttachmentProvider.NotTerminated));
        var delivery = Delivery(NotificationTemplateKeys.EmployeeWorkCertificate);

        await DeliverySender(sender, provider).SendAsync(delivery, CancellationToken.None);

        delivery.Status.ShouldBe(NotificationDeliveryStatus.Skipped);
        delivery.LastError.ShouldBe(WorkCertificateAttachmentProvider.NotTerminated);
        sender.Sent.ShouldBeEmpty();
    }

    private static EmailDeliverySender DeliverySender(IEmailSender sender, IEmailAttachmentProvider provider) =>
        new([], [provider], Renderer, sender, Options.Create(new NotificationsOptions()), TimeProvider.System);

    private static NotificationDelivery Delivery(string templateKey, params (string Key, string Value)[] values)
    {
        var model = values.ToDictionary(v => v.Key, v => (string?)v.Value);
        model[NotificationPublisher.RecipientNameKey] = "Mehmet Demir";
        return new NotificationDelivery(
            Guid.NewGuid(), Guid.NewGuid(), templateKey, NotificationChannels.Email, Guid.NewGuid(), null,
            "mehmet@example.test", "Mehmet Demir", AddressSources.PersonalEmail, "tr", JsonSerializer.Serialize(model), DateTime.UtcNow);
    }

    private static Dictionary<string, object?> Model(params (string Key, string Value)[] values)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal) { [NotificationPublisher.RecipientNameKey] = "Mehmet Demir" };
        foreach (var (key, value) in values)
        {
            model[key] = value;
        }

        return model;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
