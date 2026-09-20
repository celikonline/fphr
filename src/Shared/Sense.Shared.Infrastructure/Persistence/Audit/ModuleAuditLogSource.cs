using Microsoft.EntityFrameworkCore;
using Sense.Shared.Contracts.Audit;

namespace Sense.Shared.Infrastructure.Persistence.Audit;

/// <summary>
/// senseik-049: <see cref="IAuditLogSource"/>'un tek, paylaşımlı implementasyonu - bu paylaşımlı <see cref="AuditLogEntry"/>
/// tipini kullanan HER modül (Employee/Leave/Workflow, ...) için aynı kod. Identity kapsam dışı (bkz.
/// Sense.Modules.Identity.Infrastructure.Persistence.IdentityAuditLogSource - Identity'nin AuditLogEntry'si ayrı bir
/// CLR tipi, bkz. AuditLogEntry.cs'in doc comment'i). Modül DI kaydı: <c>services.AddScoped&lt;IAuditLogSource,
/// ModuleAuditLogSource&lt;EmployeeDbContext&gt;&gt;()</c>.
/// </summary>
public sealed class ModuleAuditLogSource<TContext>(TContext db) : IAuditLogSource
    where TContext : DbContext
{
    public async Task<IReadOnlyList<AuditLogEntrySnapshot>> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Set<AuditLogEntry>().AsNoTracking().AsQueryable();

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
            // Identity.Infrastructure'daki LikePattern'ı burada kullanamayız (Shared, Identity'ye bağımlı olamaz -
            // ters yön); tek satırlık aynı desen, paylaşımlı bir yardımcıya değecek kadar tekrar etmiyor.
            var pattern = $"%{filter.Actor.Trim()}%";
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
