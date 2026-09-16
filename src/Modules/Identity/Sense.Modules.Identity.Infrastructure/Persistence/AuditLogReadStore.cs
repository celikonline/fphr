using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.AuditLog;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Infrastructure.Querying;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

/// <summary>S-1: <c>GET /api/v1/identity/audit-log</c> okuma tarafı. Tenant izolasyonu global query filter ile otomatik.</summary>
public sealed class AuditLogReadStore(IdentityDbContext db) : IAuditLogReadStore
{
    public async Task<PagedResult<AuditLogEntryDto>> ListAsync(DateTimeOffset? from, DateTimeOffset? to, string? actor, string? action, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (from is { } fromValue)
        {
            query = query.Where(e => e.OccurredAt >= fromValue);
        }

        if (to is { } toValue)
        {
            query = query.Where(e => e.OccurredAt <= toValue);
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            var pattern = LikePattern.Contains(actor);
            query = query.Where(e => EF.Functions.ILike(e.Actor, pattern));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        query = query.OrderByDescending(e => e.OccurredAt);

        var paging = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await query.ToPagedResultAsync(paging, ct).ConfigureAwait(false);
        return result.Map(e => new AuditLogEntryDto(e.Id, e.OccurredAt, e.Actor, e.Action, e.Resource, e.ResourceId, e.Details));
    }
}
