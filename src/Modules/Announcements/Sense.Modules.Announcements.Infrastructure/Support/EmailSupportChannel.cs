using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Announcements.Infrastructure.Support;

/// <summary>
/// Default <see cref="ISupportChannel"/> (11.3.13 EmailSupportChannel): mails the ticket to SupportOptions.SupportEmail
/// through the shared IEmailSender (SMTP / Null provider). The mail is internal to the Sense support team, so it is
/// plain text and not localized. Delivery failures are logged, never thrown: the ticket row is already recorded and
/// stays visible in the user's "Taleplerim" list.
/// </summary>
public sealed partial class EmailSupportChannel(
    IEmailSender emailSender, IOptions<SupportOptions> options, ILogger<EmailSupportChannel> logger) : ISupportChannel
{
    private const string SubjectFormat = "[SenseİK Destek] {0}";

    public async Task SubmitTicketAsync(SupportTicket ticket, SupportRequester requester, CancellationToken ct)
    {
        var supportEmail = options.Value.SupportEmail;
        if (string.IsNullOrWhiteSpace(supportEmail))
        {
            Log.NotConfigured(logger, ticket.Id);
            return;
        }

        var message = new EmailMessage(
            supportEmail.Trim(),
            null,
            string.Format(CultureInfo.InvariantCulture, SubjectFormat, ticket.Subject),
            BuildBody(ticket, requester));

        try
        {
            await emailSender.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.SendFailed(logger, ex, ticket.Id);
        }
    }

    internal static string BuildBody(SupportTicket ticket, SupportRequester requester)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Ticket: {ticket.Id}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Tenant: {requester.TenantId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"User: {requester.DisplayName} <{requester.Email}> ({requester.UserId})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Category: {ticket.Category}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Page: {ticket.PageContext ?? "-"}");
        sb.AppendLine();
        sb.AppendLine(ticket.Body);
        return sb.ToString();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Support:SupportEmail is not configured; support ticket {TicketId} was recorded but not e-mailed")]
        public static partial void NotConfigured(ILogger logger, Guid ticketId);

        [LoggerMessage(EventId = 7102, Level = LogLevel.Error, Message = "Support ticket {TicketId} could not be e-mailed to the support inbox")]
        public static partial void SendFailed(ILogger logger, Exception exception, Guid ticketId);
    }
}
