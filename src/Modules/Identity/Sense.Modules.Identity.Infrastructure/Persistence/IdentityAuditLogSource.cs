using Microsoft.EntityFrameworkCore;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Infrastructure.Querying;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// senseik-049: <c>GET /api/v1/identity/audit-log</c> artık yalnız Identity'nin değil, <see cref="IAuditLogSource"/>
/// kaydeden HER modülün (Employee/Leave/Workflow, bkz. Sense.Shared.Infrastructure.Persistence.Audit.
/// ModuleAuditLogSource) kayıtlarını GetAuditLogHandler'da birleştirir. Bu sınıf, o birleştirmede Identity'nin payını
/// sağlar - Identity'nin kendi <see cref="AuditLogEntry"/> tipi (senseik-018) Employee/Leave/Workflow'un paylaşımlı
/// AuditLogEntry'siyle AYNI CLR TİPİ DEĞİL (bkz. o paylaşımlı tipin doc comment'i - taşımak migration/veri kaybı
/// riski taşırdı), bu yüzden eski AuditLogReadStore'un yerini alan ince bir adaptör.
/// </summary>
public sealed class IdentityAuditLogSource(IdentityDbContext db) : IAuditLogSource
{
    public async Task<IReadOnlyList<AuditLogEntrySnapshot>> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var query = db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (filter.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(e => e.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            var pattern = LikePattern.Contains(filter.Actor);
            query = query.Where(e => EF.Functions.ILike(e.Actor, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(e => e.Action == filter.Action);
        }

        return await query
            .Select(e => new AuditLogEntrySnapshot(e.Id, e.OccurredAt, e.Actor, e.Action, e.Resource, e.ResourceId, e.Details, e.Source))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
