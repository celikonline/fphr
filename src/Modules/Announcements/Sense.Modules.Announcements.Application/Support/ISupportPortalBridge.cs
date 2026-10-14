using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Application.Support;

public interface ISupportPortalBridge
{
    bool Enabled { get; }
    Task<Guid> CreateAsync(SupportTicket ticket, CancellationToken cancellationToken);
    Task<PortalSupportTicketDto> ReadAsync(SupportTicket ticket, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, PortalSupportTicketDto>> ListAsync(IReadOnlyList<SupportTicket> tickets, CancellationToken cancellationToken);
    Task AddCommentAsync(SupportTicket ticket, string body, Guid idempotencyKey, CancellationToken cancellationToken);
}

public sealed record PortalSupportCommentDto(string Id, string Body, DateTime CreatedAt, string? AuthorName = null, string? Source = null);
public sealed record PortalSupportTicketDto(string SyncState, IReadOnlyList<PortalSupportCommentDto> Comments,
    string? TicketId = null, string? Number = null, string? Status = null, bool? CommentsTruncated = null);
