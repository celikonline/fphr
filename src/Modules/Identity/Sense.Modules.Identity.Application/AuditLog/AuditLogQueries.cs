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
/// </summary>
[RequiresPermission(IdentityPermissions.AuditLogView)]
public sealed record GetAuditLogQuery(DateTimeOffset? From, DateTimeOffset? To, string? Actor, string? Action, int Page, int PageSize)
    : IQuery<PagedResult<AuditLogEntryDto>>;

/// <summary>Okuma tarafı portu (Onion: Application tanımlar, Infrastructure implement eder).</summary>
public interface IAuditLogReadStore
{
    Task<PagedResult<AuditLogEntryDto>> ListAsync(DateTimeOffset? from, DateTimeOffset? to, string? actor, string? action, int page, int pageSize, CancellationToken ct);
}

public sealed class GetAuditLogHandler(IAuditLogReadStore store, ICurrentUser currentUser, IPermissionService permissions)
    : IQueryHandler<GetAuditLogQuery, PagedResult<AuditLogEntryDto>>
{
    /// <summary>Sözleşmede sabit; sorgu string'i "page"/"pageSize" göndermezse (veya geçersizse) bu varsayılana düşer.</summary>
    public const int DefaultPageSize = 25;

    public async Task<Result<PagedResult<AuditLogEntryDto>>> Handle(GetAuditLogQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? DefaultPageSize : query.PageSize;

        var result = await store.ListAsync(query.From, query.To, query.Actor, query.Action, page, pageSize, cancellationToken).ConfigureAwait(false);

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
