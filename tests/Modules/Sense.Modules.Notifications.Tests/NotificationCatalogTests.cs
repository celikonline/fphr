using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Catalog;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>Zaman &amp; devam + İK eksikleri anahtarları: katalogda ve her kanal × dil için şablonu olmalı.</summary>
public sealed class NotificationCatalogTests
{
    private static readonly ScribanNotificationTemplateRenderer Renderer = new(
        Options.Create(new NotificationsOptions()),
        Options.Create(new NotificationsFrontendOptions { BaseUrl = "https://app.senseik.test/" }));

    public static TheoryData<string> NewKeys() => new()
    {
        NotificationTemplateKeys.AttendanceMissingPunchReminder,
        NotificationTemplateKeys.PayslipAcknowledgementReminder,
        NotificationTemplateKeys.PayslipDocumentUploaded,
        NotificationTemplateKeys.HrMessage,
        NotificationTemplateKeys.CompanyEventInvitation,
        NotificationTemplateKeys.ApprovalDelegationAssigned,
        NotificationTemplateKeys.PayslipObjectionAccepted,
    };

    [Theory]
    [MemberData(nameof(NewKeys))]
    public void Every_new_key_has_templates_for_all_channels_and_languages(string key)
    {
        var definition = NotificationCatalog.Find(key);

        definition.ShouldNotBeNull();
        definition.DefaultChannels.ShouldBe([NotificationChannels.Email, NotificationChannels.InApp]);

        foreach (var channel in definition.DefaultChannels)
        {
            foreach (var language in ScribanNotificationTemplateRenderer.SupportedLanguages)
            {
                var rendered = Renderer.Render(key, channel, language, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [NotificationPublisher.RecipientNameKey] = "Mehmet Demir",
                });

                rendered.Subject.ShouldNotBeNullOrWhiteSpace($"{key}.{channel}.{language}");
                rendered.Body.ShouldNotBeNullOrWhiteSpace($"{key}.{channel}.{language}");
            }
        }
    }

    [Fact]
    public void Catalog_keys_are_unique() =>
        NotificationCatalog.All.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(NotificationCatalog.All.Count);
}
