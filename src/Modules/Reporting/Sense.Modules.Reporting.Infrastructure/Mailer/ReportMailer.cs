using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Sense.Modules.Reporting.Application;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Localization;

namespace Sense.Modules.Reporting.Infrastructure.Mailer;

/// <summary>"Your report is ready" email: localized template (SharedResource) + IEmailSender, mirrors Identity's IdentityMailer.
/// R0 always links to the app (no attachment) - the file lives behind reports.my.executions-gated download endpoint.</summary>
public sealed class ReportMailer(IEmailSender sender, IStringLocalizer<SharedResource> localizer, IConfiguration configuration) : IReportMailer
{
    public Task SendReportReadyAsync(string toEmail, string? toName, Guid executionId, string reportKey, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var url = $"{baseUrl}/app/reports/executions/{executionId}";
        var name = toName ?? string.Empty;

        var subject = Render(localizer[EmailTemplateKeys.ReportReadySubject], name, url);
        var body = Render(localizer[EmailTemplateKeys.ReportReadyBody], name, url);
        return sender.SendAsync(new EmailMessage(toEmail, toName, subject, body), cancellationToken);
    }

    private static string Render(string template, string name, string url) =>
        template
            .Replace(EmailTemplateKeys.Placeholders.Name, name, StringComparison.Ordinal)
            .Replace(EmailTemplateKeys.Placeholders.Url, url, StringComparison.Ordinal);
}
