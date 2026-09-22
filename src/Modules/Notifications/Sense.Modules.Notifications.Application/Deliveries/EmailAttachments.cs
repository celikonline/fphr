using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Notifications.Application.Deliveries;

/// <summary>
/// Template-specific files attached right before an e-mail is sent (payslip PDF, work certificate PDF). Built at send
/// time from the owning module's Contracts port, so no document bytes sit in the delivery queue and a retry renders the
/// current document. A result with <see cref="EmailAttachmentResult.SkipReason"/> skips the delivery (e.g. the
/// payslip no longer exists).
/// </summary>
public interface IEmailAttachmentProvider
{
    string TemplateKey { get; }

    Task<EmailAttachmentResult> GetAsync(Guid tenantId, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default);
}

public sealed record EmailAttachmentResult(IReadOnlyList<EmailAttachment> Attachments, string? SkipReason)
{
    public static EmailAttachmentResult With(EmailAttachment attachment) => new([attachment], null);

    public static EmailAttachmentResult SkipWith(string reason) => new([], reason);
}
