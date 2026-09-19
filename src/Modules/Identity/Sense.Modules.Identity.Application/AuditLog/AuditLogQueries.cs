using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.AuditLog;

/// <summary>
/// S-1 sözleşmesi (status/faz-01/TALIMAT.md tur 3): <c>GET /api/v1/identity/audit-log</c>.
/// <c>Action</c> serbest metin değil, FE'nin bildiği sabit bir küme (create|update|delete|login|invite|role_change);
/// bilinmeyen bir değer boş sonuç döner (yok sayılan filtre yerine sessizce boş — yanlış yazılmış bir eylem adının
/// "tüm kayıtları" göstermesindense hiçbir şey göstermesi tercih edilir).
/// senseik-049: artık tek bir okuma deposu değil, kayıtlı HER <see cref="IAuditLogSource"/> sorgulanır (Identity +
/// Employee/Leave/Workflow, bkz. o arayüzün doc comment'i) - modüller Contracts üzerinden konuşur kuralı gereği
/// Identity bu modüllerin somut DbContext'ini bilmez, yalnız bu ortak arayüzü.
/// </summary>
[RequiresPermission(IdentityPermissions.AuditLogView)]
public sealed record GetAuditLogQuery(DateTimeOffset? From, DateTimeOffset? To, string? Actor, string? Action, int Page, int PageSize)
    : IQuery<PagedResult<AuditLogEntryDto>>;

public sealed class GetAuditLogHandler(IEnumerable<IAuditLogSource> sources, ICurrentUser currentUser, IPermissionService permissions)
    : IQueryHandler<GetAuditLogQuery, PagedResult<AuditLogEntryDto>>
{
    /// <summary>Sözleşmede sabit; sorgu string'i "page"/"pageSize" göndermezse (veya geçersizse) bu varsayılana düşer.</summary>
    public const int DefaultPageSize = 25;

    public async Task<Result<PagedResult<AuditLogEntryDto>>> Handle(GetAuditLogQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? DefaultPageSize : query.PageSize;

        // Her kaynak kendi filtresini DB tarafında uygular (tarih/actor/action); yalnız bellek içi birleştirme +
        // sıralama + sayfalama burada olur - tenant başına audit hacmi bu aşamada DB'de UNION gerektirecek boyutta
        // değil (senseik-049, bkz. IAuditLogSource'un doc comment'i).
        var filter = new AuditLogFilter(query.From, query.To, query.Actor, query.Action);
        var perSource = await Task.WhenAll(sources.Select(s => s.QueryAsync(filter, cancellationToken))).ConfigureAwait(false);
        var merged = perSource.SelectMany(entries => entries).OrderByDescending(e => e.OccurredAt).ToList();

        var pageItems = merged
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogEntryDto(e.Id, e.OccurredAt, e.Actor, e.Action, e.Resource, e.ResourceId, e.Details, e.Source))
            .ToList();
        var result = new PagedResult<AuditLogEntryDto>(pageItems, page, pageSize, merged.Count);

        var canViewSensitive = currentUser.UserId is { } userId
            && await permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, cancellationToken).ConfigureAwait(false);

        return canViewSensitive ? result : result.Map(Mask);
    }

    private static AuditLogEntryDto Mask(AuditLogEntryDto entry) => entry with { Details = AuditLogMasking.MaskedDetails };
}

public static class AuditLogMasking
{
    /// <summary>"identity.audit.sensitive_view" izni olmayan çağıranlara gösterilen sabit yer tutucu.</summary>
    public const string MaskedDetails = "[gizli - bu ayrıntıyı görmek için ek izin gerekir]";
}
