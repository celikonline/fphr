using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Localization;

namespace Sense.Modules.Identity.Infrastructure.Messaging;

/// <summary>Identity e-postaları: yerelleştirilmiş şablon (kaynak dosyası) + IEmailSender.</summary>
public sealed class IdentityMailer(IEmailSender sender, IStringLocalizer<SharedResource> localizer) : IIdentityMailer
{
    public Task SendInvitationAsync(string toEmail, string toName, string tenantName, string inviteUrl, DateTime expiresAtUtc, CancellationToken ct) =>
        SendAsync(toEmail, toName, EmailTemplateKeys.InvitationSubject, EmailTemplateKeys.InvitationBody, tenantName, inviteUrl, expiresAtUtc, ct);

    public Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct) =>
        SendAsync(toEmail, toName, EmailTemplateKeys.PasswordResetSubject, EmailTemplateKeys.PasswordResetBody, string.Empty, resetUrl, expiresAtUtc, ct);

    public Task SendWelcomeAsync(string toEmail, string toName, string tenantName, string loginUrl, CancellationToken ct) =>
        SendAsync(toEmail, toName, EmailTemplateKeys.WelcomeSubject, EmailTemplateKeys.WelcomeBody, tenantName, loginUrl, null, ct);

    private Task SendAsync(string toEmail, string toName, string subjectKey, string bodyKey, string tenantName, string url, DateTime? expiresAtUtc, CancellationToken ct)
    {
        var subject = Render(localizer[subjectKey], toName, tenantName, url, expiresAtUtc);
        var body = Render(localizer[bodyKey], toName, tenantName, url, expiresAtUtc);
        return sender.SendAsync(new EmailMessage(toEmail, toName, subject, body), ct);
    }

    private static string Render(string template, string name, string tenant, string url, DateTime? expires) =>
        template
            .Replace(EmailTemplateKeys.Placeholders.Name, name, StringComparison.Ordinal)
            .Replace(EmailTemplateKeys.Placeholders.Tenant, tenant, StringComparison.Ordinal)
            .Replace(EmailTemplateKeys.Placeholders.Url, url, StringComparison.Ordinal)
            .Replace(EmailTemplateKeys.Placeholders.Expires, expires?.ToString(CultureInfo.CurrentCulture) ?? string.Empty, StringComparison.Ordinal);
}

/// <summary>Ön yüz bağlantı şablonları (appsettings "Frontend").</summary>
public sealed class FrontendOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string InviteAcceptPath { get; set; } = string.Empty;

    public string PasswordResetPath { get; set; } = string.Empty;

    public string LoginPath { get; set; } = string.Empty;
}

public sealed class FrontendLinks(IOptions<FrontendOptions> options) : IFrontendLinks
{
    private const string TokenPlaceholder = "{token}";
    private const string SlugPlaceholder = "{slug}";

    public string InviteAccept(string token) => Combine(options.Value.InviteAcceptPath.Replace(TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal));

    public string PasswordReset(string token) => Combine(options.Value.PasswordResetPath.Replace(TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal));

    public string Login(string tenantSlug) => Combine(options.Value.LoginPath.Replace(SlugPlaceholder, Uri.EscapeDataString(tenantSlug), StringComparison.Ordinal));

    private string Combine(string path) => string.Concat(options.Value.BaseUrl.TrimEnd('/'), path);
}
