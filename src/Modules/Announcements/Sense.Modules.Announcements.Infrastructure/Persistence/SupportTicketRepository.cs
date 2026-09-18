using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

public sealed class SupportTicketRepository(AnnouncementsDbContext context) : ISupportTicketRepository
{
    public void Add(SupportTicket ticket) => context.SupportTickets.Add(ticket);

    public async Task<SupportTicket?> GetByIdAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken = default) =>
        await context.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<SupportTicket>> ListByRequesterAsync(Guid tenantId, Guid requestedByUserId, CancellationToken cancellationToken = default) =>
        await context.SupportTickets
            .Where(t => t.TenantId == tenantId && t.RequestedByUserId == requestedByUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupportTicket>> ListAsync(Guid tenantId, SupportTicketStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.SupportTickets.Where(t => t.TenantId == tenantId);
        if (status is { } s)
        {
            query = query.Where(t => t.Status == s);
        }

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
    }
}

public sealed class HelpSearchLogRepository(AnnouncementsDbContext context) : IHelpSearchLogRepository
{
    public void Add(HelpSearchLog log) => context.HelpSearchLogs.Add(log);
}
