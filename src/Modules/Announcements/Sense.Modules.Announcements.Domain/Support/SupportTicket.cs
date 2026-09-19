using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain.Support;

public enum SupportTicketCategory
{
    Question = 0,
    Problem = 1,
    Suggestion = 2,
    Other = 3,
}

public enum SupportTicketStatus
{
    Open = 0,
    Answered = 1,
    Closed = 2,
}

/// <summary>How the ticket reached the Sense support team. Only e-mail exists today (live chat providers are Faz 8).</summary>
public enum SupportTicketChannel
{
    Email = 0,
}

/// <summary>
/// "Desteğe bağlan" request (11.3.13 SupportTicket, trimmed: no ExternalRef/Attachments). Tenant-scoped: each tenant
/// only sees its own tickets, and the "Taleplerim" list further filters by UserId. Answering/closing happens on the
/// Sense side (platform support queue is Faz 8), so Status stays Open in this package.
/// </summary>
public sealed class SupportTicket : TenantAggregateRoot<Guid>
{
    private SupportTicket()
    {
    }

    private SupportTicket(Guid id, Guid tenantId, Guid userId)
        : base(id, tenantId)
    {
        UserId = userId;
        Status = SupportTicketStatus.Open;
    }

    public Guid UserId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public SupportTicketCategory Category { get; private set; }

    public SupportTicketStatus Status { get; private set; }

    public SupportTicketChannel Channel { get; private set; }

    /// <summary>The app route the user was on when opening the ticket (nullable).</summary>
    public string? PageContext { get; private set; }

    public static Result<SupportTicket> Create(
        Guid tenantId, Guid userId, string subject, string body, SupportTicketCategory category, SupportTicketChannel channel, string? pageContext)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Error.Validation(AnnouncementsDomainErrors.TicketSubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Error.Validation(AnnouncementsDomainErrors.TicketBodyRequired);
        }

        return new SupportTicket(Guid.CreateVersion7(), tenantId, userId)
        {
            Subject = Guard.MaxLength(subject.Trim(), AnnouncementsLimits.TicketSubjectMaxLength),
            Body = Guard.MaxLength(body.Trim(), AnnouncementsLimits.TicketBodyMaxLength),
            Category = category,
            Channel = channel,
            PageContext = string.IsNullOrWhiteSpace(pageContext) ? null : Guard.MaxLength(pageContext.Trim(), AnnouncementsLimits.PageContextMaxLength),
        };
    }
}
