namespace Sense.Shared.Contracts.Notifications;

public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>E-posta gönderim portu (SMTP/Resend adaptörleri Infrastructure'da).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailOptions
{
    public string Provider { get; set; } = EmailProviders.Smtp;

    public string FromName { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool UseSsl { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}

public static class EmailProviders
{
    public const string Smtp = "Smtp";
    public const string None = "None";
}
