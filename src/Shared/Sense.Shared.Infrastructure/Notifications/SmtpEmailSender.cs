using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Shared.Infrastructure.Notifications;

/// <summary>MailKit ile SMTP gönderimi (lokal: Mailpit 127.0.0.1:1025).</summary>
public sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var cfg = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.ToAddress, message.ToAddress));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { TextBody = message.TextBody };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            body.HtmlBody = message.HtmlBody;
        }

        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(cfg.Smtp.Host, cfg.Smtp.Port, cfg.Smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cfg.Smtp.Username))
        {
            await client.AuthenticateAsync(cfg.Smtp.Username, cfg.Smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        Log.Sent(logger, message.ToAddress, message.Subject);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "E-mail sent to {To}: {Subject}")]
        public static partial void Sent(ILogger logger, string to, string subject);
    }
}

/// <summary>E-posta kapalıysa (Provider=None) yalnız loglar.</summary>
public sealed partial class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Log.Skipped(logger, message.ToAddress, message.Subject);
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1401, Level = LogLevel.Warning, Message = "E-mail provider disabled; skipped mail to {To}: {Subject}")]
        public static partial void Skipped(ILogger logger, string to, string subject);
    }
}
