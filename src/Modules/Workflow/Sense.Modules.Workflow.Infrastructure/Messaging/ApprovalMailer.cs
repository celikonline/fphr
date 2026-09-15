using System.Globalization;
using Sense.Modules.Workflow.Application;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Workflow.Infrastructure.Messaging;

/// <summary>
/// Sends the "you have a pending approval" email carrying the single-use decision link. Minimal inline TR-only plain
/// text body (no templating system) - real per-tenant/localized templating is Notifications' job (see
/// Application.IApprovalMailer's doc comment, plan §9 kapsam dışı).
/// </summary>
public sealed class ApprovalMailer(IEmailSender sender) : IApprovalMailer
{
    public Task SendDecisionRequestAsync(
        string toEmail, string toName, string requestType, string decisionUrl, DateTime expiresAtUtc, CancellationToken ct)
    {
        const string subjectPrefix = "Onayınız bekleniyor";
        var subject = $"{subjectPrefix}: {requestType}";
        var body = string.Join(
            Environment.NewLine + Environment.NewLine,
            $"Merhaba {toName},",
            $"\"{requestType}\" türünde bir talep onayınızı bekliyor.",
            $"Onaylamak veya reddetmek için: {decisionUrl}",
            $"Bu bağlantı {expiresAtUtc.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} (UTC) tarihine kadar geçerlidir.");

        return sender.SendAsync(new EmailMessage(toEmail, toName, subject, body), ct);
    }
}
