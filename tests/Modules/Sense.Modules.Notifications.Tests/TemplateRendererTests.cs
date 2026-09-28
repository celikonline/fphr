using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Catalog;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>Embedded Scriban templates: every catalog entry renders in every language, escaping and fallbacks behave.</summary>
public sealed class TemplateRendererTests
{
    private const string BaseUrl = "https://app.senseik.test";

    private static readonly ScribanNotificationTemplateRenderer Renderer = new(
        Options.Create(new NotificationsOptions()),
        Options.Create(new NotificationsFrontendOptions { BaseUrl = BaseUrl + "/" }));

    public static TheoryData<string, string, string> AllTemplates()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var definition in NotificationCatalog.All)
        {
            // Push has no "{key}.push.{lang}.sbn" of its own: PushDeliverySender re-renders with the in_app
            // channel and reuses that text (push-bildirimleri-tasarim.md §3.4) - nothing to assert here per-key.
            foreach (var channel in definition.DefaultChannels.Where(c => c != NotificationChannels.Push))
            {
                foreach (var language in ScribanNotificationTemplateRenderer.SupportedLanguages)
                {
                    data.Add(definition.Key, channel, language);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EveryCatalogTemplate_RendersSubjectAndBody_InEveryLanguage(string templateKey, string channel, string language)
    {
        var rendered = Renderer.Render(templateKey, channel, language, SampleModel());

        rendered.Subject.ShouldNotBeNullOrWhiteSpace();
        rendered.Body.ShouldNotBeNullOrWhiteSpace();
        rendered.Subject.ShouldNotContain("{{");
        rendered.Body.ShouldNotContain("{{");
        if (channel == NotificationChannels.Email)
        {
            rendered.HtmlBody.ShouldNotBeNull();
            rendered.HtmlBody.ShouldContain(BaseUrl + ScribanNotificationTemplateRenderer.SettingsPath);
            rendered.Body.ShouldNotContain("<p>");
        }
        else
        {
            rendered.HtmlBody.ShouldBeNull();
        }
    }

    [Fact]
    public void RequestTypeName_IsLocalized_PerLanguage()
    {
        Renderer.Render(NotificationTemplateKeys.RequestApproved, NotificationChannels.InApp, "tr", SampleModel()).Subject
            .ShouldBe("İzin talebiniz onaylandı");
        Renderer.Render(NotificationTemplateKeys.RequestApproved, NotificationChannels.InApp, "de-DE", SampleModel()).Subject
            .ShouldBe("Ihr Antrag (Urlaub) wurde genehmigt");
    }

    [Theory]
    [InlineData("ExpenseClaim", "Harcama talebiniz oluşturuldu")]
    [InlineData("Advance", "Avans talebiniz oluşturuldu")]
    [InlineData("Requisition", "Kadro talebiniz oluşturuldu")]
    [InlineData("Overtime", "Fazla mesai talebiniz oluşturuldu")]
    [InlineData("VisaDocument", "Vize belgesi talebiniz oluşturuldu")]
    public void EverySubmittedRequestType_HasReadableTurkishLabel_NotTheRawEnumName(string requestType, string expectedSubject)
    {
        var model = SampleModel();
        model[WorkflowTemplateModel.RequestType] = requestType;

        Renderer.Render(NotificationTemplateKeys.RequestCreated, NotificationChannels.InApp, "tr", model).Subject.ShouldBe(expectedSubject);
    }

    [Fact]
    public void UnsupportedLanguage_FallsBackToDefaultLanguage()
    {
        var rendered = Renderer.Render(NotificationTemplateKeys.RequestCreated, NotificationChannels.InApp, "fr", SampleModel());

        rendered.Subject.ShouldBe("İzin talebiniz oluşturuldu");
    }

    [Fact]
    public void UnknownRequestType_UsesTheRawCode()
    {
        var model = SampleModel();
        model[WorkflowTemplateModel.RequestType] = "VisaRequest";

        Renderer.Render(NotificationTemplateKeys.RequestCreated, NotificationChannels.InApp, "en", model).Subject
            .ShouldBe("Your VisaRequest request was created");
    }

    [Fact]
    public void Email_HtmlEscapesUserProvidedValues()
    {
        var model = SampleModel();
        model[NotificationPublisher.RecipientNameKey] = "<script>alert(1)</script>";

        var rendered = Renderer.Render(NotificationTemplateKeys.RequestCreated, NotificationChannels.Email, "en", model);

        rendered.HtmlBody.ShouldNotContain("<script>");
        rendered.HtmlBody.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void TaskAssigned_WithDecisionLink_RendersBothButtons_AndExpiry()
    {
        var model = SampleModel();
        model[WorkflowTemplateModel.DecisionUrl] = "https://app.senseik.test/approve/tok_123";
        model[WorkflowTemplateModel.DecisionExpiresAt] = new DateTime(2026, 9, 22, 14, 30, 0, DateTimeKind.Utc);

        var rendered = Renderer.Render(NotificationTemplateKeys.ApprovalTaskAssigned, NotificationChannels.Email, "tr", model);

        rendered.Subject.ShouldBe("Onayınız bekleniyor: İzin talebi");
        rendered.HtmlBody.ShouldContain("href=\"https://app.senseik.test/approve/tok_123\"");
        rendered.HtmlBody.ShouldContain("href=\"https://app.senseik.test/app/inbox\"");
        rendered.Body.ShouldContain("Onayla veya reddet: https://app.senseik.test/approve/tok_123");
        rendered.Body.ShouldContain("22.09.2026 14:30");
        rendered.Body.ShouldContain("Ayşe Yılmaz");
    }

    [Fact]
    public void TaskAssigned_ForRoleQueue_MentionsTheQueue_AndHasNoDecisionLink()
    {
        var model = SampleModel();
        model[WorkflowTemplateModel.IsDirectAssignee] = "false";

        var rendered = Renderer.Render(NotificationTemplateKeys.ApprovalTaskAssigned, NotificationChannels.Email, "en", model);

        rendered.Body.ShouldContain("approval queue of a role");
        rendered.HtmlBody.ShouldNotContain("/approve/");
    }

    [Fact]
    public void HtmlToText_TurnsLinksAndBlocksIntoReadableText()
    {
        var text = ScribanNotificationTemplateRenderer.HtmlToText("<p>Hi &amp; welcome</p><p><a href=\"https://x.test/a?b=1&amp;c=2\">Open</a></p>");

        text.ShouldBe("Hi & welcome\n\nOpen: https://x.test/a?b=1&c=2");
    }

    [Theory]
    [InlineData(NotificationTemplateKeys.AssetAssigned)]
    [InlineData(NotificationTemplateKeys.AssetReturned)]
    [InlineData(NotificationTemplateKeys.AssetUnreturnedAtTermination)]
    public void AssetTemplates_ShowAssetNameWhenPresent_AndFallBackToNamelessTextWhenMissingOrEmpty(string templateKey)
    {
        foreach (var channel in new[] { NotificationChannels.InApp, NotificationChannels.Email })
        {
            foreach (var language in ScribanNotificationTemplateRenderer.SupportedLanguages)
            {
                var named = SampleModel();
                named["asset_name"] = "MacBook <Pro> 14";
                named["employee_name"] = "Ayşe Yılmaz";
                var withName = Renderer.Render(templateKey, channel, language, named);
                withName.Subject.ShouldContain("MacBook <Pro> 14");
                withName.Body.ShouldContain("MacBook <Pro> 14");
                if (channel == NotificationChannels.Email)
                {
                    withName.HtmlBody!.ShouldContain("MacBook &lt;Pro&gt; 14");
                }

                var nameless = SampleModel();
                nameless["employee_name"] = "Ayşe Yılmaz";
                var withoutName = Renderer.Render(templateKey, channel, language, nameless);
                foreach (var emptyName in new object?[] { null, string.Empty })
                {
                    var model = SampleModel();
                    model["employee_name"] = "Ayşe Yılmaz";
                    model["asset_name"] = emptyName;
                    var rendered = Renderer.Render(templateKey, channel, language, model);
                    rendered.Subject.ShouldBe(withoutName.Subject);
                    rendered.Body.ShouldBe(withoutName.Body);
                    rendered.Subject.ShouldNotContain("()");
                    rendered.Body.ShouldNotContain("MacBook");
                }
            }
        }
    }

    private static Dictionary<string, object?> SampleModel() => new(StringComparer.Ordinal)
    {
        [NotificationPublisher.RecipientNameKey] = "Mehmet Demir",
        [NotificationPublisher.ActionPathKey] = WorkflowTemplateModel.InboxPath,
        [WorkflowTemplateModel.RequestType] = "Leave",
        [WorkflowTemplateModel.RequesterName] = "Ayşe Yılmaz",
        [WorkflowTemplateModel.StepNumber] = "1",
        [WorkflowTemplateModel.RejectedAtStep] = "2",
        [WorkflowTemplateModel.IsDirectAssignee] = "true",
    };
}
