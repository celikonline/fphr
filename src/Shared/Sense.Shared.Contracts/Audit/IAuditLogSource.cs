namespace Sense.Shared.Contracts.Audit;

/// <summary>
/// senseik-049: Identity'nin denetim kaydı ekranı (<c>GET /api/v1/identity/audit-log</c>) artık tek bir modülün
/// tablosunu değil, "audit_log_entries" tablosu olan HER modülü okur. Modüller yalnız Contracts üzerinden konuşur
/// kuralı gereği Identity, Employee/Leave/Workflow'un somut DbContext'ini bilmez - her modül kendi şemasındaki
/// tabloyu bu arayüz üzerinden sağlar (bkz. Sense.Shared.Infrastructure.Persistence.Audit.ModuleAuditLogSource,
/// Identity için ise IdentityAuditLogSource - Identity'nin AuditLogEntry'si senseik-018'den beri ayrı bir CLR tipi,
/// migration geçmişini bozmamak için bu turda taşınmadı). GetAuditLogHandler tüm kayıtlı IAuditLogSource'ları
/// filtreler, bellek içinde birleştirir/sıralar/sayfalar (bkz. o handler'ın doc comment'i - tenant başına audit
/// hacmi bu aşamada DB'de UNION gerektirecek boyutta değil).
/// </summary>
public interface IAuditLogSource
{
    Task<IReadOnlyList<AuditLogEntrySnapshot>> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}

/// <summary>Denetim kaydı sorgu filtresi - S-1 sözleşmesindeki alanların aynısı (bkz. Identity.Application.AuditLog.GetAuditLogQuery).</summary>
public sealed record AuditLogFilter(DateTimeOffset? From, DateTimeOffset? To, string? Actor, string? Action);

/// <summary>Bir kaynağın (modülün) döndürdüğü tek denetim kaydı - okuma tarafının modül-bağımsız DTO'su.</summary>
public sealed record AuditLogEntrySnapshot(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Resource,
    string? ResourceId,
    string Details,
    string? Source);
